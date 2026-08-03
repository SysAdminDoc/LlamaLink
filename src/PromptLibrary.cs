#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LlamaLink;

public sealed class SystemPromptEntry
{
    public string Id { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public bool BuiltIn { get; set; }
}

public static class PromptLibraryStore
{
    public static List<SystemPromptEntry> CreateDefaults()
    {
        return new List<SystemPromptEntry>
        {
            CreateBuiltIn("code-review", "Code", "Code reviewer", "You are a precise code reviewer. Identify correctness, security, and maintainability issues first. Explain the reason for each finding and propose focused fixes with small examples."),
            CreateBuiltIn("code-pair", "Code", "Pair programmer", "You are a pragmatic pair programmer. Clarify assumptions briefly, then provide an idiomatic, testable implementation that fits the existing project style."),
            CreateBuiltIn("roleplay-character", "Roleplay", "Character roleplay", "Stay consistently in the requested character and setting. Keep the dialogue vivid and responsive while respecting the user's boundaries and instructions."),
            CreateBuiltIn("roleplay-narrator", "Roleplay", "Narrator", "Act as an evocative narrator. Describe scenes through concrete sensory details, maintain continuity, and leave room for the user's character to act."),
            CreateBuiltIn("summarize-brief", "Summarize", "Brief summary", "Summarize the provided material in a few concise paragraphs. Preserve the main claims, decisions, risks, and any important numbers; do not invent missing context."),
            CreateBuiltIn("summarize-action", "Summarize", "Action items", "Turn the provided material into an actionable brief with decisions, open questions, owners when stated, and next steps. Mark uncertainty instead of guessing."),
            CreateBuiltIn("translate-natural", "Translate", "Natural translation", "Translate the provided text naturally while preserving meaning, tone, formatting, and names. Return only the translation unless a brief clarification is essential."),
            CreateBuiltIn("translate-literal", "Translate", "Literal translation", "Provide a faithful, close translation of the provided text. Preserve technical terms and ambiguity, and note any unavoidable translation choice briefly."),
        };
    }

    public static List<SystemPromptEntry> Load(string path)
    {
        var entries = CreateDefaults();
        if (!File.Exists(path)) return entries;

        var imported = Parse(File.ReadAllText(path));
        foreach (var entry in imported.Where(entry => !entry.BuiltIn))
            Upsert(entries, entry);
        return entries;
    }

    public static void Save(string path, IEnumerable<SystemPromptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var custom = entries
            .Where(entry => !entry.BuiltIn)
            .Select(Normalize)
            .Where(IsValid)
            .ToList();
        File.WriteAllText(path, Serialize(custom));
    }

    public static string Serialize(IEnumerable<SystemPromptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var normalized = entries.Select(Normalize).Where(IsValid).ToList();
        return JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
    }

    public static List<SystemPromptEntry> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<SystemPromptEntry>();
        var entries = JsonSerializer.Deserialize<List<SystemPromptEntry>>(json) ?? new List<SystemPromptEntry>();
        return entries.Select(Normalize).Where(IsValid).ToList();
    }

    public static void Upsert(List<SystemPromptEntry> entries, SystemPromptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);
        var normalized = Normalize(entry);
        if (!IsValid(normalized)) throw new ArgumentException("Prompt entries require a domain, name, and content.", nameof(entry));

        var index = entries.FindIndex(existing =>
            string.Equals(existing.Id, normalized.Id, StringComparison.OrdinalIgnoreCase)
            || (!existing.BuiltIn && string.Equals(existing.Domain, normalized.Domain, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Name, normalized.Name, StringComparison.OrdinalIgnoreCase)));
        if (index >= 0)
            entries[index] = normalized;
        else
            entries.Add(normalized);
    }

    private static SystemPromptEntry CreateBuiltIn(string id, string domain, string name, string content)
    {
        return new SystemPromptEntry { Id = id, Domain = domain, Name = name, Content = content, BuiltIn = true };
    }

    private static SystemPromptEntry Normalize(SystemPromptEntry entry)
    {
        return new SystemPromptEntry
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim(),
            Domain = entry.Domain.Trim(),
            Name = entry.Name.Trim(),
            Content = entry.Content.Trim(),
            BuiltIn = entry.BuiltIn,
        };
    }

    private static bool IsValid(SystemPromptEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Domain)
            && !string.IsNullOrWhiteSpace(entry.Name)
            && !string.IsNullOrWhiteSpace(entry.Content);
}
