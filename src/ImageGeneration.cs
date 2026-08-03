#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaLink;

public sealed record ImageGenerationSettings(
    string ExecutablePath,
    string ModelPath,
    string OutputDirectory,
    int Steps = 20,
    int Width = 512,
    int Height = 512);

public sealed record ImageGenerationResult(bool Success, string Message, string? OutputPath = null)
{
    public static ImageGenerationResult Error(string message) => new(false, message);
}

public static class ImageGenerationCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(
        ImageGenerationSettings settings,
        string prompt,
        string outputPath)
        => new[]
        {
            "--model", settings.ModelPath,
            "--prompt", prompt,
            "--output", outputPath,
            "--steps", Math.Clamp(settings.Steps, 1, 100).ToString(),
            "--width", Math.Clamp(settings.Width, 128, 2048).ToString(),
            "--height", Math.Clamp(settings.Height, 128, 2048).ToString(),
        };
}

public static class ImageGenerationService
{
    public static async Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationSettings settings,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return ImageGenerationResult.Error("/image requires a prompt.");
        if (prompt.Length > 2000)
            return ImageGenerationResult.Error("Image prompts are limited to 2,000 characters.");
        if (!File.Exists(settings.ExecutablePath))
            return ImageGenerationResult.Error("The configured image generator was not found.");
        if (!File.Exists(settings.ModelPath))
            return ImageGenerationResult.Error("The configured image generation model was not found.");

        try
        {
            Directory.CreateDirectory(settings.OutputDirectory);
            var outputPath = Path.Combine(
                settings.OutputDirectory,
                $"llamalink_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.png");
            var startInfo = new ProcessStartInfo
            {
                FileName = settings.ExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in ImageGenerationCommandBuilder.BuildArguments(settings, prompt, outputPath))
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The image generator could not be started.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            _ = await stdoutTask;
            if (process.ExitCode != 0)
                return ImageGenerationResult.Error(string.IsNullOrWhiteSpace(stderr) ? "Image generation failed." : stderr.Trim());
            if (!File.Exists(outputPath))
                return ImageGenerationResult.Error("Image generation completed without producing a PNG.");
            return new ImageGenerationResult(true, "Image generated.", outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception ex)
        {
            return ImageGenerationResult.Error(ex.Message);
        }
        catch (IOException ex)
        {
            return ImageGenerationResult.Error(ex.Message);
        }
    }
}
