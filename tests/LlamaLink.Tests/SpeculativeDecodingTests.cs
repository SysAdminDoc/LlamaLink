using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class SpeculativeDecodingTests
{
    [TestMethod]
    public void DisabledDraftModelDoesNotChangeServerArguments()
    {
        var arguments = SpeculativeDecodingArguments.Build(
            new SpeculativeDecodingSettings(false, @"C:\models\draft.gguf", 12, 1024));

        Assert.AreEqual(0, arguments.Count);
    }

    [TestMethod]
    public void EmitsDraftModelAndOptionalDraftRuntimeTuning()
    {
        var arguments = SpeculativeDecodingArguments.Build(
            new SpeculativeDecodingSettings(true, @"C:\models\draft model.gguf", 12, 1024));

        CollectionAssert.AreEqual(
            new[] { "-md", @"C:\models\draft model.gguf", "-ngld", "12", "-cd", "1024" },
            arguments.ToArray());
    }
}
