#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaLink;

public sealed record FineTuneSettings(
    string ExecutablePath,
    string BaseModelPath,
    string TrainingDataPath,
    string OutputAdapterPath,
    int ContextSize = 512,
    int BatchSize = 1,
    int MicroBatchSize = 1,
    int AdamIterations = 16,
    int Threads = 4);

public sealed record FineTuneResult(bool Success, string Message)
{
    public static FineTuneResult Error(string message) => new(false, message);
}

public static class FineTuneCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(FineTuneSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new[]
        {
            "--model-base", settings.BaseModelPath,
            "--train-data", settings.TrainingDataPath,
            "--lora-out", settings.OutputAdapterPath,
            "--ctx", Math.Clamp(settings.ContextSize, 64, 32_768).ToString(),
            "--batch", Math.Clamp(settings.BatchSize, 1, 1_024).ToString(),
            "--ubatch", Math.Clamp(settings.MicroBatchSize, 1, 1_024).ToString(),
            "--adam-iter", Math.Clamp(settings.AdamIterations, 1, 1_000_000).ToString(),
            "--threads", Math.Clamp(settings.Threads, 1, 512).ToString(),
        };
    }
}

public static class FineTuneRunner
{
    public static async Task<FineTuneResult> RunAsync(
        FineTuneSettings settings,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var validation = Validate(settings);
        if (validation is not null)
            return FineTuneResult.Error(validation);

        try
        {
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.OutputAdapterPath));
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
        }
        catch (ArgumentException)
        {
            return FineTuneResult.Error("The output adapter path is invalid.");
        }
        catch (NotSupportedException)
        {
            return FineTuneResult.Error("The output adapter path is invalid.");
        }
        catch (IOException ex)
        {
            return FineTuneResult.Error($"Could not prepare the output folder: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return FineTuneResult.Error($"Could not prepare the output folder: {ex.Message}");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = settings.ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.ExecutablePath))
                    ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in FineTuneCommandBuilder.BuildArguments(settings))
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                return FineTuneResult.Error("The fine-tune process could not be started.");

            var stdoutTask = ReadLinesAsync(process.StandardOutput, progress, cancellationToken);
            var stderrTask = ReadLinesAsync(process.StandardError, progress, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await stdoutTask;
            var error = await stderrTask;
            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(error) ? output : error;
                return FineTuneResult.Error(string.IsNullOrWhiteSpace(detail)
                    ? $"Fine-tune exited with code {process.ExitCode}."
                    : detail.Trim());
            }

            return File.Exists(settings.OutputAdapterPath)
                ? new FineTuneResult(true, $"LoRA adapter written to {settings.OutputAdapterPath}")
                : FineTuneResult.Error(
                    "Fine-tune exited successfully, but no adapter file was found at the configured output path.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Win32Exception ex)
        {
            return FineTuneResult.Error(ex.Message);
        }
        catch (IOException ex)
        {
            return FineTuneResult.Error(ex.Message);
        }
    }

    private static string? Validate(FineTuneSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ExecutablePath))
            return "Select the llama.cpp finetune executable first.";
        if (string.IsNullOrWhiteSpace(settings.BaseModelPath))
            return "Select a base GGUF model first.";
        if (string.IsNullOrWhiteSpace(settings.TrainingDataPath))
            return "Select a training data file first.";
        if (string.IsNullOrWhiteSpace(settings.OutputAdapterPath))
            return "Choose an output adapter path first.";
        if (!File.Exists(settings.ExecutablePath))
            return "The configured finetune executable was not found.";
        if (!File.Exists(settings.BaseModelPath))
            return "The selected base model was not found.";
        if (!File.Exists(settings.TrainingDataPath))
            return "The selected training data file was not found.";

        try
        {
            if (new FileInfo(settings.TrainingDataPath).Length == 0)
                return "The training data file is empty.";
            if (string.Equals(
                    Path.GetFullPath(settings.BaseModelPath),
                    Path.GetFullPath(settings.OutputAdapterPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return "The adapter output must not overwrite the base model.";
            }
        }
        catch (ArgumentException)
        {
            return "One of the fine-tune paths is invalid.";
        }
        catch (NotSupportedException)
        {
            return "One of the fine-tune paths is invalid.";
        }
        catch (IOException ex)
        {
            return $"Could not inspect the fine-tune paths: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Could not inspect the fine-tune paths: {ex.Message}";
        }

        return null;
    }

    private static async Task<string> ReadLinesAsync(
        StreamReader reader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (output.Length > 0)
                output.AppendLine();
            output.Append(line);
            if (!string.IsNullOrWhiteSpace(line))
                progress?.Report(line.Trim());
        }

        return output.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited between the check and Kill.
        }
    }
}
