#nullable enable

using System;
using System.Collections.Generic;

namespace LlamaLink;

public sealed record SpeculativeDecodingSettings(
    bool Enabled,
    string DraftModelPath,
    int DraftGpuLayers,
    int DraftContextSize)
{
    public IReadOnlyList<string> ToArguments()
    {
        if (!Enabled || string.IsNullOrWhiteSpace(DraftModelPath))
            return Array.Empty<string>();

        var arguments = new List<string> { "-md", DraftModelPath.Trim() };
        if (DraftGpuLayers >= 0)
        {
            arguments.Add("-ngld");
            arguments.Add(DraftGpuLayers.ToString());
        }
        if (DraftContextSize > 0)
        {
            arguments.Add("-cd");
            arguments.Add(DraftContextSize.ToString());
        }
        return arguments;
    }
}

public static class SpeculativeDecodingArguments
{
    public static IReadOnlyList<string> Build(SpeculativeDecodingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ToArguments();
    }
}
