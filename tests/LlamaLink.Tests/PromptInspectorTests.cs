using System.Collections.Generic;
using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class PromptInspectorTests
{
    [TestMethod]
    public void BuildsRoleTranscriptAndApproximateTokenPreview()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new() { Role = "system", Content = "Be concise." },
            new() { Role = "user", Content = "Hello, world!" },
        };
        var payload = new Dictionary<string, object> { ["messages"] = messages };

        var inspection = PromptInspector.Build(
            LlamaBackendKind.LlamaCpp,
            "http://127.0.0.1:8080/v1/chat/completions",
            messages,
            payload);

        StringAssert.Contains(inspection.Transcript, "[system]");
        StringAssert.Contains(inspection.Transcript, "[user]");
        StringAssert.Contains(inspection.TokenPreview, "Hello");
        Assert.IsTrue(inspection.EstimatedTokens > 0);
        Assert.IsTrue(inspection.PayloadJson.Contains("messages", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void KeepsImageMetadataOutOfPromptPreviewPayload()
    {
        var messages = new List<ChatHistoryMessage>
        {
            new()
            {
                Role = "user",
                Content = "Describe this",
                Images = new List<ChatImageAttachment>
                {
                    new() { Path = "photo.png", MimeType = "image/png" },
                },
            },
        };

        var inspection = PromptInspector.Build(
            LlamaBackendKind.Ollama,
            "http://127.0.0.1:11434/api/chat",
            messages,
            new Dictionary<string, object> { ["messages"] = messages });

        StringAssert.Contains(inspection.Transcript, "photo.png");
        Assert.IsFalse(inspection.PayloadJson.Contains("base64", System.StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(
            PromptInspector.GetTemplateDescription(LlamaBackendKind.Ollama),
            "Ollama");
    }
}
