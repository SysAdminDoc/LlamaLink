#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LlamaLink;

public sealed class RagChunk
{
    [JsonPropertyName("source_path")]
    public string SourcePath { get; set; } = "";

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

public sealed class RagIndexDocument
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("chunks")]
    public List<RagChunk> Chunks { get; set; } = new();
}

public sealed class RagSearchResult
{
    public string SourcePath { get; init; } = "";
    public string SourceName => Path.GetFileName(SourcePath);
    public int ChunkIndex { get; init; }
    public string Text { get; init; } = "";
    public double Score { get; init; }
}

public sealed class RagIndexingResult
{
    public int FilesIndexed { get; init; }
    public int ChunksIndexed { get; init; }
    public List<string> Errors { get; init; } = new();
}

public sealed class RagIndex
{
    private readonly List<RagChunk> _chunks;

    public RagIndex(IEnumerable<RagChunk>? chunks = null)
    {
        _chunks = chunks?.ToList() ?? new List<RagChunk>();
        foreach (var chunk in _chunks)
        {
            if (chunk.Embedding.Length != RagEmbedding.Dimensions)
                chunk.Embedding = RagEmbedding.Create(chunk.Text);
        }
    }

    public int ChunkCount => _chunks.Count;

    public IReadOnlyList<string> SourcePaths => _chunks
        .Select(chunk => chunk.SourcePath)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public RagIndexingResult IndexFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var indexedFiles = 0;
        var indexedChunks = 0;
        var errors = new List<string>();

        foreach (var path in paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException("File not found.", path);
                if (!RagTextExtractor.IsSupported(path))
                    throw new InvalidDataException("Only .pdf, .md, and .txt files are supported.");

                var text = RagTextExtractor.Extract(path);
                var chunks = RagChunker.Chunk(text);
                if (chunks.Count == 0)
                    throw new InvalidDataException("No indexable text was found.");

                _chunks.RemoveAll(chunk => string.Equals(chunk.SourcePath, path, StringComparison.OrdinalIgnoreCase));
                _chunks.AddRange(chunks.Select((chunk, index) => new RagChunk
                {
                    SourcePath = path,
                    ChunkIndex = index,
                    Text = chunk,
                    Embedding = RagEmbedding.Create(chunk),
                }));
                indexedFiles++;
                indexedChunks += chunks.Count;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return new RagIndexingResult
        {
            FilesIndexed = indexedFiles,
            ChunksIndexed = indexedChunks,
            Errors = errors,
        };
    }

    public void Clear() => _chunks.Clear();

    public List<RagSearchResult> Search(string query, int topK = 4)
    {
        if (string.IsNullOrWhiteSpace(query) || topK <= 0 || _chunks.Count == 0)
            return new List<RagSearchResult>();

        var queryVector = RagEmbedding.Create(query);
        return _chunks
            .Select(chunk => new RagSearchResult
            {
                SourcePath = chunk.SourcePath,
                ChunkIndex = chunk.ChunkIndex,
                Text = chunk.Text,
                Score = RagEmbedding.Cosine(queryVector, chunk.Embedding),
            })
            .Where(result => result.Score > 0.05)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.ChunkIndex)
            .Take(Math.Clamp(topK, 1, 12))
            .ToList();
    }

    public RagIndexDocument ToDocument() => new()
    {
        SchemaVersion = 1,
        Chunks = _chunks.Select(chunk => new RagChunk
        {
            SourcePath = chunk.SourcePath,
            ChunkIndex = chunk.ChunkIndex,
            Text = chunk.Text,
            Embedding = chunk.Embedding,
        }).ToList(),
    };

    public static string FormatContext(IEnumerable<RagSearchResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var builder = new StringBuilder();
        builder.AppendLine("Use the following local document excerpts when they are relevant. Cite the source name in your answer.");
        foreach (var result in results)
        {
            builder.AppendLine($"[Source: {result.SourceName}, chunk {result.ChunkIndex + 1}, relevance {result.Score:F2}]");
            builder.AppendLine(result.Text);
            builder.AppendLine();
        }
        return builder.ToString().Trim();
    }
}

public static class RagIndexStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static RagIndex Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new RagIndex();

        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<RagIndexDocument>(json)
            ?? throw new JsonException("RAG index is empty.");
        if (document.SchemaVersion != 1)
            throw new JsonException($"Unsupported RAG index schema {document.SchemaVersion}.");
        return new RagIndex(document.Chunks ?? new List<RagChunk>());
    }

    public static void Save(string path, RagIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Index path is required.", nameof(path));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".part";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(index.ToDocument(), WriteOptions));
        File.Move(temporaryPath, path, true);
    }
}

