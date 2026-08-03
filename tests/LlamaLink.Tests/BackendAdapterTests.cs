using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class BackendAdapterTests
{
    [TestMethod]
    public void NormalizesPrefixedHostUrls()
    {
        Assert.AreEqual(
            "http://127.0.0.1:5001/v1/chat/completions",
            BackendAdapter.BuildEndpoint("http://127.0.0.1:5001/v1", "/v1/chat/completions"));
        Assert.AreEqual(
            "http://127.0.0.1:11434/api/chat",
            BackendAdapter.BuildEndpoint("http://127.0.0.1:11434/api", "/api/chat"));
    }

    [TestMethod]
    public void BuildsOllamaPayloadWithNestedGenerationOptions()
    {
        var payload = BackendAdapter.BuildPayload(
            LlamaBackendKind.Ollama,
            "llama3.2",
            new[] { new ChatHistoryMessage { Role = "user", Content = "Hello" } },
            0.7,
            0.9,
            40,
            1.1,
            256);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        Assert.AreEqual("llama3.2", document.RootElement.GetProperty("model").GetString());
        Assert.AreEqual(256, document.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
    }

    [TestMethod]
    public void ParsesSseAndOllamaNdjsonDeltas()
    {
        var sse = BackendAdapter.ParseStreamLine(
            LlamaBackendKind.OpenAiCompatible,
            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}");
        var ollama = BackendAdapter.ParseStreamLine(
            LlamaBackendKind.Ollama,
            "{\"message\":{\"role\":\"assistant\",\"content\":\" world\"},\"done\":false}");
        var done = BackendAdapter.ParseStreamLine(LlamaBackendKind.Ollama, "{\"done\":true}");

        Assert.AreEqual("Hello", sse!.Content);
        Assert.AreEqual(" world", ollama!.Content);
        Assert.IsTrue(done!.Done);
    }

    [TestMethod]
    public void ParsesKoboldLegacyResultTextForCompatibility()
    {
        var result = BackendAdapter.ParseStreamLine(
            LlamaBackendKind.KoboldCpp,
            "{\"results\":[{\"text\":\" generated\"}]}");

        Assert.AreEqual(" generated", result!.Content);
    }
}
