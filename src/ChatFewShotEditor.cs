#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaLink;

public sealed class FewShotTurn
{
    public int Index { get; init; }
    public string Content { get; init; } = "";
    public string Display { get; init; } = "";
}

public static class ChatFewShotEditor
{
    public static List<FewShotTurn> FindAssistantTurns(IReadOnlyList<ChatHistoryMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return messages
            .Select((message, index) => (message, index))
            .Where(entry => string.Equals(entry.message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            .Select((entry, assistantNumber) => new FewShotTurn
            {
                Index = entry.index,
                Content = entry.message.Content,
                Display = $"Assistant turn {assistantNumber + 1} (message {entry.index + 1})",
            })
            .ToList();
    }

    public static bool TryApplyAssistantEdit(
        IList<ChatHistoryMessage> messages,
        int messageIndex,
        string content)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messageIndex < 0 || messageIndex >= messages.Count
            || !string.Equals(messages[messageIndex].Role, "assistant", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(content))
            return false;

        messages[messageIndex].Content = content.Trim();
        return true;
    }
}
