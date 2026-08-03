#nullable enable

using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaLink;

public sealed record SpeechToolPaths(
    string FfmpegPath,
    string WhisperPath,
    string WhisperModelPath,
    string PiperPath,
    string PiperVoicePath,
    string MicrophoneName = "default");

public sealed record SpeechOperationResult(bool Success, string Content, string? OutputPath = null)
{
    public static SpeechOperationResult Error(string message) => new(false, message);
}

public static class SpeechCommandBuilder
{
    public static IReadOnlyList<string> BuildRecorderArguments(string microphoneName, string outputPath)
        => new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "dshow", "-i", $"audio={microphoneName.Trim()}",
            "-ac", "1", "-ar", "16000", outputPath,
        };

    public static IReadOnlyList<string> BuildWhisperArguments(string modelPath, string audioPath)
        => new[]
        {
            "-m", modelPath,
            "-f", audioPath,
            "--no-timestamps",
            "--print-progress", "false",
        };

    public static IReadOnlyList<string> BuildPiperArguments(string voicePath, string outputPath)
        => new[] { "--model", voicePath, "--output_file", outputPath };

    public static string ParseWhisperTranscript(string output)
    {
        var lines = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Replace(line.Trim(), @"^\[[^\]]+\]\s*", ""))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        return string.Join(" ", lines).Trim();
    }
}

public static class SpeechToolRunner
{
    public static async Task<SpeechOperationResult> TranscribeAsync(
        SpeechToolPaths paths,
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(paths.WhisperPath))
            return SpeechOperationResult.Error("whisper-cli was not found at the configured path.");
        if (!File.Exists(paths.WhisperModelPath))
            return SpeechOperationResult.Error("The configured Whisper model was not found.");
        if (!File.Exists(audioPath))
            return SpeechOperationResult.Error("The recorded audio file was not found.");

        var result = await RunAsync(
            paths.WhisperPath,
            SpeechCommandBuilder.BuildWhisperArguments(paths.WhisperModelPath, audioPath),
            input: null,
            cancellationToken);
        return result.Success
            ? new SpeechOperationResult(true, SpeechCommandBuilder.ParseWhisperTranscript(result.Content))
            : result;
    }

    public static async Task<SpeechOperationResult> SynthesizeAsync(
        SpeechToolPaths paths,
        string text,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SpeechOperationResult.Error("There is no assistant text to synthesize.");
        if (!File.Exists(paths.PiperPath))
            return SpeechOperationResult.Error("Piper was not found at the configured path.");
        if (!File.Exists(paths.PiperVoicePath))
            return SpeechOperationResult.Error("The configured Piper voice was not found.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var result = await RunAsync(
            paths.PiperPath,
            SpeechCommandBuilder.BuildPiperArguments(paths.PiperVoicePath, outputPath),
            text,
            cancellationToken);
        return result.Success && File.Exists(outputPath)
            ? new SpeechOperationResult(true, "Speech WAV created.", outputPath)
            : result;
    }

    public static Process StartRecording(SpeechToolPaths paths, string outputPath)
    {
        if (!File.Exists(paths.FfmpegPath))
            throw new FileNotFoundException("ffmpeg was not found at the configured path.", paths.FfmpegPath);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var startInfo = new ProcessStartInfo
        {
            FileName = paths.FfmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in SpeechCommandBuilder.BuildRecorderArguments(paths.MicrophoneName, outputPath))
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ffmpeg could not be started.");
        return process;
    }

    private static async Task<SpeechOperationResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? input,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = input is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            process.Start();
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input);
                process.StandardInput.Close();
            }
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return process.ExitCode == 0
                ? new SpeechOperationResult(true, stdout.Trim())
                : SpeechOperationResult.Error(string.IsNullOrWhiteSpace(stderr) ? "Speech command failed." : stderr.Trim());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
            throw;
        }
        catch (Win32Exception ex)
        {
            return SpeechOperationResult.Error(ex.Message);
        }
    }
}
