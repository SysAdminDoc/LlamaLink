#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LlamaLink;

public sealed record QuantModelFile(string FileName, long SizeBytes, string Quant, string Path = "")
{
    public double SizeGiB => SizeBytes / (double)QuantRecommender.BytesPerGiB;
}

public sealed class QuantRecommendationResult
{
    internal QuantRecommendationResult(
        QuantModelFile? selectedFile,
        IReadOnlyList<QuantModelFile> candidates,
        double availableMemoryGiB,
        double? estimatedMemoryGiB,
        string message)
    {
        SelectedFile = selectedFile;
        Candidates = candidates;
        AvailableMemoryGiB = availableMemoryGiB;
        EstimatedMemoryGiB = estimatedMemoryGiB;
        Message = message;
    }

    public QuantModelFile? SelectedFile { get; }
    public IReadOnlyList<QuantModelFile> Candidates { get; }
    public double AvailableMemoryGiB { get; }
    public double? EstimatedMemoryGiB { get; }
    public string Message { get; }
    public bool HasRecommendation => SelectedFile is not null;
}

public static class QuantRecommender
{
    public const long BytesPerGiB = 1024L * 1024 * 1024;
    public const double RuntimeHeadroomMultiplier = 1.15;

    private static readonly Regex QuantRegex = new(
        @"(?<![A-Za-z0-9])(?<quant>I?Q\d(?:_[A-Za-z0-9]+)*)(?=[.\-]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string ParseQuant(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return "";
        var match = QuantRegex.Match(filename);
        return match.Success ? match.Groups["quant"].Value.ToUpperInvariant() : "";
    }

    public static string GetModelFamilyKey(string filename)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(filename).Trim();
        var quant = ParseQuant(stem);
        if (quant.Length == 0) return stem;

        var quantIndex = stem.IndexOf(quant, StringComparison.OrdinalIgnoreCase);
        return quantIndex > 0
            ? stem[..quantIndex].TrimEnd('.', '-', '_', ' ')
            : stem;
    }

    public static int GetQualityRank(string quant)
    {
        var normalized = (quant ?? "").Trim().ToUpperInvariant();
        if (normalized.StartsWith("Q8", StringComparison.Ordinal)) return 500;
        if (normalized.StartsWith("Q6_K", StringComparison.Ordinal)) return 400;
        if (normalized == "Q6_0" || normalized.StartsWith("Q6_", StringComparison.Ordinal)) return 390;
        if (normalized == "Q5_K_M") return 300;
        if (normalized.StartsWith("Q5_K", StringComparison.Ordinal)) return 295;
        if (normalized.StartsWith("Q5", StringComparison.Ordinal)) return 285;
        if (normalized == "Q4_K_M") return 200;
        if (normalized.StartsWith("Q4_K", StringComparison.Ordinal)) return 195;
        if (normalized.StartsWith("Q4", StringComparison.Ordinal)) return 185;
        if (normalized.StartsWith("IQ4", StringComparison.Ordinal)) return 175;
        if (normalized == "IQ3_M") return 160;
        if (normalized == "IQ3_S") return 150;
        if (normalized.StartsWith("IQ3", StringComparison.Ordinal)) return 145;
        if (normalized.StartsWith("IQ2", StringComparison.Ordinal)) return 100;
        return 0;
    }

    public static QuantRecommendationResult Recommend(
        IEnumerable<QuantModelFile> modelFiles,
        double vramGiB,
        double ramGiB,
        double runtimeHeadroomMultiplier = RuntimeHeadroomMultiplier)
    {
        ArgumentNullException.ThrowIfNull(modelFiles);

        var candidates = modelFiles
            .Where(file => file is not null && file.SizeBytes > 0 && GetQualityRank(file.Quant) > 0)
            .GroupBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(file => file.SizeBytes).First())
            .OrderByDescending(file => GetQualityRank(file.Quant))
            .ThenBy(file => file.SizeBytes)
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (double.IsNaN(vramGiB) || double.IsInfinity(vramGiB) || vramGiB < 0
            || double.IsNaN(ramGiB) || double.IsInfinity(ramGiB) || ramGiB < 0
            || vramGiB + ramGiB <= 0)
        {
            return new QuantRecommendationResult(
                null, candidates, 0, null,
                "Enter a positive VRAM + RAM capacity to compare quantizations.");
        }

        if (double.IsNaN(runtimeHeadroomMultiplier) || double.IsInfinity(runtimeHeadroomMultiplier)
            || runtimeHeadroomMultiplier < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeHeadroomMultiplier));
        }

        var availableMemoryGiB = vramGiB + ramGiB;
        if (candidates.Length == 0)
        {
            return new QuantRecommendationResult(
                null, candidates, availableMemoryGiB, null,
                "No recognized quantized GGUF variants were found beside the selected model.");
        }

        var selected = candidates.FirstOrDefault(file =>
            file.SizeBytes / (double)BytesPerGiB * runtimeHeadroomMultiplier <= availableMemoryGiB);

        if (selected is not null)
        {
            var estimatedGiB = selected.SizeGiB * runtimeHeadroomMultiplier;
            return new QuantRecommendationResult(
                selected, candidates, availableMemoryGiB, estimatedGiB,
                $"{selected.Quant} fits with approximately {estimatedGiB.ToString("F2", CultureInfo.InvariantCulture)} GiB required.");
        }

        var smallest = candidates.OrderBy(file => file.SizeBytes).First();
        var minimumRequiredGiB = smallest.SizeGiB * runtimeHeadroomMultiplier;
        return new QuantRecommendationResult(
            null, candidates, availableMemoryGiB, minimumRequiredGiB,
            $"No listed quant fits; the smallest option needs approximately {minimumRequiredGiB.ToString("F2", CultureInfo.InvariantCulture)} GiB.");
    }
}
