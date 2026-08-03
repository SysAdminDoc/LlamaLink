using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class ImageGenerationTests
{
    [TestMethod]
    public void BuildsBoundedStableDiffusionArguments()
    {
        var settings = new ImageGenerationSettings("sd.exe", "model.gguf", "out", Steps: 500, Width: 10, Height: 5000);
        var args = ImageGenerationCommandBuilder.BuildArguments(settings, "a red fox", "out\\image.png");

        CollectionAssert.Contains(args.ToArray(), "--prompt");
        CollectionAssert.Contains(args.ToArray(), "a red fox");
        CollectionAssert.Contains(args.ToArray(), "100");
        CollectionAssert.Contains(args.ToArray(), "128");
        CollectionAssert.Contains(args.ToArray(), "2048");
    }

    [TestMethod]
    public async Task MissingLocalGeneratorFailsWithoutStartingAProcess()
    {
        var result = await ImageGenerationService.GenerateAsync(
            new ImageGenerationSettings("missing-sd.exe", "missing-model.gguf", "out"),
            "a landscape");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "generator");
    }
}
