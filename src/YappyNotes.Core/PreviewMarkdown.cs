namespace YappyNotes.Core;

/// <summary>
/// Applies source edits to text selected in the formatted note. The source span
/// must contain only visible text and adjacent line breaks; the result must draw
/// the same characters and links, changing only the requested emphasis.
/// </summary>
public static class PreviewMarkdown
{
    private readonly record struct DrawnCharacter(char Text, RunStyle Style, Uri? Link, bool Selected);

    public static MarkdownEdit? ToggleEmphasis(string content, int start, int end, Emphasis emphasis)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (start < 0 || end <= start || end > content.Length
            || !IsTextBoundary(content, start) || !IsTextBoundary(content, end))
        {
            return null;
        }

        var before = NoteMarkdown.Parse(content);
        if (!CoversOnlyEditableText(content, before, start, end))
        {
            return null;
        }

        var edit = MarkdownEmphasis.Toggle(content, start, end, emphasis);
        if (edit.Content == content)
        {
            return null;
        }

        // MarkdownEmphasis leaves whitespace outside its markers and returns a
        // selection trimmed to the marked text. A Preview drag may include those
        // spaces, so restore them to the selection before validating the redraw.
        var leading = 0;
        while (start + leading < end && char.IsWhiteSpace(content[start + leading]))
        {
            leading++;
        }

        var trailing = 0;
        while (end - trailing > start + leading && char.IsWhiteSpace(content[end - trailing - 1]))
        {
            trailing++;
        }

        var markerWidth = emphasis == Emphasis.Bold ? 2 : 1;
        edit = edit with
        {
            SelectionStart = edit.SelectionStart - (leading > 0 ? leading + markerWidth : 0),
            SelectionEnd = edit.SelectionEnd + (trailing > 0 ? trailing + markerWidth : 0),
        };

        var after = NoteMarkdown.Parse(edit.Content);
        if (before.Count != after.Count || before.Where((block, index) =>
                block.Kind != after[index].Kind || block.Level != after[index].Level).Any())
        {
            return null;
        }

        var source = DrawnCharacters(before, start, end);
        var result = DrawnCharacters(after, edit.SelectionStart, edit.SelectionEnd);
        if (source.Count != result.Count)
        {
            return null;
        }

        var flag = emphasis == Emphasis.Bold ? RunStyle.Bold : RunStyle.Italic;
        var selectedText = source.Where(character => character.Selected && character.Text != '\n').ToList();
        if (selectedText.Count == 0)
        {
            return null;
        }

        var shouldHaveStyle = !selectedText.All(character => character.Style.HasFlag(flag));
        for (var i = 0; i < source.Count; i++)
        {
            var old = source[i];
            var next = result[i];
            if (old.Text != next.Text || old.Link != next.Link || old.Selected != next.Selected)
            {
                return null;
            }

            if (!old.Selected || old.Text == '\n')
            {
                if (old.Style != next.Style)
                {
                    return null;
                }
            }
            else if (char.IsWhiteSpace(old.Text) && old.Style == next.Style)
            {
                // Edge spaces stay outside the Markdown markers. They remain
                // selected, even though their visual style does not change.
                continue;
            }
            else if (next.Style.HasFlag(flag) != shouldHaveStyle
                     || (old.Style & ~flag) != (next.Style & ~flag))
            {
                return null;
            }
        }

        return edit;
    }

    private static bool CoversOnlyEditableText(string content, IReadOnlyList<MarkdownBlock> blocks, int start, int end)
    {
        var covered = new bool[end - start];
        var selectedBlocks = new bool[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            foreach (var run in blocks[i].Runs)
            {
                if (run.Style.HasFlag(RunStyle.Code))
                {
                    continue;
                }

                var from = Math.Max(start, run.SourceStart);
                var through = Math.Min(end, run.SourceStart + run.Text.Length);
                if (from >= through)
                {
                    continue;
                }

                selectedBlocks[i] = true;
                for (var position = from; position < through; position++)
                {
                    covered[position - start] = true;
                }
            }
        }

        for (var i = 0; i + 1 < blocks.Count; i++)
        {
            if (!selectedBlocks[i] || !selectedBlocks[i + 1])
            {
                continue;
            }

            var from = blocks[i].SourceStart + blocks[i].SourceLength;
            var through = blocks[i + 1].SourceStart;
            for (var position = Math.Max(start, from); position < Math.Min(end, through); position++)
            {
                if (content[position] is not ('\r' or '\n'))
                {
                    return false;
                }

                covered[position - start] = true;
            }
        }

        return covered.All(value => value);
    }

    private static List<DrawnCharacter> DrawnCharacters(IReadOnlyList<MarkdownBlock> blocks, int start, int end)
    {
        var drawn = new List<DrawnCharacter>();
        for (var i = 0; i < blocks.Count; i++)
        {
            foreach (var run in blocks[i].Runs)
            {
                for (var offset = 0; offset < run.Text.Length; offset++)
                {
                    var position = run.SourceStart + offset;
                    drawn.Add(new DrawnCharacter(run.Text[offset], run.Style, run.Link,
                        position >= start && position < end));
                }
            }

            if (i + 1 < blocks.Count)
            {
                var position = blocks[i].SourceStart + blocks[i].SourceLength;
                drawn.Add(new DrawnCharacter('\n', RunStyle.None, null, position >= start && position < end));
            }
        }

        return drawn;
    }

    private static bool IsTextBoundary(string content, int index)
        => index == 0 || index == content.Length
            || !char.IsHighSurrogate(content[index - 1]) || !char.IsLowSurrogate(content[index]);
}
