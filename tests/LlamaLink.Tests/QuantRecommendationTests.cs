using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class QuantRecommendationTests
{
    private static long GiB(double value) => (long)(value * QuantRecommender.BytesPerGiB);

    [TestMethod]
    public void PrefersHighestQualityQuantThatFits()
    {
        var files = new[]
        {
            new QuantModelFile("model-Q8_0.gguf", GiB(7), "Q8_0"),
            new QuantModelFile("model-Q6_K.gguf", GiB(5), "Q6_K"),
            new QuantModelFile("model-Q5_K_M.gguf", GiB(4), "Q5_K_M"),
        };

        var result = QuantRecommender.Recommend(files, vramGiB: 8, ramGiB: 1);

        Assert.IsTrue(result.HasRecommendation);
        Assert.AreEqual("Q8_0", result.SelectedFile!.Quant);
    }

    [TestMethod]
    public void FallsBackInRoadmapQualityOrderWhenLargerQuantsDoNotFit()
    {
        var files = new[]
        {
            new QuantModelFile("model-Q8_0.gguf", GiB(8), "Q8_0"),
            new QuantModelFile("model-Q6_K.gguf", GiB(7), "Q6_K"),
            new QuantModelFile("model-Q5_K_M.gguf", GiB(6), "Q5_K_M"),
            new QuantModelFile("model-Q4_K_M.gguf", GiB(4), "Q4_K_M"),
            new QuantModelFile("model-IQ3_S.gguf", GiB(3), "IQ3_S"),
        };

        var result = QuantRecommender.Recommend(files, vramGiB: 5, ramGiB: 0);

        Assert.IsTrue(result.HasRecommendation);
        Assert.AreEqual("Q4_K_M", result.SelectedFile!.Quant);
    }

    [TestMethod]
    public void AppliesRuntimeHeadroomBeforeDeclaringAFileFit()
    {
        var files = new[]
        {
            new QuantModelFile("model-Q4_K_M.gguf", GiB(5), "Q4_K_M"),
        };

        var result = QuantRecommender.Recommend(files, vramGiB: 5.5, ramGiB: 0);

        Assert.IsFalse(result.HasRecommendation);
        StringAssert.Contains(result.Message, "smallest option needs");
    }

    [TestMethod]
    public void ParsesQuantNamesAndKeepsUnknownFormatsOutOfRecommendations()
    {
        Assert.AreEqual("Q4_K_M", QuantRecommender.ParseQuant("Llama-3.1-8B.Q4_K_M.gguf"));
        Assert.AreEqual("IQ3_S", QuantRecommender.ParseQuant("model-IQ3_S.gguf"));

        var result = QuantRecommender.Recommend(
            new[]
            {
                new QuantModelFile("model-custom.gguf", GiB(1), "CUSTOM"),
                new QuantModelFile("model-Q5_K_M.gguf", GiB(3), "Q5_K_M"),
            },
            vramGiB: 4,
            ramGiB: 0);

        Assert.AreEqual(1, result.Candidates.Count);
        Assert.AreEqual("Q5_K_M", result.SelectedFile!.Quant);
    }

    [TestMethod]
    public void RejectsEmptyMemoryBudget()
    {
        var result = QuantRecommender.Recommend(
            new[] { new QuantModelFile("model-Q4_K_M.gguf", GiB(3), "Q4_K_M") },
            vramGiB: 0,
            ramGiB: 0);

        Assert.IsFalse(result.HasRecommendation);
        StringAssert.Contains(result.Message, "positive VRAM");
    }
}
