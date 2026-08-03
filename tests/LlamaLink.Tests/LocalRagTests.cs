using System;
using System.IO;
using System.Linq;
using System.Text;
using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class LocalRagTests
{
    [TestMethod]
    public void ChunksAndSearchesIndexedMarkdown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llamalink-rag-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(path, "The red fox lives in a quiet forest.\n\nThe blue whale lives in the ocean.");
            var index = new RagIndex();
            var result = index.IndexFiles(new[] { path });

            Assert.AreEqual(1, result.FilesIndexed);
            Assert.IsTrue(result.ChunksIndexed > 0);
            var matches = index.Search("red fox forest", 2);
            Assert.IsTrue(matches.Count > 0);
            StringAssert.Contains(matches[0].Text, "red fox");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void ExtractsPlainTextPdfOperatorsAndEscapes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llamalink-rag-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%PDF-1.4 BT (Hello\\040PDF\\nworld) Tj ET"));
            var text = RagTextExtractor.Extract(path);
            StringAssert.Contains(text, "Hello PDF world");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void FormatsRetrievedSourcesForPromptInjection()
    {
        var context = RagIndex.FormatContext(new[]
        {
            new RagSearchResult
            {
                SourcePath = @"C:\docs\guide.md",
                ChunkIndex = 1,
                Score = 0.82,
                Text = "Keep the answer grounded.",
            },
        });

        StringAssert.Contains(context, "guide.md");
        StringAssert.Contains(context, "Keep the answer grounded.");
        StringAssert.Contains(context, "chunk 2");
    }

    [TestMethod]
    public void ReindexReplacesChangedFolderSourceAndSupportsRemoval()
    {
        var path = Path.Combine(Path.GetTempPath(), $"llamalink-rag-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "alpha folder note");
            var index = new RagIndex();
            index.IndexFiles(new[] { path });
            File.WriteAllText(path, "beta folder note");
            var update = index.IndexFiles(new[] { path });

            Assert.AreEqual(1, update.FilesIndexed);
            Assert.AreEqual("beta folder note", index.Search("beta", 1).Single().Text);
            Assert.AreEqual(1, index.RemoveSource(path));
            Assert.AreEqual(0, index.ChunkCount);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
