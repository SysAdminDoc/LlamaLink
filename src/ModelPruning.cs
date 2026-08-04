#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LlamaLink;

public sealed record ModelPruneCandidate(string FilePath, long SizeBytes)
{
    public string Name => Path.GetFileName(FilePath);

    public string SizeDisplay => SizeBytes >= 1024L * 1024 * 1024
        ? $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
        : SizeBytes >= 1024L * 1024
            ? $"{SizeBytes / (1024.0 * 1024):F1} MB"
            : SizeBytes >= 1024
                ? $"{SizeBytes / 1024.0:F0} KB"
                : $"{SizeBytes:N0} B";
}

public static class ModelPruner
{
    public static IReadOnlyList<ModelPruneCandidate> FindCandidates(
        string folder,
        IEnumerable<string>? protectedPaths = null)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return Array.Empty<ModelPruneCandidate>();

        string root;
        try
        {
            root = Path.GetFullPath(folder);
        }
        catch (ArgumentException)
        {
            return Array.Empty<ModelPruneCandidate>();
        }
        catch (NotSupportedException)
        {
            return Array.Empty<ModelPruneCandidate>();
        }

        var protectedSet = NormalizePaths(protectedPaths);
        var candidates = new List<ModelPruneCandidate>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.gguf", options))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                catch (NotSupportedException)
                {
                    continue;
                }

                if (!IsPathWithin(root, fullPath)
                    || protectedSet.Contains(fullPath)
                    || !string.Equals(Path.GetExtension(fullPath), ".gguf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(fullPath);
                    if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        continue;

                    candidates.Add(new ModelPruneCandidate(fullPath, info.Length));
                }
                catch (IOException)
                {
                    // A model can disappear or become inaccessible while scanning.
                }
                catch (UnauthorizedAccessException)
                {
                    // A model can disappear or become inaccessible while scanning.
                }
            }
        }
        catch (IOException)
        {
            // Treat an inaccessible folder as an empty scan; the UI reports the result.
        }
        catch (UnauthorizedAccessException)
        {
            // Treat an inaccessible folder as an empty scan; the UI reports the result.
        }

        return candidates
            .GroupBy(candidate => candidate.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(candidate => candidate.SizeBytes)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsSafeToDelete(
        string folder,
        string filePath,
        IEnumerable<string>? protectedPaths = null)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(filePath))
            return false;

        string root;
        string fullPath;
        try
        {
            root = Path.GetFullPath(folder);
            fullPath = Path.GetFullPath(filePath);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        if (!IsPathWithin(root, fullPath)
            || !string.Equals(Path.GetExtension(fullPath), ".gguf", StringComparison.OrdinalIgnoreCase)
            || NormalizePaths(protectedPaths).Contains(fullPath))
        {
            return false;
        }

        try
        {
            var info = new FileInfo(fullPath);
            return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsPathWithin(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
            return false;

        try
        {
            var fullRoot = Path.GetFullPath(root);
            var fullCandidate = Path.GetFullPath(candidate);
            if (string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase))
                return false;

            var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            return fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static HashSet<string> NormalizePaths(IEnumerable<string>? paths)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths is null) return normalized;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                normalized.Add(Path.GetFullPath(path));
            }
            catch (ArgumentException)
            {
                // Ignore malformed optional protection paths.
            }
            catch (NotSupportedException)
            {
                // Ignore malformed optional protection paths.
            }
        }

        return normalized;
    }
}
