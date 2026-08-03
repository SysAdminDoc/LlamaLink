#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LlamaLink;

public sealed class ServerProfile
{
    public string Name { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public int ContextSize { get; set; } = 4096;
    public int GpuLayers { get; set; } = 99;
    public int Threads { get; set; } = 4;
    public bool FlashAttention { get; set; } = true;
    public bool Mlock { get; set; }
}

public static class ServerProfileStore
{
    public static List<ServerProfile> Read(JsonElement root)
    {
        var profiles = new List<ServerProfile>();
        if (!root.TryGetProperty("server_profiles", out var profileArray)
            || profileArray.ValueKind != JsonValueKind.Array)
        {
            return profiles;
        }

        foreach (var element in profileArray.EnumerateArray())
        {
            var name = ReadString(element, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var profile = new ServerProfile
            {
                Name = name.Trim(),
                ModelPath = ReadString(element, "model_path").Trim(),
                ContextSize = ReadPositiveInt(element, "ctx_size", 4096),
                GpuLayers = ReadNonNegativeInt(element, "gpu_layers", 99),
                Threads = ReadPositiveInt(element, "threads", 4),
                FlashAttention = ReadBool(element, "flash_attn", true),
                Mlock = ReadBool(element, "mlock", false),
            };

            var existing = profiles.FindIndex(
                candidate => string.Equals(candidate.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                profiles[existing] = profile;
            else
                profiles.Add(profile);
        }

        return profiles;
    }

    public static List<Dictionary<string, object>> ToJson(IEnumerable<ServerProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var serialized = new List<Dictionary<string, object>>();
        foreach (var profile in profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) continue;

            var normalized = new ServerProfile
            {
                Name = profile.Name.Trim(),
                ModelPath = profile.ModelPath?.Trim() ?? "",
                ContextSize = profile.ContextSize > 0 ? profile.ContextSize : 4096,
                GpuLayers = Math.Max(0, profile.GpuLayers),
                Threads = profile.Threads > 0 ? profile.Threads : 4,
                FlashAttention = profile.FlashAttention,
                Mlock = profile.Mlock,
            };

            var values = new Dictionary<string, object>
            {
                ["name"] = normalized.Name,
                ["model_path"] = normalized.ModelPath,
                ["ctx_size"] = normalized.ContextSize,
                ["gpu_layers"] = normalized.GpuLayers,
                ["threads"] = normalized.Threads,
                ["flash_attn"] = normalized.FlashAttention,
                ["mlock"] = normalized.Mlock,
            };

            var existing = serialized.FindIndex(item =>
                string.Equals((string)item["name"], normalized.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
                serialized[existing] = values;
            else
                serialized.Add(values);
        }

        return serialized;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static int ReadPositiveInt(JsonElement element, string propertyName, int fallback)
    {
        var value = ReadInt(element, propertyName, fallback);
        return value > 0 ? value : fallback;
    }

    private static int ReadNonNegativeInt(JsonElement element, string propertyName, int fallback)
    {
        var value = ReadInt(element, propertyName, fallback);
        return Math.Max(0, value);
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool fallback)
    {
        return element.TryGetProperty(propertyName, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;
    }
}
