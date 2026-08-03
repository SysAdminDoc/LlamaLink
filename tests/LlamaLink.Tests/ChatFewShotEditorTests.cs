using System.Collections.Generic;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ChatFewShotEditorTests
{
    [TestMethod]
    public void FindsEveryAssistantTurnWithStableMessageIndexes()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new() { Role = "system", Content = "Be precise" },
            new() { Role = "user", Content = "First" },
            new() { Role = "assistant", Content = "Answer one" },
            new() { Role = "user", Content = "Second" },
            new() { Role = "assistant", Content = "Answer two" },
        };

        var turns = ChatFewShotEditor.FindAssistantTurns(messages);

        Assert.AreEqual(2, turns.Count);
        Assert.AreEqual(2, turns[0].Index);
        Assert.AreEqual("Assistant turn 2 (message 5)", turns[1].Display);
    }

    [TestMethod]
    public void AppliesOnlyTheSelectedAssistantTurn()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new() { Role = "user", Content = "Question" },
            new() { Role = "assistant", Content = "Old answer" },
            new() { Role = "user", Content = "Follow-up" },
            new() { Role = "assistant", Content = "Keep this" },
        };

        Assert.IsTrue(ChatFewShotEditor.TryApplyAssistantEdit(messages, 1, "  Better answer  "));
        Assert.AreEqual("Better answer", messages[1].Content);
        Assert.AreEqual("Follow-up", messages[2].Content);
        Assert.AreEqual("Keep this", messages[3].Content);
        Assert.IsFalse(ChatFewShotEditor.TryApplyAssistantEdit(messages, 0, "No user edits"));
        Assert.IsFalse(ChatFewShotEditor.TryApplyAssistantEdit(messages, 1, "   "));
    }
}
