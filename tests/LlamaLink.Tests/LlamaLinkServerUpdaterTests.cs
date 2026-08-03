using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class LlamaLinkServerUpdaterTests
{
    [TestMethod]
    public void ParsesOnlyWindowsX64ReleaseAssets()
    {
        var release = LlamaServerUpdater.ParseRelease("""
            {
              "tag_name": "b5000",
              "published_at": "2026-07-01T12:00:00Z",
              "assets": [
                { "name": "llama-b5000-bin-win-cuda-cu12.4-x64.zip", "browser_download_url": "https://example/cuda.zip", "size": 100 },
                { "name": "llama-b5000-bin-win-avx2-x64.zip", "browser_download_url": "https://example/avx2.zip", "size": 80 },
                { "name": "llama-b5000-bin-linux-x64.tar.gz", "browser_download_url": "https://example/linux.tar.gz", "size": 90 },
                { "name": "checksums.sha256", "browser_download_url": "https://example/sha", "size": 10 }
              ]
            }
            """);

        Assert.AreEqual("b5000", release.TagName);
        Assert.AreEqual(2, release.Assets.Count);
        Assert.AreEqual(LlamaServerBackend.Cuda, release.Assets[0].Backend);
    }

    [TestMethod]
    public void SelectsBestSupportedBackend()
    {
        var assets = new[]
        {
            new LlamaServerAsset("cpu.zip", "https://example/cpu", 10, LlamaServerBackend.Cpu),
            new LlamaServerAsset("avx2.zip", "https://example/avx2", 20, LlamaServerBackend.Avx2),
            new LlamaServerAsset("cuda.zip", "https://example/cuda", 30, LlamaServerBackend.Cuda),
        };

        var result = LlamaServerUpdater.SelectBestAsset(
            assets,
            new LlamaHardwareCapabilities(Avx2: true, Avx512: false, Cuda: true, Vulkan: false, Rocm: false));

        Assert.IsNotNull(result);
        Assert.AreEqual(LlamaServerBackend.Cuda, result!.Backend);
    }

    [TestMethod]
    public void FallsBackToCpuWhenNoAcceleratedBuildIsSupported()
    {
        var result = LlamaServerUpdater.SelectBestAsset(
            new[]
            {
                new LlamaServerAsset("cuda.zip", "https://example/cuda", 10, LlamaServerBackend.Cuda),
                new LlamaServerAsset("cpu.zip", "https://example/cpu", 20, LlamaServerBackend.Cpu),
            },
            new LlamaHardwareCapabilities(Avx2: false, Avx512: false, Cuda: false, Vulkan: false, Rocm: false));

        Assert.IsNotNull(result);
        Assert.AreEqual(LlamaServerBackend.Cpu, result!.Backend);
    }

    [TestMethod]
    public void ExtractsBuildAndSemanticVersionsFromServerOutput()
    {
        Assert.AreEqual("b5000", LlamaServerUpdater.ExtractVersion("llama-server version: b5000"));
        Assert.AreEqual("v1.2.3", LlamaServerUpdater.ExtractVersion("llama.cpp v1.2.3"));
        Assert.IsNull(LlamaServerUpdater.ExtractVersion("llama-server started"));
    }
}
