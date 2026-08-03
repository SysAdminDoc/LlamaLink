#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace LlamaLink;

public sealed class ChatImageAttachment
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = "image/png";

    [JsonIgnore]
    public string DisplayName => System.IO.Path.GetFileName(Path);

    public ChatImageAttachment Clone() => new() { Path = Path, MimeType = MimeType };
}

public static class VisionImageStore
{
    public const long MaxImageBytes = 10L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> MimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
        };

    public static bool IsSupported(string path)
        => MimeTypes.ContainsKey(System.IO.Path.GetExtension(path));

    public static ChatImageAttachment Create(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Image path is empty.");

        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Image file was not found.", fullPath);
        if (!MimeTypes.TryGetValue(System.IO.Path.GetExtension(fullPath), out var mimeType))
            throw new InvalidDataException("Only PNG, JPEG, WebP, and GIF images are supported.");
        if (new FileInfo(fullPath).Length > MaxImageBytes)
            throw new InvalidDataException($"Images larger than {MaxImageBytes / (1024 * 1024)} MB are not attached.");

        return new ChatImageAttachment { Path = fullPath, MimeType = mimeType };
    }

    public static bool TryRead(ChatImageAttachment attachment, out string base64)
    {
        base64 = "";
        try
        {
            if (!IsSupported(attachment.Path) || !File.Exists(attachment.Path))
                return false;
            var info = new FileInfo(attachment.Path);
            if (info.Length <= 0 || info.Length > MaxImageBytes)
                return false;
            base64 = Convert.ToBase64String(File.ReadAllBytes(attachment.Path));
            return true;
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

    public static IReadOnlyList<ChatImageAttachment> CloneAll(IEnumerable<ChatImageAttachment> attachments)
        => attachments.Select(attachment => attachment.Clone()).ToList();
}
