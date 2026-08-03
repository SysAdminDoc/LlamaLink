#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace LlamaLink;

public static class ChatRegenerator
{
    public static bool CanRegenerate(IReadOnlyList<ChatHistoryMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return messages.Count >= 2
            && string.Equals(messages[^1].Role, "assistant", StringComparison.OrdinalIgnoreCase)
            && messages.Take(messages.Count - 1)
                .Any(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));
    }

    public static List<ChatHistoryMessage> BuildPrompt(IReadOnlyList<ChatHistoryMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!CanRegenerate(messages))
            throw new InvalidOperationException("A completed assistant response is required to regenerate.");

        return messages
            .Take(messages.Count - 1)
            .Select(message => new ChatHistoryMessage { Role = message.Role, Content = message.Content })
            .ToList();
    }
}
