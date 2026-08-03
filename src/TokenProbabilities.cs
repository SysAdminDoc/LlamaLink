#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LlamaLink;

public sealed record TokenProbabilityOptions(bool Enabled, int TopK)
{
    public int ClampedTopK => Math.Clamp(TopK, 1, 20);
}

public sealed record TokenProbabilityAlternative(string Token, double LogProbability)
{
    public double Probability => TokenProbabilityFormatting.FromLogProbability(LogProbability);
}

public sealed record TokenProbabilityEntry(
    string Token,
    double LogProbability,
    IReadOnlyList<TokenProbabilityAlternative> Alternatives)
{
    public double Probability => TokenProbabilityFormatting.FromLogProbability(LogProbability);
}

public static class TokenProbabilityFormatting
{
    public static string Format(TokenProbabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var alternatives = entry.Alternatives.Count == 0
            ? new[] { new TokenProbabilityAlternative(entry.Token, entry.LogProbability) }
            : entry.Alternatives;
        var choices = string.Join(
            "  •  ",
            alternatives.Select(alternative =>
                $"{DisplayToken(alternative.Token)} {FormatProbability(alternative.LogProbability)}"));
        return $"{DisplayToken(entry.Token)}  {FormatProbability(entry.LogProbability)}  |  {choices}";
    }

    public static string DisplayToken(string? token)
        => (token ?? "")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace(" ", "[space]", StringComparison.Ordinal);

    public static double FromLogProbability(double logProbability)
    {
        if (!double.IsFinite(logProbability)) return 0;
        return Math.Clamp(Math.Exp(Math.Clamp(logProbability, -745, 0)), 0, 1);
    }

    public static string FormatProbability(double logProbability)
        => double.IsFinite(logProbability)
            ? FromLogProbability(logProbability).ToString("P1", CultureInfo.InvariantCulture)
            : "n/a";
}
