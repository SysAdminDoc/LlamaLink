using System.Text.Json;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ServerProfileTests
{
    [TestMethod]
    public void ReadsAndWritesProfileSettings()
    {
        using var document = JsonDocument.Parse("""
            {
              "server_profiles": [
                {
                  "name": "Coding",
                  "model_path": "C:\\models\\code-Q5_K_M.gguf",
                  "ctx_size": 8192,
                  "gpu_layers": 33,
                  "threads": 12,
                  "flash_attn": false,
                  "mlock": true
                }
              ]
            }
            """);

        var profiles = ServerProfileStore.Read(document.RootElement);
        var serialized = JsonSerializer.Serialize(ServerProfileStore.ToJson(profiles));

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual("Coding", profiles[0].Name);
        Assert.AreEqual(8192, profiles[0].ContextSize);
        Assert.IsFalse(profiles[0].FlashAttention);
        Assert.IsTrue(profiles[0].Mlock);
        StringAssert.Contains(serialized, "code-Q5_K_M.gguf");
    }

    [TestMethod]
    public void ReplacesDuplicateNamesAndNormalizesInvalidValues()
    {
        using var document = JsonDocument.Parse("""
            {
              "server_profiles": [
                { "name": "Default", "ctx_size": -1, "gpu_layers": -4, "threads": 0 },
                { "name": " default ", "ctx_size": 2048, "gpu_layers": 12, "threads": 4 }
              ]
            }
            """);

        var profiles = ServerProfileStore.Read(document.RootElement);
        var serialized = ServerProfileStore.ToJson(profiles);

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual("default", profiles[0].Name);
        Assert.AreEqual(2048, profiles[0].ContextSize);
        Assert.AreEqual(12, profiles[0].GpuLayers);
        Assert.AreEqual(1, serialized.Count);
        Assert.AreEqual("default", serialized[0]["name"]);
    }
}
