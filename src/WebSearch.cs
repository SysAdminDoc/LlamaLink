#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaLink;

public sealed record WebSearchOptions(
    bool Enabled,
    string Provider,
    string Endpoint,
    int MaxResults = 5);

public sealed record WebSearchResult(string Title, string Url, string Snippet);

public static class WebSearchService
{
    private const int MaxQueryLength = 400;
    private const int MaxResponseCharacters = 12000;
    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static async Task<ToolExecutionResult> SearchAsync(
        string query,
        WebSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return ToolExecutionResult.Error("Web search is not enabled.");
        if (string.IsNullOrWhiteSpace(query))
            return ToolExecutionResult.Error("web_search requires a query.");

        query = query.Trim();
        if (query.Length > MaxQueryLength)
            query = query[..MaxQueryLength];

        try
        {
            var uri = string.Equals(options.Provider, "searxng", StringComparison.OrdinalIgnoreCase)
                ? BuildSearxUri(query, options.Endpoint)
                : new Uri($"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&no_redirect=1&skip_disambig=1");
            using var response = await Client.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var results = string.Equals(options.Provider, "searxng", StringComparison.OrdinalIgnoreCase)
                ? ParseSearx(json, options.MaxResults)
                : ParseDuckDuckGo(json, options.MaxResults);
            return new ToolExecutionResult(true, FormatResults(results));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or UriFormatException)
        {
            return ToolExecutionResult.Error($"Web search failed: {ex.Message}");
        }
    }

    public static List<WebSearchResult> ParseDuckDuckGo(string json, int maxResults = 5)
    {
        using var document = JsonDocument.Parse(json);
        var results = new List<WebSearchResult>();
        var root = document.RootElement;
        AddResult(results, root, "AbstractText", "AbstractURL", "DuckDuckGo", maxResults);
        if (root.TryGetProperty("RelatedTopics", out var topics))
            AddDuckTopics(results, topics, maxResults);
        return results.Take(SanitizeMaxResults(maxResults)).ToList();
    }

    public static List<WebSearchResult> ParseSearx(string json, int maxResults = 5)
    {
        using var document = JsonDocument.Parse(json);
        var results = new List<WebSearchResult>();
        if (!document.RootElement.TryGetProperty("results", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var item in items.EnumerateArray())
        {
            var title = ReadString(item, "title");
            var url = ReadString(item, "url");
            var snippet = CleanSnippet(ReadString(item, "content"));
            if (!string.IsNullOrWhiteSpace(title) && Uri.TryCreate(url, UriKind.Absolute, out _))
                results.Add(new WebSearchResult(title, url, snippet));
            if (results.Count >= SanitizeMaxResults(maxResults))
                break;
        }
        return results;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LlamaLink/0.5 local-web-search");
        return client;
    }

    private static Uri BuildSearxUri(string query, string endpoint)
    {
        if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https")
            || !string.IsNullOrWhiteSpace(baseUri.UserInfo))
            throw new UriFormatException("SearxNG endpoint must be an HTTP(S) URL without credentials.");

        var builder = new UriBuilder(baseUri);
        var path = builder.Path.TrimEnd('/');
        if (!path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
            path += "/search";
        builder.Path = path;
        builder.Query = $"q={Uri.EscapeDataString(query)}&format=json&safesearch=1";
        return builder.Uri;
    }

    private static void AddDuckTopics(List<WebSearchResult> results, JsonElement topics, int maxResults)
    {
        if (topics.ValueKind != JsonValueKind.Array)
            return;
        foreach (var topic in topics.EnumerateArray())
        {
            if (topic.TryGetProperty("Topics", out var nested))
                AddDuckTopics(results, nested, maxResults);
            else
                AddResult(results, topic, "Text", "FirstURL", "DuckDuckGo", maxResults);
            if (results.Count >= SanitizeMaxResults(maxResults))
                return;
        }
    }

    private static void AddResult(
        List<WebSearchResult> results,
        JsonElement item,
        string textProperty,
        string urlProperty,
        string fallbackTitle,
        int maxResults)
    {
        var snippet = CleanSnippet(ReadString(item, textProperty));
        var url = ReadString(item, urlProperty);
        if (!string.IsNullOrWhiteSpace(snippet) && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            var title = snippet.Length > 100 ? snippet[..100] + "..." : snippet;
            results.Add(new WebSearchResult(string.IsNullOrWhiteSpace(fallbackTitle) ? title : fallbackTitle, url, snippet));
        }
        if (results.Count >= SanitizeMaxResults(maxResults))
            return;
    }

    private static string FormatResults(IReadOnlyList<WebSearchResult> results)
    {
        if (results.Count == 0)
            return "No web results found.";

        var builder = new StringBuilder("Web search results:\n");
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            builder.AppendLine($"[{index + 1}] {result.Title}");
            builder.AppendLine(result.Url);
            if (!string.IsNullOrWhiteSpace(result.Snippet))
                builder.AppendLine(result.Snippet);
            builder.AppendLine();
        }
        var output = builder.ToString().Trim();
        return output.Length > MaxResponseCharacters
            ? output[..MaxResponseCharacters].Trim()
            : output;
    }

    private static string CleanSnippet(string value)
        => Regex.Replace(WebUtility.HtmlDecode(HtmlTagRegex.Replace(value ?? "", " ")), @"\s+", " ").Trim();

    private static string ReadString(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int SanitizeMaxResults(int value) => Math.Clamp(value, 1, 8);
}
