#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LlamaLink;

public sealed record ModelCardDocument(
    string RepoId,
    string Title,
    string License,
    string Tags,
    string RenderedMarkdown);

public static class ModelCardParser
{
    public const int MaxRenderedCharacters = 200_000;

    private static readonly Regex MetadataLine = new(
        @"^(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HeadingLine = new(
        @"^#{1,6}\s+(?<heading>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ImageLink = new(
        @"!\[(?<alt>[^\]]*)\]\([^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TextLink = new(
        @"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTag = new(
        @"<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string BuildRawReadmeUrl(string repoId, string branch = "main")
    {
        var segments = (repoId ?? "").Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || string.IsNullOrWhiteSpace(branch))
            throw new ArgumentException("A Hugging Face repository must be namespace/name.", nameof(repoId));

        return $"https://huggingface.co/{Uri.EscapeDataString(segments[0])}/{Uri.EscapeDataString(segments[1])}" +
               $"/raw/{Uri.EscapeDataString(branch.Trim())}/README.md";
    }

    public static ModelCardDocument Parse(string repoId, string markdown)
    {
        var safeRepoId = (repoId ?? "").Trim();
        var source = markdown ?? "";
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = 0;
        if (lines.Length > 0 && lines[0].Trim() == "---")
            bodyStart = ReadFrontMatter(lines, metadata);

        var body = lines.Skip(bodyStart).ToArray();
        var title = metadata.TryGetValue("title", out var metadataTitle) && !string.IsNullOrWhiteSpace(metadataTitle)
            ? metadataTitle
            : body.Select(line => HeadingLine.Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["heading"].Value.Trim())
                .FirstOrDefault()
            ?? (safeRepoId.Contains('/') ? safeRepoId[(safeRepoId.IndexOf('/') + 1)..] : safeRepoId);
        var license = metadata.TryGetValue("license", out var metadataLicense)
            ? metadataLicense
            : "Not specified";
        var tags = metadata.TryGetValue("tags", out var metadataTags)
            ? metadataTags
            : metadata.TryGetValue("pipeline_tag", out var pipelineTag) ? pipelineTag : "";

        return new ModelCardDocument(
            safeRepoId,
            Unquote(title),
            string.IsNullOrWhiteSpace(license) ? "Not specified" : Unquote(license),
            Unquote(tags),
            RenderBody(body));
    }

    private static int ReadFrontMatter(string[] lines, Dictionary<string, string> metadata)
    {
        var currentKey = "";
        var values = new List<string>();
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line == "---")
            {
                if (!string.IsNullOrWhiteSpace(currentKey))
                    metadata[currentKey] = string.Join(", ", values);
                return index + 1;
            }

            var match = MetadataLine.Match(line);
            if (match.Success)
            {
                if (!string.IsNullOrWhiteSpace(currentKey))
                    metadata[currentKey] = string.Join(", ", values);
                currentKey = match.Groups["key"].Value;
                values = new List<string>();
                var value = match.Groups["value"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(currentKey))
            {
                values.Add(line[2..].Trim());
            }
        }

        return 0;
    }

    private static string RenderBody(IEnumerable<string> lines)
    {
        var output = new StringBuilder();
        var inCode = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCode = !inCode;
                if (output.Length > 0 && output[^1] != '\n') output.AppendLine();
                output.AppendLine(inCode ? "Code:" : "End code");
                continue;
            }

            if (!inCode)
            {
                var heading = HeadingLine.Match(line);
                if (heading.Success)
                {
                    line = $"\n{heading.Groups["heading"].Value.Trim().ToUpperInvariant()}";
                }
                else if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    line = $"│ {line.TrimStart()[1..].Trim()}";
                }
                else if (line.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith("* ", StringComparison.Ordinal))
                {
                    line = $"• {line.TrimStart()[2..].Trim()}";
                }
                line = ConvertInlineMarkdown(line);
            }

            output.AppendLine(line);
            if (output.Length >= MaxRenderedCharacters)
                break;
        }

        var rendered = output.ToString().Trim();
        return rendered.Length > MaxRenderedCharacters
            ? rendered[..MaxRenderedCharacters] + "\n\n[Model card truncated]"
            : rendered;
    }

    private static string ConvertInlineMarkdown(string value)
    {
        var result = ImageLink.Replace(value, "[image: ${alt}]");
        result = TextLink.Replace(result, "${text} (${url})");
        result = HtmlTag.Replace(result, "");
        return result.Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);
    }

    private static string Unquote(string value)
        => value.Trim().Trim('"', '\'');
}
