using System.Collections.Generic;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ChatRegenerationTests
{
    [TestMethod]
    public void BuildsPromptWithoutFinalAssistantTurnOrSourceMutation()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new() { Role = "system", Content = "Be concise" },
            new() { Role = "user", Content = "Explain this" },
            new() { Role = "assistant", Content = "Old answer" },
        };

        var prompt = ChatRegenerator.BuildPrompt(messages);

        Assert.AreEqual(2, prompt.Count);
        Assert.AreEqual("Explain this", prompt[^1].Content);
        Assert.AreEqual(3, messages.Count);
        Assert.AreEqual("Old answer", messages[^1].Content);
    }

    [TestMethod]
    public void RejectsChatsWithoutCompletedAssistantTurn()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new() { Role = "user", Content = "Not answered yet" },
        };

        Assert.IsFalse(ChatRegenerator.CanRegenerate(messages));
        Assert.ThrowsException<System.InvalidOperationException>(() => ChatRegenerator.BuildPrompt(messages));
    }
}
