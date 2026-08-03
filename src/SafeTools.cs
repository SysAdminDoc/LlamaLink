#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaLink;

public sealed record SafeToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, object> Parameters)
{
    public Dictionary<string, object> ToPayload()
    {
        return new Dictionary<string, object>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object>
            {
                ["name"] = Name,
                ["description"] = Description,
                ["parameters"] = Parameters,
            },
        };
    }
}

public sealed record ToolCallRequest(string Id, string Name, string ArgumentsJson);

public sealed record ToolExecutionResult(bool Success, string Content)
{
    public static ToolExecutionResult Error(string message) => new(false, message);
}

public static class SafeToolRegistry
{
    public static IReadOnlyList<SafeToolDefinition> GetDefinitions(
        bool fileRead,
        bool calculator,
        bool pythonEvaluation,
        bool webSearch = false)
    {
        var definitions = new List<SafeToolDefinition>();
        if (fileRead)
        {
            definitions.Add(new SafeToolDefinition(
                "read_file",
                "Read a UTF-8 text file beneath the configured safe tool root. The user must confirm before execution.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["path"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Relative file path beneath the safe tool root",
                        },
                    },
                    ["required"] = new[] { "path" },
                    ["additionalProperties"] = false,
                }));
        }

        if (calculator)
        {
            definitions.Add(new SafeToolDefinition(
                "calculator",
                "Evaluate a basic arithmetic expression using numbers, parentheses, +, -, *, /, %, and ^.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["expression"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Arithmetic expression",
                        },
                    },
                    ["required"] = new[] { "expression" },
                    ["additionalProperties"] = false,
                }));
        }

        if (pythonEvaluation)
        {
            definitions.Add(new SafeToolDefinition(
                "python_eval",
                "Evaluate a restricted Python numeric expression in an isolated subprocess; imports, calls, names, and file access are disabled.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["code"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "A numeric Python expression, not a statement or script",
                        },
                    },
                    ["required"] = new[] { "code" },
                    ["additionalProperties"] = false,
                }));
        }

        if (webSearch)
        {
            definitions.Add(new SafeToolDefinition(
                "web_search",
                "Search the web through the configured DuckDuckGo endpoint or SearxNG proxy. The user must confirm before any network request.",
                new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["query"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "A concise web search query",
                        },
                        ["max_results"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = 8,
                        },
                    },
                    ["required"] = new[] { "query" },
                    ["additionalProperties"] = false,
                }));
        }

        return definitions;
    }
}

public sealed class ToolCallAccumulator
{
    private sealed class Draft
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public StringBuilder Arguments { get; } = new();
    }

    private readonly Dictionary<string, Draft> _drafts = new(StringComparer.Ordinal);

    public void Add(IEnumerable<BackendToolCallFragment> fragments)
    {
        foreach (var fragment in fragments)
        {
            var key = string.IsNullOrEmpty(fragment.Index) ? _drafts.Count.ToString() : fragment.Index;
            if (!_drafts.TryGetValue(key, out var draft))
            {
                draft = new Draft();
                _drafts[key] = draft;
            }

            if (!string.IsNullOrEmpty(fragment.Id)) draft.Id = fragment.Id;
            if (!string.IsNullOrEmpty(fragment.Name)) draft.Name += fragment.Name;
            draft.Arguments.Append(fragment.Arguments);
        }
    }

    public IReadOnlyList<ToolCallRequest> Complete()
    {
        return _drafts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value.Name))
            .Select(pair => new ToolCallRequest(
                pair.Value.Id,
                pair.Value.Name,
                string.IsNullOrWhiteSpace(pair.Value.Arguments.ToString()) ? "{}" : pair.Value.Arguments.ToString()))
            .ToArray();
    }
}

public static class SafeToolExecutor
{
    private const int MaxFileBytes = 64 * 1024;
    private const string PythonScript = """
        import ast, operator, sys

        expression = sys.argv[1]
        tree = ast.parse(expression, mode="eval")
        allowed = (ast.Expression, ast.Constant, ast.BinOp, ast.UnaryOp,
                   ast.Add, ast.Sub, ast.Mult, ast.Div, ast.Mod, ast.Pow,
                   ast.UAdd, ast.USub)
        for node in ast.walk(tree):
            if not isinstance(node, allowed):
                raise ValueError("only numeric operators are allowed")
            if isinstance(node, ast.Constant) and (isinstance(node.value, bool) or not isinstance(node.value, (int, float))):
                raise ValueError("only numeric constants are allowed")
        result = eval(compile(tree, "<llamalink-tool>", "eval"), {"__builtins__": {}}, {})
        if not isinstance(result, (int, float)) or isinstance(result, bool):
            raise ValueError("expression did not return a number")
        print(result)
        """;

