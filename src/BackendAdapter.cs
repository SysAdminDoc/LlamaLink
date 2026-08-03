#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LlamaLink;

public enum LlamaBackendKind
{
    LlamaCpp,
    OpenAiCompatible,
    Ollama,
    KoboldCpp,
    TextGenerationWebUi,
}

public sealed record BackendStreamPart(string Content, bool Done);

public static class BackendAdapter
{
    public static LlamaBackendKind Parse(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "ollama" => LlamaBackendKind.Ollama,
            "kobold" or "koboldcpp" or "kobold.cpp" => LlamaBackendKind.KoboldCpp,
            "textgen" or "text-generation-webui" or "text generation webui" => LlamaBackendKind.TextGenerationWebUi,
            "llama.cpp" or "llamacpp" => LlamaBackendKind.LlamaCpp,
            _ => LlamaBackendKind.OpenAiCompatible,
        };
    }

    public static string GetHealthPath(LlamaBackendKind backend)
    {
        return backend switch
        {
            LlamaBackendKind.LlamaCpp => "/health",
            LlamaBackendKind.Ollama => "/api/tags",
            _ => "/v1/models",
        };
    }

    public static string GetChatPath(LlamaBackendKind backend)
        => backend == LlamaBackendKind.Ollama ? "/api/chat" : "/v1/chat/completions";

    public static string BuildEndpoint(string baseUrl, string relativePath)
    {
        var normalizedBase = (baseUrl ?? "").Trim().TrimEnd('/');
        var normalizedPath = "/" + (relativePath ?? "").Trim().TrimStart('/');

        if (normalizedBase.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            && normalizedPath.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedBase = normalizedBase[..^3];
        }
        else if (normalizedBase.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            && normalizedPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedBase = normalizedBase[..^4];
        }

        return normalizedBase + normalizedPath;
    }

    public static Dictionary<string, object> BuildPayload(
        LlamaBackendKind backend,
        string model,
        IReadOnlyList<ChatHistoryMessage> messages,
        double temperature,
        double topP,
        int topK,
        double repeatPenalty,
        int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var messagePayload = messages.Select(message => new
        {
            role = message.Role,
            content = message.Content,
        }).ToArray();

        if (backend == LlamaBackendKind.Ollama)
        {
            return new Dictionary<string, object>
            {
                ["model"] = model.Trim(),
                ["messages"] = messagePayload,
                ["stream"] = true,
                ["options"] = new Dictionary<string, object>
                {
                    ["temperature"] = temperature,
                    ["top_p"] = topP,
                    ["top_k"] = topK,
                    ["repeat_penalty"] = repeatPenalty,
                    ["num_predict"] = maxTokens,
                },
            };
        }

        var payload = new Dictionary<string, object>
        {
            ["messages"] = messagePayload,
            ["stream"] = true,
            ["temperature"] = temperature,
            ["top_p"] = topP,
            ["top_k"] = topK,
            ["repeat_penalty"] = repeatPenalty,
        };
        if (!string.IsNullOrWhiteSpace(model)) payload["model"] = model.Trim();
        if (maxTokens > 0) payload["max_tokens"] = maxTokens;
        return payload;
    }

    public static BackendStreamPart? ParseStreamLine(LlamaBackendKind backend, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var data = line.Trim();
        if (backend != LlamaBackendKind.Ollama && data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            data = data[5..].Trim();
        if (data == "[DONE]") return new BackendStreamPart("", true);

        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            var done = root.TryGetProperty("done", out var doneElement)
                && doneElement.ValueKind == JsonValueKind.True;

            if (backend == LlamaBackendKind.Ollama)
            {
                var content = root.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var messageContent)
                    ? messageContent.GetString() ?? ""
                    : root.TryGetProperty("response", out var response) ? response.GetString() ?? "" : "";
                return new BackendStreamPart(content, done);
            }

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                var content = ReadContent(choice, "delta") ?? ReadContent(choice, "message") ?? "";
                return new BackendStreamPart(content, done);
            }

            if (root.TryGetProperty("results", out var results)
                && results.ValueKind == JsonValueKind.Array
                && results.GetArrayLength() > 0
                && results[0].TryGetProperty("text", out var resultText))
            {
                return new BackendStreamPart(resultText.GetString() ?? "", done);
            }

            return done ? new BackendStreamPart("", true) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool RequiresModel(LlamaBackendKind backend)
        => backend == LlamaBackendKind.Ollama;

    private static string? ReadContent(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value)
            && value.TryGetProperty("content", out var content)
            ? content.GetString()
            : null;
    }
}