public static class RagTextExtractor
{
    private static readonly Regex PdfBlockRegex = new(@"BT(?<body>.*?)ET", RegexOptions.Singleline | RegexOptions.Compiled);

    public static bool IsSupported(string path)
        => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase);

    public static string Extract(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (info.Length > 50L * 1024 * 1024)
            throw new InvalidDataException("Files larger than 50 MB are not indexed.");

        var extension = Path.GetExtension(path);
        var text = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase)
            ? ExtractPdf(File.ReadAllBytes(path))
            : File.ReadAllText(path, Encoding.UTF8);
        return RagTextNormalizer.Normalize(text);
    }

    private static string ExtractPdf(byte[] bytes)
    {
        var raw = Encoding.Latin1.GetString(bytes);
        var builder = new StringBuilder();
        foreach (Match block in PdfBlockRegex.Matches(raw))
        {
            foreach (var value in ReadPdfLiteralStrings(block.Groups["body"].Value))
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(value);
            }
        }

        if (builder.Length == 0)
            throw new InvalidDataException("The PDF has no plain-text operators that can be extracted locally.");
        return builder.ToString();
    }

    private static IEnumerable<string> ReadPdfLiteralStrings(string block)
    {
        for (var index = 0; index < block.Length; index++)
        {
            if (block[index] != '(')
                continue;

            var depth = 1;
            var value = new StringBuilder();
            for (index++; index < block.Length && depth > 0; index++)
            {
                var current = block[index];
                if (current == '\\' && index + 1 < block.Length)
                {
                    var escaped = block[++index];
                    if (escaped is >= '0' and <= '7')
                    {
                        var octal = new StringBuilder().Append(escaped);
                        for (var digit = 0; digit < 2 && index + 1 < block.Length
                            && block[index + 1] is >= '0' and <= '7'; digit++)
                            octal.Append(block[++index]);
                        value.Append((char)Convert.ToInt32(octal.ToString(), 8));
                    }
                    else
                    {
                        value.Append(escaped switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            'b' => '\b',
                            'f' => '\f',
                            _ => escaped,
                        });
                    }
                }
                else if (current == '(')
                {
                    depth++;
                    value.Append(current);
                }
                else if (current == ')')
                {
                    depth--;
                    if (depth > 0)
                        value.Append(current);
                }
                else
                {
                    value.Append(current);
                }
            }

            if (depth == 0)
                yield return value.ToString();
        }
    }
}

public static class RagTextNormalizer
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        return Regex.Replace(text.Replace('\0', ' '), @"\s+", " ").Trim();
    }
}

public static class RagChunker
{
    public const int DefaultChunkSize = 1200;
    public const int DefaultOverlap = 180;

    public static List<string> Chunk(string text, int maxChars = DefaultChunkSize, int overlap = DefaultOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string>();
        if (maxChars < 128 || overlap < 0 || overlap >= maxChars)
            throw new ArgumentOutOfRangeException(nameof(maxChars), "Chunk size and overlap are invalid.");

        var normalized = RagTextNormalizer.Normalize(text);
        var chunks = new List<string>();
        var cursor = 0;
        while (cursor < normalized.Length)
        {
            var end = Math.Min(normalized.Length, cursor + maxChars);
            if (end < normalized.Length)
            {
                var split = normalized.LastIndexOf(' ', end - 1, Math.Min(maxChars / 3, end - cursor));
                if (split > cursor + maxChars / 2)
                    end = split;
            }

            var chunk = normalized[cursor..end].Trim();
            if (chunk.Length > 0)
                chunks.Add(chunk);
            if (end >= normalized.Length)
                break;
            cursor = Math.Max(cursor + 1, end - overlap);
        }
        return chunks;
    }
}

public static class RagEmbedding
{
    public const int Dimensions = 384;
    private static readonly Regex TokenRegex = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    public static float[] Create(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match match in TokenRegex.Matches(text.ToLowerInvariant()))
        {
            var hash = Hash(match.Value);
            vector[hash % Dimensions] += 1;
            if (match.Value.Length >= 6)
                vector[(hash / Dimensions + 17) % Dimensions] += 0.35f;
        }

        var norm = Math.Sqrt(vector.Sum(value => value * value));
        if (norm > 0)
        {
            for (var index = 0; index < vector.Length; index++)
                vector[index] = (float)(vector[index] / norm);
        }
        return vector;
    }

    public static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count || left.Count == 0)
            return 0;
        var score = 0.0;
        for (var index = 0; index < left.Count; index++)
            score += left[index] * right[index];
        return score;
    }

    private static int Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= 16777619;
        }
        return (int)(hash & 0x7FFFFFFF);
    }
}
