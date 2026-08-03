using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class TokenProbabilitiesTests
{
    [TestMethod]
    public void AddsOptionalTopLogprobsRequestFields()
    {
        var messages = new[] { new ChatHistoryMessage { Role = "user", Content = "Hello" } };
        var payload = BackendAdapter.BuildPayload(
            LlamaBackendKind.OpenAiCompatible,
            "",
            messages,
            0.7,
            0.9,
            40,
            1.1,
            128,
            tokenProbabilities: new TokenProbabilityOptions(true, 99));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        Assert.IsTrue(document.RootElement.GetProperty("logprobs").GetBoolean());
        Assert.AreEqual(20, document.RootElement.GetProperty("top_logprobs").GetInt32());
    }

    [TestMethod]
    public void ParsesStreamingAlternativesFromOpenAiLogprobs()
    {
        var part = BackendAdapter.ParseStreamLine(
            LlamaBackendKind.OpenAiCompatible,
            "data: {\"choices\":[{\"delta\":{\"content\":\"cat\"},\"logprobs\":{\"content\":[{\"token\":\"cat\",\"logprob\":-0.1,\"top_logprobs\":[{\"token\":\"cat\",\"logprob\":-0.1},{\"token\":\"car\",\"logprob\":-1.2}]}]}}]}");

        Assert.IsNotNull(part);
        Assert.AreEqual("cat", part!.Content);
        Assert.AreEqual(1, part.TokenProbabilities!.Count);
        Assert.AreEqual("cat", part.TokenProbabilities[0].Token);
        Assert.AreEqual(2, part.TokenProbabilities[0].Alternatives.Count);
        Assert.AreEqual("car", part.TokenProbabilities[0].Alternatives[1].Token);
    }

    [TestMethod]
    public void FormatsWhitespaceAndAlternativeProbabilitiesForViewer()
    {
        var entry = new TokenProbabilityEntry(
            "\n",
            -0.1,
            new[]
            {
                new TokenProbabilityAlternative("\n", -0.1),
                new TokenProbabilityAlternative(" ", -1.2),
            });

        var formatted = TokenProbabilityFormatting.Format(entry);

        StringAssert.Contains(formatted, "\\n");
        StringAssert.Contains(formatted, "[space]");
        StringAssert.Contains(formatted, "•");
    }
}
