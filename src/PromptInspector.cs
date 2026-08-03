#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LlamaLink;

public sealed record PromptInspection(
    LlamaBackendKind Backend,
    string Endpoint,
    string TemplateDescription,
    string PayloadJson,
    string Transcript,
    string TokenPreview,
    int EstimatedTokens);

public static class PromptInspector
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly Regex PreviewTokenRegex = new(
        @"\r\n|\r|\n|[ \t]+|[\p{L}\p{N}_]+|[^\s\p{L}\p{N}]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static PromptInspection Build(
        LlamaBackendKind backend,
        string endpoint,
        IReadOnlyList<ChatHistoryMessage> messages,
        IReadOnlyDictionary<string, object> payload)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(payload);

        var transcript = BuildTranscript(messages);
        return new PromptInspection(
            backend,
            endpoint,
            GetTemplateDescription(backend),
            JsonSerializer.Serialize(payload, WriteOptions),
            transcript,
            BuildTokenPreview(transcript),
            EstimateTokens(transcript));
    }

    public static string BuildTranscript(IReadOnlyList<ChatHistoryMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return string.Join(
            "\n\n",
            messages.Select(message =>
            {
                var images = message.Images.Count == 0
                    ? ""
                    : $"\n[images: {string.Join(", ", message.Images.Select(image => image.DisplayName))}]";
                return $"[{message.Role}]\n{message.Content}{images}";
            }));
    }

    public static string BuildTokenPreview(string text, int maxTokens = 240)
    {
        var tokens = PreviewTokenRegex.Matches(text ?? "")
            .Cast<Match>()
            .Select(match => DisplayToken(match.Value))
            .Take(Math.Max(1, maxTokens))
            .ToList();
        if (tokens.Count == 0) return "(empty prompt)";

        var totalMatches = PreviewTokenRegex.Matches(text ?? "").Count;
        if (totalMatches > tokens.Count)
            tokens.Add($"... [+{totalMatches - tokens.Count} more]");
        return string.Join(" ", tokens);
    }

    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }

    public static string GetTemplateDescription(LlamaBackendKind backend)
        => backend switch
        {
            LlamaBackendKind.LlamaCpp => "llama.cpp will apply the selected model chat template server-side.",
            LlamaBackendKind.Ollama => "Ollama will apply its model message template server-side.",
            _ => "The compatible backend receives role/content messages and applies its configured template.",
        };

    private static string DisplayToken(string token)
        => token
            .Replace(" ", "␠", StringComparison.Ordinal)
            .Replace("\r", "␍", StringComparison.Ordinal)
            .Replace("\n", "↵", StringComparison.Ordinal)
            .Replace("\t", "⇥", StringComparison.Ordinal);
}
