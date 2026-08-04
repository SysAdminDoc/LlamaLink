#nullable enable

using System;
using System.Globalization;

namespace LlamaLink;

public sealed record CostEstimate(
    int PromptTokens,
    int OutputTokens,
    double ElapsedSeconds,
    double WattHours,
    double Currency);

public static class CostEstimator
{
    public static CostEstimate Calculate(
        int promptTokens,
        int outputTokens,
        double elapsedSeconds,
        double powerWatts,
        double electricityRate)
    {
        if (promptTokens < 0) throw new ArgumentOutOfRangeException(nameof(promptTokens));
        if (outputTokens < 0) throw new ArgumentOutOfRangeException(nameof(outputTokens));
        if (elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (powerWatts < 0) throw new ArgumentOutOfRangeException(nameof(powerWatts));
        if (electricityRate < 0) throw new ArgumentOutOfRangeException(nameof(electricityRate));

        var wattHours = powerWatts * elapsedSeconds / 3600.0;
        return new CostEstimate(
            promptTokens,
            outputTokens,
            elapsedSeconds,
            wattHours,
            wattHours / 1000.0 * electricityRate);
    }

    public static CostEstimate Forecast(
        int promptTokens,
        int outputTokens,
        double tokensPerSecond,
        double powerWatts,
        double electricityRate)
    {
        if (tokensPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(tokensPerSecond));
        return Calculate(promptTokens, outputTokens, outputTokens / tokensPerSecond, powerWatts, electricityRate);
    }

    public static string Format(CostEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return $"{estimate.PromptTokens + estimate.OutputTokens:N0} tok • " +
               $"{estimate.WattHours:F2} Wh • " +
               $"{estimate.Currency.ToString("C4", CultureInfo.CurrentCulture)}";
    }
}
