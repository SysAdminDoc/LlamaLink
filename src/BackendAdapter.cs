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

public sealed record BackendToolCallFragment(
    string Index,
    string Id,
    string Name,
    string Arguments);

public sealed record BackendStreamPart(
    string Content,
    bool Done,
    IReadOnlyList<BackendToolCallFragment>? ToolCalls = null,
    IReadOnlyList<TokenProbabilityEntry>? TokenProbabilities = null);

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
        int maxTokens,
        IReadOnlyList<SafeToolDefinition>? tools = null,
        GrammarConstraint? grammar = null,
        TokenProbabilityOptions? tokenProbabilities = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var messagePayload = messages
            .Select(message => BuildMessagePayload(backend, message))
            .ToArray();

        if (backend == LlamaBackendKind.Ollama)
        {
            var ollamaPayload = new Dictionary<string, object>
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
            if (tools is { Count: > 0 })
                ollamaPayload["tools"] = tools.Select(tool => tool.ToPayload()).ToArray();
            if (grammar is { Enabled: true, Mode: GrammarMode.Json })
                ollamaPayload["format"] = "json";
            ApplyGrammarConstraint(ollamaPayload, grammar);
            return ollamaPayload;
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
        if (tools is { Count: > 0 })
            payload["tools"] = tools.Select(tool => tool.ToPayload()).ToArray();
        ApplyGrammarConstraint(payload, grammar);
        ApplyTokenProbabilityOptions(payload, tokenProbabilities);
        return payload;
    }

    private static void ApplyGrammarConstraint(Dictionary<string, object> payload, GrammarConstraint? grammar)
    {
        if (grammar is not { Enabled: true }) return;

        payload["grammar"] = grammar.Gbnf;
        if (grammar.Mode == GrammarMode.Json)
        {
            payload["response_format"] = new Dictionary<string, object>
            {
                ["type"] = "json_object",
            };
        }
    }

    private static void ApplyTokenProbabilityOptions(
        Dictionary<string, object> payload,
        TokenProbabilityOptions? options)
    {
        if (options is not { Enabled: true }) return;

        payload["logprobs"] = true;
        payload["top_logprobs"] = options.ClampedTopK;
    }

    private static Dictionary<string, object> BuildMessagePayload(
        LlamaBackendKind backend,
        ChatHistoryMessage message)
    {
        var imageData = new List<(ChatImageAttachment Attachment, string Base64)>();
        foreach (var attachment in message.Images)
        {
            if (VisionImageStore.TryRead(attachment, out var base64))
                imageData.Add((attachment, base64));
        }

        var content = message.Content;
        if (imageData.Count < message.Images.Count)
            content += $"\n[Unavailable image attachments: {message.Images.Count - imageData.Count}]";

        if (imageData.Count == 0)
            return new Dictionary<string, object>
            {
                ["role"] = message.Role,
                ["content"] = content,
            };

        if (backend == LlamaBackendKind.Ollama)
        {
            return new Dictionary<string, object>
            {
                ["role"] = message.Role,
                ["content"] = content,
                ["images"] = imageData.Select(image => image.Base64).ToArray(),
            };
        }

        var parts = new List<object>
        {
            new Dictionary<string, object>
            {
                ["type"] = "text",
                ["text"] = content,
            },
        };
        foreach (var image in imageData)
        {
            parts.Add(new Dictionary<string, object>
            {
                ["type"] = "image_url",
                ["image_url"] = new Dictionary<string, object>
                {
                    ["url"] = $"data:{image.Attachment.MimeType};base64,{image.Base64}",
                },
            });
        }

        return new Dictionary<string, object>
        {
            ["role"] = message.Role,
            ["content"] = parts,
        };
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
                var toolCalls = ReadOllamaToolCalls(root);
                var content = root.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var messageContent)
                    ? messageContent.GetString() ?? ""
                    : root.TryGetProperty("response", out var response) ? response.GetString() ?? "" : "";
                return new BackendStreamPart(content, done, toolCalls);
            }

            if (root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                var content = ReadContent(choice, "delta") ?? ReadContent(choice, "message") ?? "";
                var toolCalls = ReadOpenAiToolCalls(choice);
                var tokenProbabilities = ReadTokenProbabilities(choice);
                return new BackendStreamPart(content, done, toolCalls, tokenProbabilities);
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

    private static IReadOnlyList<TokenProbabilityEntry>? ReadTokenProbabilities(JsonElement choice)
    {
        if (!choice.TryGetProperty("logprobs", out var logprobs)
            || logprobs.ValueKind != JsonValueKind.Object
            || !logprobs.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = new List<TokenProbabilityEntry>();
        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("token", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || !TryReadDouble(item, "logprob", out var logProbability))
            {
                continue;
            }

            var alternatives = new List<TokenProbabilityAlternative>();
            if (item.TryGetProperty("top_logprobs", out var topLogprobs)
                && topLogprobs.ValueKind == JsonValueKind.Array)
            {
                foreach (var alternative in topLogprobs.EnumerateArray())
                {
                    if (alternative.TryGetProperty("token", out var alternativeToken)
                        && alternativeToken.ValueKind == JsonValueKind.String
                        && TryReadDouble(alternative, "logprob", out var alternativeLogProbability))
                    {
                        alternatives.Add(new TokenProbabilityAlternative(
                            alternativeToken.GetString() ?? "", alternativeLogProbability));
                    }
                }
            }

            entries.Add(new TokenProbabilityEntry(tokenElement.GetString() ?? "", logProbability, alternatives));
        }

        return entries.Count == 0 ? null : entries;
    }

    private static bool TryReadDouble(JsonElement parent, string propertyName, out double value)
    {
        if (parent.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static IReadOnlyList<BackendToolCallFragment>? ReadOpenAiToolCalls(JsonElement choice)
    {
        if (!choice.TryGetProperty("delta", out var delta)
            || !delta.TryGetProperty("tool_calls", out var calls)
            || calls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var fragments = new List<BackendToolCallFragment>();
        foreach (var call in calls.EnumerateArray())
        {
            var index = call.TryGetProperty("index", out var indexElement)
                ? indexElement.ToString()
                : fragments.Count.ToString();
            var id = call.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? "" : "";
            var name = "";
            var arguments = "";
            if (call.TryGetProperty("function", out var function))
            {
                if (function.TryGetProperty("name", out var nameElement))
                    name = nameElement.GetString() ?? "";
                if (function.TryGetProperty("arguments", out var argumentsElement))
                    arguments = argumentsElement.GetString() ?? "";
            }
            fragments.Add(new BackendToolCallFragment(index, id, name, arguments));
        }

        return fragments;
    }

    private static IReadOnlyList<BackendToolCallFragment>? ReadOllamaToolCalls(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("tool_calls", out var calls)
            || calls.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var fragments = new List<BackendToolCallFragment>();
        var index = 0;
        foreach (var call in calls.EnumerateArray())
        {
            if (!call.TryGetProperty("function", out var function)) continue;
            var name = function.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? ""
                : "";
            var arguments = function.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement.ValueKind == JsonValueKind.String
                    ? argumentsElement.GetString() ?? ""
                    : argumentsElement.GetRawText()
                : "{}";
            fragments.Add(new BackendToolCallFragment(index++.ToString(), "", name, arguments));
        }

        return fragments;
    }
}
