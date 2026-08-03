using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class GrammarTemplatesTests
{
    [TestMethod]
    public void ProvidesDistinctStarterGrammars()
    {
        StringAssert.Contains(GrammarTemplates.GetTemplate(GrammarMode.Json), "root ::= object");
        StringAssert.Contains(GrammarTemplates.GetTemplate(GrammarMode.Regex), "[a-zA-Z0-9_");
        StringAssert.Contains(GrammarTemplates.GetTemplate(GrammarMode.CodeOnly), "line ::= ");
        Assert.AreNotEqual(
            GrammarTemplates.GetTemplate(GrammarMode.Json),
            GrammarTemplates.GetTemplate(GrammarMode.CodeOnly));
    }

    [TestMethod]
    public void AppliesJsonConstraintToBackendSpecificPayloadFields()
    {
        var message = new[] { new ChatHistoryMessage { Role = "user", Content = "Return JSON" } };
        var constraint = new GrammarConstraint(GrammarMode.Json, GrammarTemplates.GetTemplate(GrammarMode.Json));

        var openAi = BackendAdapter.BuildPayload(
            LlamaBackendKind.OpenAiCompatible, "", message, 0.7, 0.9, 40, 1.1, 128, grammar: constraint);
        var ollama = BackendAdapter.BuildPayload(
            LlamaBackendKind.Ollama, "model", message, 0.7, 0.9, 40, 1.1, 128, grammar: constraint);

        using var openAiJson = JsonDocument.Parse(JsonSerializer.Serialize(openAi));
        using var ollamaJson = JsonDocument.Parse(JsonSerializer.Serialize(ollama));
        Assert.AreEqual("json_object", openAiJson.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.IsTrue(openAiJson.RootElement.TryGetProperty("grammar", out _));
        Assert.AreEqual("json", ollamaJson.RootElement.GetProperty("format").GetString());
        Assert.IsTrue(ollamaJson.RootElement.TryGetProperty("grammar", out _));
    }
}
