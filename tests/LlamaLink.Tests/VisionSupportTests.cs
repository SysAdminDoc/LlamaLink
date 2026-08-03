using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class VisionSupportTests
{
    [TestMethod]
    public void EncodesImageAsOpenAiDataUriAndOllamaBase64()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llamalink-image-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 });
            var attachment = VisionImageStore.Create(path);
            var message = new ChatHistoryMessage
            {
                Role = "user",
                Content = "What is here?",
                Images = new List<ChatImageAttachment> { attachment },
            };

            var openAi = JsonDocument.Parse(JsonSerializer.Serialize(BackendAdapter.BuildPayload(
                LlamaBackendKind.OpenAiCompatible,
                "",
                new[] { message },
                0.7,
                0.9,
                40,
                1.1,
                128)));
            var ollama = JsonDocument.Parse(JsonSerializer.Serialize(BackendAdapter.BuildPayload(
                LlamaBackendKind.Ollama,
                "vision-model",
                new[] { message },
                0.7,
                0.9,
                40,
                1.1,
                128)));

            Assert.AreEqual(JsonValueKind.Array, openAi.RootElement.GetProperty("messages")[0].GetProperty("content").ValueKind);
            StringAssert.StartsWith(
                openAi.RootElement.GetProperty("messages")[0].GetProperty("content")[1]
                    .GetProperty("image_url").GetProperty("url").GetString()!,
                "data:image/png;base64,");
            Assert.AreEqual(JsonValueKind.Array, ollama.RootElement.GetProperty("messages")[0].GetProperty("images").ValueKind);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void PersistsImagePathAlongsideChatMessage()
    {
        var attachment = new ChatImageAttachment { Path = @"C:\images\sample.jpg", MimeType = "image/jpeg" };
        var json = ChatHistoryStore.Serialize(
            new[] { new Dictionary<string, string> { ["role"] = "user", ["content"] = "Describe" } },
            null,
            1,
            images: new Dictionary<int, IReadOnlyList<ChatImageAttachment>>
            {
                [0] = new[] { attachment },
            });

        var document = ChatHistoryStore.Deserialize(json);

        Assert.AreEqual(1, document.Messages[0].Images.Count);
        Assert.AreEqual(attachment.Path, document.Messages[0].Images.Single().Path);
    }
}
