namespace YappyNotes.Core;

/// <summary>Writes a local image reference into a note's Markdown text.</summary>
public static class NoteImageReference
{
    /// <summary>
    /// Inserts a reference on its own line and places the caret on the following
    /// line when the note has other content. An unsupported URI leaves the edit
    /// unchanged. This function only edits text and never reads the file.
    /// </summary>
    public static MarkdownEdit Insert(string content, int caret, Uri fileUri, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fileUri);

        if (!fileUri.IsAbsoluteUri)
        {
            return new MarkdownEdit(content, caret, caret);
        }

        var at = Math.Clamp(caret, 0, content.Length);
        var label = description ?? DescriptionFrom(fileUri);
        var escapedLabel = EscapeDescription(label);
        var destination = fileUri.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal)
            .Replace(")", "%29", StringComparison.Ordinal);
        var reference = $"![{escapedLabel}]({destination})";

        var parsed = NoteMarkdown.Parse(reference);
        if (parsed.Count != 1 || parsed[0].Kind != MarkdownBlockKind.Image)
        {
            return new MarkdownEdit(content, caret, caret);
        }

        // A caret inside CRLF is treated as sitting before the whole line break.
        if (at > 0 && at < content.Length && content[at - 1] == '\r' && content[at] == '\n')
        {
            at--;
        }

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var before = at > 0 && content[at - 1] != '\n' ? newline : string.Empty;
        var followingBreak = at < content.Length && content[at] == '\r' && at + 1 < content.Length && content[at + 1] == '\n'
            ? 2
            : at < content.Length && content[at] is '\r' or '\n' ? 1 : 0;
        var after = content.Length > 0 && followingBreak == 0 ? newline : string.Empty;
        var updated = string.Concat(content[..at], before, reference, after, content[at..]);
        var newCaret = at + before.Length + reference.Length + after.Length + followingBreak;

        return new MarkdownEdit(updated, newCaret, newCaret);
    }

    private static string DescriptionFrom(Uri fileUri)
    {
        var path = fileUri.AbsolutePath;
        var filename = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        var extensionStart = filename.LastIndexOf('.');
        var name = extensionStart > 0 ? filename[..extensionStart] : filename;
        return string.IsNullOrWhiteSpace(name) ? "imagem" : name;
    }

    private static string EscapeDescription(string description)
    {
        var singleLine = string.Join(' ', description.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
