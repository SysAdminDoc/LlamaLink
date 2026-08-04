using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public class ModelPruningTests
{
    [TestMethod]
    public void FindCandidates_ExcludesProtectedNonModelsAndReparseFiles()
    {
        var root = CreateTempDirectory();
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            var keep = Path.Combine(root, "keep.gguf");
            var remove = Path.Combine(nested, "remove.gguf");
            File.WriteAllBytes(keep, new byte[4]);
            File.WriteAllBytes(remove, new byte[8]);
            File.WriteAllText(Path.Combine(root, "notes.txt"), "not a model");

            var candidates = ModelPruner.FindCandidates(root, new[] { keep });

            Assert.AreEqual(1, candidates.Count);
            Assert.AreEqual(Path.GetFullPath(remove), candidates[0].FilePath);
            Assert.AreEqual(8, candidates[0].SizeBytes);
            Assert.AreEqual("8 B", candidates[0].SizeDisplay);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void FindCandidates_SortsLargestFirstThenName()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "small.gguf"), new byte[2]);
            File.WriteAllBytes(Path.Combine(root, "large.gguf"), new byte[10]);
            File.WriteAllBytes(Path.Combine(root, "other-large.gguf"), new byte[10]);

            var names = ModelPruner.FindCandidates(root).Select(candidate => candidate.Name).ToArray();

            CollectionAssert.AreEqual(new[] { "large.gguf", "other-large.gguf", "small.gguf" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void IsSafeToDelete_RejectsOutsideProtectedAndNonGgufPaths()
    {
        var root = CreateTempDirectory();
        var outside = CreateTempDirectory();
        try
        {
            var model = Path.Combine(root, "model.gguf");
            var text = Path.Combine(root, "notes.txt");
            var outsideModel = Path.Combine(outside, "outside.gguf");
            File.WriteAllBytes(model, new byte[1]);
            File.WriteAllText(text, "notes");
            File.WriteAllBytes(outsideModel, new byte[1]);

            Assert.IsTrue(ModelPruner.IsSafeToDelete(root, model));
            Assert.IsFalse(ModelPruner.IsSafeToDelete(root, model, new[] { model }));
            Assert.IsFalse(ModelPruner.IsSafeToDelete(root, text));
            Assert.IsFalse(ModelPruner.IsSafeToDelete(root, outsideModel));
            Assert.IsFalse(ModelPruner.IsPathWithin(root, root));
            Assert.IsTrue(ModelPruner.IsPathWithin(root, model));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    private static string CreateTempDirectory()
        => Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "LlamaLink-ModelPruning-" + Guid.NewGuid().ToString("N"))).FullName;
}
