using YappyNotes.Core;

namespace YappyNotes.ViewModels;

/// <summary>
/// A selection in the formatted note, expressed in offsets of its Markdown source.
/// An interval is accepted only when it contains visible, editable text and line
/// breaks between selected blocks. Hidden Markdown syntax cannot be edited by
/// accident through a selection in the preview.
/// </summary>
public sealed class PreviewSelection
{
    private PreviewSelection(int anchor, int focus, IReadOnlyList<int> blocks)
    {
        Anchor = anchor;
        Focus = focus;
        Blocks = blocks;
    }

    public int Anchor { get; }

    public int Focus { get; }

    public int Start => Math.Min(Anchor, Focus);

    public int End => Math.Max(Anchor, Focus);

    /// <summary>Zero-based indices of source blocks containing selected text.</summary>
    public IReadOnlyList<int> Blocks { get; }

    /// <summary>The characters highlighted on screen, without Markdown markers.</summary>
    public string VisibleText(string content) => TextInRange(content, Start, End).Text;

    /// <summary>
    /// Restore a selection after a formatting edit has inserted hidden syntax
    /// between its two ends. Pointer-created selections remain stricter through
    /// <see cref="TryCreate"/>; this only accepts the same visible text as before.
    /// </summary>
    public static PreviewSelection? Restore(string content, int anchor, int focus, string expectedText)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(expectedText);
        if (anchor < 0 || focus < 0 || anchor > content.Length || focus > content.Length
            || anchor == focus || !IsTextBoundary(content, anchor) || !IsTextBoundary(content, focus))
        {
            return null;
        }

        var (text, blocks) = TextInRange(content, Math.Min(anchor, focus), Math.Max(anchor, focus));
        if (text.Length == 0 || text != expectedText
            || blocks.Where((block, index) => index > 0 && block != blocks[index - 1] + 1).Any())
        {
            return null;
        }

        return new PreviewSelection(anchor, focus, Array.AsReadOnly(blocks.ToArray()));
    }

    /// <summary>Maps boundaries in the rendered lines to a safe source interval.</summary>
    public static PreviewSelection? FromRendered(string content, IReadOnlyList<NoteLine> lines,
        int anchorLine, int anchorBoundary, int focusLine, int focusBoundary)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(lines);
        if (anchorLine < 0 || anchorLine >= lines.Count || focusLine < 0 || focusLine >= lines.Count)
        {
            return null;
        }

        var forward = anchorLine < focusLine || (anchorLine == focusLine && anchorBoundary < focusBoundary);
        var startLine = forward ? anchorLine : focusLine;
        var startBoundary = forward ? anchorBoundary : focusBoundary;
        var endLine = forward ? focusLine : anchorLine;
        var endBoundary = forward ? focusBoundary : anchorBoundary;
        var start = lines[startLine].SourceBoundaryAt(startBoundary, startOfSelection: true);
        var end = lines[endLine].SourceBoundaryAt(endBoundary, startOfSelection: false);
        if (start is null || end is null)
        {
            return null;
        }

        return forward ? TryCreate(content, start.Value, end.Value) : TryCreate(content, end.Value, start.Value);
    }

    /// <summary>
    /// Validates a source interval already located by the view's text hit testing.
    /// Mapping a pointer gesture to these offsets belongs to a later UI step.
    /// </summary>
    public static PreviewSelection? TryCreate(string content, int anchor, int focus)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (anchor < 0 || focus < 0 || anchor > content.Length || focus > content.Length || anchor == focus
            || !IsTextBoundary(content, anchor) || !IsTextBoundary(content, focus))
        {
            return null;
        }

        var start = Math.Min(anchor, focus);
        var end = Math.Max(anchor, focus);
        var blocks = NoteMarkdown.Parse(content);
        var selected = new bool[blocks.Count];
        var spans = new List<(int Start, int End)>();
        var selectedBlocks = new List<int>();

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.Kind is not (MarkdownBlockKind.Paragraph or MarkdownBlockKind.Heading
                or MarkdownBlockKind.Bullet or MarkdownBlockKind.Task))
            {
                continue;
            }

            foreach (var run in block.Runs)
            {
                if (run.Style.HasFlag(RunStyle.Code))
                {
                    continue;
                }

                var visibleStart = Math.Max(start, run.SourceStart);
                var visibleEnd = Math.Min(end, run.SourceStart + run.Text.Length);
                if (visibleStart >= visibleEnd)
                {
                    continue;
                }

                spans.Add((visibleStart, visibleEnd));
                if (!selected[i])
                {
                    selected[i] = true;
                    selectedBlocks.Add(i);
                }
            }
        }

        for (var i = 0; i + 1 < blocks.Count; i++)
        {
            if (!selected[i] || !selected[i + 1])
            {
                continue;
            }

            var breakStart = blocks[i].SourceStart + blocks[i].SourceLength;
            var breakEnd = blocks[i + 1].SourceStart;
            spans.Add((Math.Max(start, breakStart), Math.Min(end, breakEnd)));
        }

        var coveredUntil = start;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            if (span.Start > coveredUntil)
            {
                return null;
            }

            coveredUntil = Math.Max(coveredUntil, span.End);
        }

        return selectedBlocks.Count > 0 && coveredUntil >= end
            ? new PreviewSelection(anchor, focus, Array.AsReadOnly(selectedBlocks.ToArray()))
            : null;
    }

    private static bool IsTextBoundary(string content, int index)
        => index == 0 || index == content.Length
            || !char.IsHighSurrogate(content[index - 1]) || !char.IsLowSurrogate(content[index]);

    private static (string Text, List<int> Blocks) TextInRange(string content, int start, int end)
    {
        var parts = new List<string>();
        var selectedBlocks = new List<int>();
        var blocks = NoteMarkdown.Parse(content);
        for (var i = 0; i < blocks.Count; i++)
        {
            var line = new System.Text.StringBuilder();
            foreach (var run in blocks[i].Runs)
            {
                if (run.Style.HasFlag(RunStyle.Code))
                {
                    continue;
                }

                var from = Math.Max(start, run.SourceStart);
                var through = Math.Min(end, run.SourceStart + run.Text.Length);
                if (from < through)
                {
                    line.Append(run.Text, from - run.SourceStart, through - from);
                }
            }

            if (line.Length > 0)
            {
                parts.Add(line.ToString());
                selectedBlocks.Add(i);
            }
        }

        return (string.Join("\n", parts), selectedBlocks);
    }
}
