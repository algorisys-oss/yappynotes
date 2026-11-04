using System.Text;

namespace YappyNotes.Core;

/// <summary>Changes the heading prefix of every editable line touched by a selection.</summary>
public static class MarkdownHeading
{
    private sealed record PrefixChange(int Start, int TextStart, string Prefix)
    {
        public int OldWidth => TextStart - Start;
        public int Delta => Prefix.Length - OldWidth;
    }

    public static bool CanSetLevel(string content, int selectionStart, int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SelectedBlocks(content, selectionStart, selectionEnd) is not null;
    }

    public static MarkdownEdit SetLevel(string content, int selectionStart, int selectionEnd, int level)
    {
        ArgumentNullException.ThrowIfNull(content);
        var unchanged = new MarkdownEdit(content, selectionStart, selectionEnd);
        if (level is < 0 or > 4 || SelectedBlocks(content, selectionStart, selectionEnd) is not { } blocks)
        {
            return unchanged;
        }

        var prefix = level == 0 ? string.Empty : new string('#', level) + " ";
        var changes = blocks.Where(block =>
                !content.AsSpan(block.SourceStart, block.TextStart - block.SourceStart).SequenceEqual(prefix))
            .Select(block => new PrefixChange(block.SourceStart, block.TextStart, prefix))
            .ToList();
        if (changes.Count == 0)
        {
            return unchanged;
        }

        var result = new StringBuilder(content.Length + changes.Sum(change => change.Delta));
        var copiedThrough = 0;
        foreach (var change in changes)
        {
            result.Append(content, copiedThrough, change.Start - copiedThrough);
            result.Append(change.Prefix);
            copiedThrough = change.TextStart;
        }

        result.Append(content, copiedThrough, content.Length - copiedThrough);
        return new MarkdownEdit(result.ToString(), Move(selectionStart, changes), Move(selectionEnd, changes));
    }

    private static int Move(int position, IReadOnlyList<PrefixChange> changes)
    {
        var offset = 0;
        foreach (var change in changes)
        {
            if (position < change.Start)
            {
                break;
            }

            if (position <= change.TextStart)
            {
                return change.Start + offset + change.Prefix.Length;
            }

            offset += change.Delta;
        }

        return position + offset;
    }

    private static IReadOnlyList<MarkdownBlock>? SelectedBlocks(string content, int start, int end)
    {
        if (start < 0 || end < start || end > content.Length)
        {
            return null;
        }

        var parsed = NoteMarkdown.Parse(content);
        var selected = new List<MarkdownBlock>();
        for (var i = 0; i < parsed.Count; i++)
        {
            var block = parsed[i];
            var nextStart = i + 1 < parsed.Count ? parsed[i + 1].SourceStart : content.Length;
            var touched = start == end
                ? start >= block.SourceStart && start <= block.SourceStart + block.SourceLength
                : start < nextStart && end > block.SourceStart;
            if (!touched)
            {
                continue;
            }

            if (block.Kind is not (MarkdownBlockKind.Paragraph or MarkdownBlockKind.Heading))
            {
                return null;
            }

            selected.Add(block);
        }

        return selected.Count == 0 ? null : selected;
    }
}
