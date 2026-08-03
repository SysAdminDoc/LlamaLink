using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ChatHistoryTests
{
    [TestMethod]
    public void RoundTripsMessagesAndServerContext()
    {
        var json = ChatHistoryStore.Serialize(
            new[]
            {
                new Dictionary<string, string> { ["role"] = "user", ["content"] = "Keep going" },
                new Dictionary<string, string> { ["role"] = "assistant", ["content"] = "Absolutely" },
            },
            ChatServerContext.ForProfile("Coding"),
            timestamp: 1234);

        var document = ChatHistoryStore.Deserialize(json);

        Assert.AreEqual(2, document.Messages.Count);
        Assert.AreEqual("Keep going", document.Messages[0].Content);
        Assert.AreEqual("Profile: Coding", document.ServerContext);
        Assert.AreEqual(1234, document.Timestamp);
    }

    [TestMethod]
    public void ReadsLegacyHistoryWithoutServerContext()
    {
        var document = ChatHistoryStore.Deserialize("""
            { "messages": [{ "role": "user", "content": "Legacy" }] }
            """);

        Assert.AreEqual(1, document.Messages.Count);
        Assert.AreEqual("Legacy", document.Messages[0].Content);
        Assert.IsNull(document.ServerContext);
    }

    [TestMethod]
    public void FormatsPortableServerContexts()
    {
        Assert.AreEqual("External: http://127.0.0.1:8080", ChatServerContext.ForExternal("http://127.0.0.1:8080/"));
        Assert.AreEqual("Local: model-Q4_K_M.gguf", ChatServerContext.ForLocal(@"C:\models\model-Q4_K_M.gguf"));
        Assert.AreEqual("Local: No model selected", ChatServerContext.ForLocal(""));
    }
}
