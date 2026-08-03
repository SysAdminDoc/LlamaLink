#nullable enable

using System;

namespace LlamaLink;

public enum GrammarMode
{
    None,
    Json,
    Regex,
    CodeOnly,
    Custom,
}

public sealed record GrammarConstraint(GrammarMode Mode, string Gbnf)
{
    public bool Enabled => Mode != GrammarMode.None && !string.IsNullOrWhiteSpace(Gbnf);
}

public static class GrammarTemplates
{
    private const string JsonTemplate = """
        root ::= object
        object ::= "{" ws (member ("," ws member)*)? ws "}"
        member ::= string ws ":" ws value
        value ::= object | array | string | number | "true" | "false" | "null"
        array ::= "[" ws (value ("," ws value)*)? ws "]"
        string ::= "\"" ( [^"\\] | "\\" escape )* "\""
        escape ::= ["\\/bfnrt] | "u" [0-9a-fA-F]{4}
        number ::= "-"? ("0" | [1-9] [0-9]*) ("." [0-9]+)? ([eE] [+-]? [0-9]+)?
        ws ::= [ \t\n]*
        """;

    private const string RegexStarterTemplate = "root ::= [a-zA-Z0-9_./:-]+";
    private const string CodeOnlyTemplate = "root ::= line*\nline ::= [^\\n]* \"\\n\"";

    public static GrammarMode ParseMode(string? value)
        => Enum.TryParse<GrammarMode>(value, ignoreCase: true, out var mode) ? mode : GrammarMode.None;

    public static string GetTemplate(GrammarMode mode)
        => mode switch
        {
            GrammarMode.Json => JsonTemplate,
            GrammarMode.Regex => RegexStarterTemplate,
            GrammarMode.CodeOnly => CodeOnlyTemplate,
            _ => "",
        };

    public static string GetDescription(GrammarMode mode)
        => mode switch
        {
            GrammarMode.Json => "JSON object grammar plus JSON response_format where supported.",
            GrammarMode.Regex => "Safe identifier/path starter grammar; edit the GBNF for your pattern.",
            GrammarMode.CodeOnly => "Line-oriented text grammar for code-only responses.",
            GrammarMode.Custom => "Custom llama.cpp GBNF supplied in the editor.",
            _ => "No output constraint is sent.",
        };
}
