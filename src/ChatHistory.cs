#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlamaLink;

public sealed class ChatHistoryMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public sealed class ChatHistoryDocument
{
    [JsonPropertyName("messages")]
    public List<ChatHistoryMessage> Messages { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("server_context")]
    public string? ServerContext { get; set; }

    [JsonPropertyName("branch_id")]
    public string? BranchId { get; set; }

    [JsonPropertyName("parent_chat")]
    public string? ParentChat { get; set; }

    [JsonPropertyName("branch_point")]
    public int? BranchPoint { get; set; }

    [JsonPropertyName("branch_name")]
    public string? BranchName { get; set; }
}

public static class ChatHistoryStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string Serialize(
        IEnumerable<Dictionary<string, string>> messages,
        string? serverContext,
        long timestamp,
        string? branchId = null,
        string? parentChat = null,
        int? branchPoint = null,
        string? branchName = null)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var document = new ChatHistoryDocument
        {
            Messages = messages.Select(message => new ChatHistoryMessage
            {
                Role = message.TryGetValue("role", out var role) ? role : "",
                Content = message.TryGetValue("content", out var content) ? content : "",
            }).ToList(),
            Timestamp = timestamp,
            ServerContext = string.IsNullOrWhiteSpace(serverContext) ? null : serverContext,
            BranchId = string.IsNullOrWhiteSpace(branchId) ? null : branchId,
            ParentChat = string.IsNullOrWhiteSpace(parentChat) ? null : parentChat,
            BranchPoint = branchPoint,
            BranchName = string.IsNullOrWhiteSpace(branchName) ? null : branchName,
        };

        return JsonSerializer.Serialize(document, WriteOptions);
    }

    public static ChatHistoryDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new JsonException("Chat history is empty.");

        var document = JsonSerializer.Deserialize<ChatHistoryDocument>(json)
            ?? throw new JsonException("Chat history document is empty.");
        document.Messages ??= new List<ChatHistoryMessage>();
        return document;
    }
}

public static class ChatServerContext
{
    public static string ForProfile(string profileName)
        => $"Profile: {profileName.Trim()}";

    public static string ForExternal(string url)
        => $"External: {url.Trim().TrimEnd('/')}";

    public static string ForLocal(string modelPath)
    {
        var modelName = string.IsNullOrWhiteSpace(modelPath)
            ? "No model selected"
            : Path.GetFileName(modelPath);
        return $"Local: {modelName}";
    }
}
