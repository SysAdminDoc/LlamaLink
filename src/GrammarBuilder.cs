#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LlamaLink;

public sealed record GrammarBuilderRule(string Name, string Definition)
{
    public string Display => $"{Name} ::= {Definition}";
}

public static class GrammarBuilder
{
    private static readonly Regex RuleName = new(
        @"^[A-Za-z_][A-Za-z0-9_-]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<GrammarBuilderRule> Parse(string gbnf)
    {
        var rules = new List<GrammarBuilderRule>();
        foreach (var rawLine in (gbnf ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                continue;

            var separator = line.IndexOf("::=", StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var name = line[..separator].Trim();
            var definition = line[(separator + 3)..].Trim();
            if (IsValidName(name) && !string.IsNullOrWhiteSpace(definition))
                rules.Add(new GrammarBuilderRule(name, definition));
        }
        return rules;
    }

    public static string Build(IReadOnlyList<GrammarBuilderRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0)
            throw new ArgumentException("Add at least one grammar rule.", nameof(rules));

        var names = new HashSet<string>(StringComparer.Ordinal);
        var lines = new List<string>(rules.Count);
        foreach (var rule in rules)
        {
            if (!IsValidName(rule.Name))
                throw new ArgumentException($"Invalid grammar rule name: {rule.Name}", nameof(rules));
            if (!names.Add(rule.Name))
                throw new ArgumentException($"Duplicate grammar rule: {rule.Name}", nameof(rules));
            if (string.IsNullOrWhiteSpace(rule.Definition))
                throw new ArgumentException($"Rule {rule.Name} has no definition.", nameof(rules));
            lines.Add($"{rule.Name.Trim()} ::= {rule.Definition.Trim()}");
        }

        if (!names.Contains("root"))
            throw new ArgumentException("A grammar must define a root rule.", nameof(rules));
        return string.Join(Environment.NewLine, lines);
    }

    public static bool IsValidName(string name)
        => !string.IsNullOrWhiteSpace(name) && RuleName.IsMatch(name.Trim());
}
