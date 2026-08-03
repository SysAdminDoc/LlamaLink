using System;
using System.IO;
using System.Linq;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class PromptLibraryTests
{
    [TestMethod]
    public void DefaultsCoverTheFourPromptDomains()
    {
        var domains = PromptLibraryStore.CreateDefaults()
            .Select(prompt => prompt.Domain)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CollectionAssert.AreEquivalent(new[] { "Code", "Roleplay", "Summarize", "Translate" }, domains);
    }

    [TestMethod]
    public void RoundTripsCustomPromptsAndKeepsBuiltInsOutOfWorkspaceJson()
    {
        var custom = new SystemPromptEntry
        {
            Id = "custom-1",
            Domain = "Research",
            Name = "Evidence first",
            Content = "Separate evidence from inference.",
        };
        var json = PromptLibraryStore.Serialize(new[] { custom });
        var parsed = PromptLibraryStore.Parse(json);

        Assert.AreEqual(1, parsed.Count);
        Assert.AreEqual("Research", parsed[0].Domain);
        Assert.IsFalse(json.Contains("Code reviewer", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SaveAndLoadMergesCustomPromptsWithDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "LlamaLinkPrompts-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            PromptLibraryStore.Save(path, new[]
            {
                new SystemPromptEntry { Id = "custom-1", Domain = "Custom", Name = "One", Content = "Do one." },
            });

            var loaded = PromptLibraryStore.Load(path);

            Assert.IsTrue(loaded.Any(prompt => prompt.Name == "Code reviewer"));
            Assert.IsTrue(loaded.Any(prompt => prompt.Name == "One"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
