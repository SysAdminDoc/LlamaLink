using System;
using System.Collections.Generic;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ConversationBranchingTests
{
    [TestMethod]
    public void SlicesConversationThroughSelectedMessageWithoutMutatingSource()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new() { Role = "system", Content = "Be concise" },
            new() { Role = "user", Content = "First" },
            new() { Role = "assistant", Content = "Answer one" },
            new() { Role = "user", Content = "Second" },
        };

        var branch = ConversationBrancher.SliceThrough(messages, 2);

        Assert.AreEqual(3, branch.Count);
        Assert.AreEqual("Answer one", branch[^1].Content);
        Assert.AreEqual(4, messages.Count);
    }

    [TestMethod]
    public void BuildsSafeBranchFileNames()
    {
        var name = ConversationBrancher.BuildFileName(
            new DateTimeOffset(2026, 8, 3, 10, 11, 12, TimeSpan.Zero),
            "Try / alternate: path");

        StringAssert.Matches(name, new System.Text.RegularExpressions.Regex(
            @"^20260803_101112_branch_Try_alternate_path\.json$"));
    }

    [TestMethod]
    public void PersistsBranchMetadataAlongsideLegacyFields()
    {
        var json = ChatHistoryStore.Serialize(
            new[] { new Dictionary<string, string> { ["role"] = "user", ["content"] = "Hi" } },
            "Profile: Coding",
            1,
            branchId: "branch-1",
            parentChat: "parent.json",
            branchPoint: 0,
            branchName: "Alternate");

        var document = ChatHistoryStore.Deserialize(json);

        Assert.AreEqual("branch-1", document.BranchId);
        Assert.AreEqual("parent.json", document.ParentChat);
        Assert.AreEqual(0, document.BranchPoint);
        Assert.AreEqual("Alternate", document.BranchName);
    }
}
