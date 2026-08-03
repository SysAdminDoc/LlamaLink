using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ModelCardTests
{
    [TestMethod]
    public void BuildsSafeRawReadmeUrlFromRepositoryId()
    {
        Assert.AreEqual(
            "https://huggingface.co/org/model/raw/main/README.md",
            ModelCardParser.BuildRawReadmeUrl("org/model"));
    }

    [TestMethod]
    public void ParsesFrontMatterAndReadableMarkdown()
    {
        var card = ModelCardParser.Parse(
            "org/model",
            "---\ntitle: Friendly Model\nlicense: apache-2.0\ntags:\n- text-generation\n---\n# Friendly Model\n\nA **small** model.\n\n- Fast\n- Local\n\n[Docs](https://example.com)");

        Assert.AreEqual("Friendly Model", card.Title);
        Assert.AreEqual("apache-2.0", card.License);
        StringAssert.Contains(card.Tags, "text-generation");
        StringAssert.Contains(card.RenderedMarkdown, "A small model.");
        StringAssert.Contains(card.RenderedMarkdown, "• Fast");
        StringAssert.Contains(card.RenderedMarkdown, "Docs (https://example.com)");
    }

    [TestMethod]
    public void UsesRepositoryNameWhenCardHasNoTitle()
    {
        var card = ModelCardParser.Parse("org/model-name", "No heading here.");

        Assert.AreEqual("model-name", card.Title);
        Assert.AreEqual("Not specified", card.License);
    }
}
