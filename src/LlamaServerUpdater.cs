#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LlamaLink;

public enum LlamaServerBackend
{
    Cpu,
    Avx2,
    Avx512,
    Cuda,
    Vulkan,
    Rocm,
}

public sealed record LlamaHardwareCapabilities(
    bool Avx2,
    bool Avx512,
    bool Cuda,
    bool Vulkan,
    bool Rocm)
{
    public static LlamaHardwareCapabilities Detect()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        var rocmPath = Environment.GetEnvironmentVariable("ROCM_PATH")
                    ?? Environment.GetEnvironmentVariable("HIP_PATH");

        return new LlamaHardwareCapabilities(
            System.Runtime.Intrinsics.X86.Avx2.IsSupported,
            System.Runtime.Intrinsics.X86.Avx512F.IsSupported,
            !string.IsNullOrWhiteSpace(cudaPath) || HasCommand(path, "nvidia-smi"),
            File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"))
                || HasCommand(path, "vulkaninfo"),
            !string.IsNullOrWhiteSpace(rocmPath) || HasCommand(path, "rocminfo"));
    }

    private static bool HasCommand(string path, string command)
    {
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(directory, command))
                || File.Exists(Path.Combine(directory, $"{command}.exe")))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record LlamaServerAsset(
    string Name,
    string DownloadUrl,
    long SizeBytes,
    LlamaServerBackend Backend)
{
    public string BackendLabel => Backend switch
    {
        LlamaServerBackend.Avx512 => "AVX512",
        LlamaServerBackend.Avx2 => "AVX2",
        LlamaServerBackend.Cuda => "CUDA",
        LlamaServerBackend.Vulkan => "Vulkan",
        LlamaServerBackend.Rocm => "ROCm",
        _ => "CPU",
    };

    public string SizeDisplay => SizeBytes <= 0
        ? "size unknown"
        : SizeBytes >= 1024L * 1024 * 1024
            ? $"{SizeBytes / (1024.0 * 1024 * 1024):F1} GB"
            : $"{SizeBytes / (1024.0 * 1024):F0} MB";

    public string DisplayName => $"{BackendLabel} · {Name} ({SizeDisplay})";
}

public sealed record LlamaServerRelease(
    string TagName,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<LlamaServerAsset> Assets);

public static class LlamaServerUpdater
{
    private static readonly Regex VersionRegex = new(
        @"(?<![A-Za-z0-9])(?<version>(?:v\d+(?:\.\d+)*|b\d+|\d+\.\d+(?:\.\d+)*))(?![A-Za-z0-9])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static LlamaServerRelease ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tagName = root.TryGetProperty("tag_name", out var tag)
            ? tag.GetString() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(tagName))
            throw new JsonException("Release response did not contain a tag_name.");

        DateTimeOffset? publishedAt = null;
        if (root.TryGetProperty("published_at", out var published)
            && published.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(published.GetString(), out var parsedDate))
        {
            publishedAt = parsedDate;
        }

        var assets = new List<LlamaServerAsset>();
        if (root.TryGetProperty("assets", out var assetArray)
            && assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetArray.EnumerateArray())
            {
                var name = ReadString(asset, "name");
                var url = ReadString(asset, "browser_download_url");
                if (!IsWindowsX64Zip(name) || string.IsNullOrWhiteSpace(url)) continue;

                assets.Add(new LlamaServerAsset(
                    name,
                    url,
                    asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                    ClassifyBackend(name)));
            }
        }

        return new LlamaServerRelease(tagName, publishedAt, assets);
    }

    public static LlamaServerAsset? SelectBestAsset(
        IEnumerable<LlamaServerAsset> assets,
        LlamaHardwareCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(capabilities);

        return assets
            .Where(asset => IsCompatible(asset.Backend, capabilities))
            .OrderByDescending(asset => BackendScore(asset.Backend))
            .ThenBy(asset => asset.SizeBytes <= 0 ? long.MaxValue : asset.SizeBytes)
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static string? ExtractVersion(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        var match = VersionRegex.Match(output);
        return match.Success ? match.Groups["version"].Value : null;
    }

    public static LlamaServerBackend ClassifyBackend(string assetName)
    {
        var name = (assetName ?? "").ToLowerInvariant();
        if (name.Contains("cuda")) return LlamaServerBackend.Cuda;
        if (name.Contains("vulkan")) return LlamaServerBackend.Vulkan;
        if (name.Contains("rocm") || name.Contains("hip")) return LlamaServerBackend.Rocm;
        if (name.Contains("avx512")) return LlamaServerBackend.Avx512;
        if (name.Contains("avx2")) return LlamaServerBackend.Avx2;
        return LlamaServerBackend.Cpu;
    }

    private static bool IsWindowsX64Zip(string name)
    {
        var normalized = (name ?? "").ToLowerInvariant();
        return normalized.EndsWith(".zip", StringComparison.Ordinal)
            && normalized.Contains("win", StringComparison.Ordinal)
            && normalized.Contains("x64", StringComparison.Ordinal)
            && !normalized.Contains("arm64", StringComparison.Ordinal)
            && !normalized.Contains("sha", StringComparison.Ordinal);
    }

    private static bool IsCompatible(LlamaServerBackend backend, LlamaHardwareCapabilities capabilities)
    {
        return backend switch
        {
            LlamaServerBackend.Cpu => true,
            LlamaServerBackend.Avx2 => capabilities.Avx2,
            LlamaServerBackend.Avx512 => capabilities.Avx512,
            LlamaServerBackend.Cuda => capabilities.Cuda,
            LlamaServerBackend.Vulkan => capabilities.Vulkan,
            LlamaServerBackend.Rocm => capabilities.Rocm,
            _ => false,
        };
    }

    private static int BackendScore(LlamaServerBackend backend)
    {
        return backend switch
        {
            LlamaServerBackend.Cuda => 600,
            LlamaServerBackend.Vulkan => 550,
            LlamaServerBackend.Rocm => 550,
            LlamaServerBackend.Avx512 => 400,
            LlamaServerBackend.Avx2 => 300,
            _ => 100,
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}
