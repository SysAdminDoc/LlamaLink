#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LlamaLink;

public static class ConversationBrancher
{
    public static List<ChatHistoryMessage> SliceThrough(
        IReadOnlyList<ChatHistoryMessage> messages,
        int inclusiveIndex)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (inclusiveIndex < 0 || inclusiveIndex >= messages.Count)
            throw new ArgumentOutOfRangeException(nameof(inclusiveIndex));

        return messages
            .Take(inclusiveIndex + 1)
            .Select(message => new ChatHistoryMessage
            {
                Role = message.Role,
                Content = message.Content,
                Images = VisionImageStore.CloneAll(message.Images).ToList(),
            })
            .ToList();
    }

    public static string BuildFileName(DateTimeOffset timestamp, string branchName)
    {
        var safeName = Regex.Replace(branchName ?? "branch", @"[^A-Za-z0-9._-]+", "_").Trim('_');
        if (string.IsNullOrEmpty(safeName)) safeName = "branch";
        return $"{timestamp:yyyyMMdd_HHmmss}_branch_{safeName}.json";
    }

    public static string Describe(string branchName, string? parentChat)
    {
        var parent = string.IsNullOrWhiteSpace(parentChat) ? "current chat" : Path.GetFileName(parentChat);
        return $"Branch '{branchName}' from {parent}";
    }
}