    public static async Task<ToolExecutionResult> ExecuteAsync(
        ToolCallRequest request,
        string safeRoot,
        CancellationToken cancellationToken = default,
        WebSearchOptions? webSearchOptions = null)
    {
        try
        {
            using var arguments = JsonDocument.Parse(request.ArgumentsJson);
            return request.Name switch
            {
                "read_file" => ReadFile(arguments.RootElement, safeRoot),
                "calculator" => Calculate(arguments.RootElement),
                "python_eval" => await EvaluatePythonAsync(arguments.RootElement, safeRoot, cancellationToken),
                "web_search" => await ExecuteWebSearchAsync(arguments.RootElement, webSearchOptions, cancellationToken),
                _ => ToolExecutionResult.Error($"Unknown tool: {request.Name}"),
            };
        }
        catch (JsonException ex)
        {
            return ToolExecutionResult.Error($"Invalid tool arguments: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExecutionResult.Error(ex.Message);
        }
    }

    private static async Task<ToolExecutionResult> ExecuteWebSearchAsync(
        JsonElement arguments,
        WebSearchOptions? options,
        CancellationToken cancellationToken)
    {
        if (options is null)
            return ToolExecutionResult.Error("Web search is not configured.");
        var query = ReadString(arguments, "query");
        var maxResults = arguments.TryGetProperty("max_results", out var max)
            && max.TryGetInt32(out var parsedMax)
            ? Math.Clamp(parsedMax, 1, 8)
            : options.MaxResults;
        return await WebSearchService.SearchAsync(
            query,
            options with { MaxResults = maxResults },
            cancellationToken);
    }

    private static ToolExecutionResult ReadFile(JsonElement arguments, string safeRoot)
    {
        var relativePath = ReadString(arguments, "path");
        if (string.IsNullOrWhiteSpace(relativePath))
            return ToolExecutionResult.Error("read_file requires a path.");
        if (string.IsNullOrWhiteSpace(safeRoot) || !Directory.Exists(safeRoot))
            return ToolExecutionResult.Error("The configured safe tool root does not exist.");

        var rootInfo = new DirectoryInfo(Path.GetFullPath(safeRoot));
        var resolvedRoot = rootInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? rootInfo.FullName;
        var root = resolvedRoot
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return ToolExecutionResult.Error("Path is outside the configured safe tool root.");
        if (!File.Exists(candidate))
            return ToolExecutionResult.Error($"File not found: {relativePath}");

        var resolvedCandidate = new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate;
        if (!resolvedCandidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return ToolExecutionResult.Error("File link resolves outside the configured safe tool root.");

        var info = new FileInfo(candidate);
        if (info.Length > MaxFileBytes)
            return ToolExecutionResult.Error($"File is larger than the {MaxFileBytes:N0}-byte safe limit.");

        var content = File.ReadAllText(candidate, Encoding.UTF8);
        return new ToolExecutionResult(true, content);
    }

    private static ToolExecutionResult Calculate(JsonElement arguments)
    {
        var expression = ReadString(arguments, "expression");
        if (string.IsNullOrWhiteSpace(expression))
            return ToolExecutionResult.Error("calculator requires an expression.");

        var value = SafeCalculator.Evaluate(expression);
        return new ToolExecutionResult(true, value.ToString("G15", CultureInfo.InvariantCulture));
    }

    private static async Task<ToolExecutionResult> EvaluatePythonAsync(
        JsonElement arguments,
        string safeRoot,
        CancellationToken cancellationToken)
    {
        var code = ReadString(arguments, "code");
        if (string.IsNullOrWhiteSpace(code))
            return ToolExecutionResult.Error("python_eval requires an expression.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "python",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Directory.Exists(safeRoot) ? Path.GetFullPath(safeRoot) : Environment.CurrentDirectory,
            }
        };
        process.StartInfo.ArgumentList.Add("-I");
        process.StartInfo.ArgumentList.Add("-S");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(PythonScript);
        process.StartInfo.ArgumentList.Add(code);

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            return process.ExitCode == 0
                ? new ToolExecutionResult(true, output.Trim())
                : ToolExecutionResult.Error(string.IsNullOrWhiteSpace(error) ? "Python expression failed." : error.Trim());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }
            throw;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ToolExecutionResult.Error("Python was not found on PATH.");
        }
    }

    private static string ReadString(JsonElement arguments, string name)
    {
        return arguments.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }
}

public static class SafeCalculator
{
    public static double Evaluate(string expression)
    {
        var parser = new Parser(expression);
        var result = parser.ParseExpression();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
            throw new FormatException($"Unexpected character at position {parser.Position}.");
        if (double.IsNaN(result) || double.IsInfinity(result))
            throw new ArithmeticException("The result is not finite.");
        return result;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _position;

        public Parser(string text) => _text = text ?? "";
        public int Position => _position;
        public bool AtEnd => _position >= _text.Length;

        public double ParseExpression()
        {
            var value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('+')) value += ParseTerm();
                else if (TryConsume('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            var value = ParsePower();
            while (true)
            {
                SkipWhitespace();
                if (TryConsume('*')) value *= ParsePower();
                else if (TryConsume('/'))
                {
                    var divisor = ParsePower();
                    if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException();
                    value /= divisor;
                }
                else if (TryConsume('%'))
                {
                    var divisor = ParsePower();
                    if (Math.Abs(divisor) < double.Epsilon) throw new DivideByZeroException();
                    value %= divisor;
                }
                else return value;
            }
        }

        private double ParsePower()
        {
            var value = ParseUnary();
            SkipWhitespace();
            return TryConsume('^') ? Math.Pow(value, ParsePower()) : value;
        }

        private double ParseUnary()
        {
            SkipWhitespace();
            if (TryConsume('+')) return ParseUnary();
            if (TryConsume('-')) return -ParseUnary();
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipWhitespace();
            if (TryConsume('('))
            {
                var groupedValue = ParseExpression();
                if (!TryConsume(')')) throw new FormatException("Missing closing parenthesis.");
                return groupedValue;
            }

            var start = _position;
            while (!AtEnd && (char.IsDigit(_text[_position]) || _text[_position] is '.' or 'e' or 'E' or '+' or '-'))
            {
                if ((_text[_position] is '+' or '-') && _position > start
                    && _text[_position - 1] is not 'e' and not 'E') break;
                _position++;
            }
            if (start == _position
                || !double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw new FormatException($"Expected a number at position {start}.");
            }
            return number;
        }

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(_text[_position])) _position++;
        }

        private bool TryConsume(char character)
        {
            SkipWhitespace();
            if (AtEnd || _text[_position] != character) return false;
            _position++;
            return true;
        }
    }
}
