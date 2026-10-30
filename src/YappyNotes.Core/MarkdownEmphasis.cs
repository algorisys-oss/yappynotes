namespace YappyNotes.Core;

public enum Emphasis
{
    Bold,
    Italic,
}

/// <summary>A note's content after an edit, and what should be selected in it.</summary>
public sealed record MarkdownEdit(string Content, int SelectionStart, int SelectionEnd);

/// <summary>
/// What Ctrl+B and Ctrl+I do to the Markdown: put markers around the selection, or
/// take them off if they are already there.
/// </summary>
/// <remarks>
/// <para>
/// Written against what <see cref="NoteMarkdown"/> will draw, not just against the
/// characters. A marker only closes when text sits straight before it, so
/// spaces at the edges of a selection are left outside. Emphasis never crosses a
/// line, so a selection over several lines is wrapped line by line - and from
/// where each line's text starts, so a checklist's <c>- [ ]</c> is never inside the
/// markers.
/// </para>
/// <para>
/// Asterisks only. Underscores mean the same, but an asterisk is what somebody
/// typing expects to see, and one spelling makes "is this already bold" a
/// question with one answer. <c>***</c> is bold and italic, so toggling one of them
/// leaves the other.
/// </para>
/// </remarks>
public static class MarkdownEmphasis
{
    private const char Marker = '*';

    public static MarkdownEdit Toggle(string content, int selectionStart, int selectionEnd, Emphasis emphasis)
    {
        ArgumentNullException.ThrowIfNull(content);

        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, content.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, content.Length);
        var width = emphasis == Emphasis.Bold ? 2 : 1;

        // A caret with nothing selected gets an empty pair to type into - or, sitting
        // inside one, has it taken away.
        List<Segment> segments = start == end
            ? NoteMarkdown.Parse(content).Any(block => block.Kind == MarkdownBlockKind.Image
                && start >= block.SourceStart && start <= block.SourceStart + block.SourceLength)
                ? []
                : [new Segment(start, end, LineBounds(content, start))]
            : SegmentsOf(content, start, end);

        if (segments.Count == 0)
        {
            return new MarkdownEdit(content, selectionStart, selectionEnd);
        }

        var states = segments.Select(segment => StateOf(content, segment, emphasis)).ToList();
        var removing = states.All(state => state != Marked.No);

        var text = content;
        var delta = 0;
        int? newStart = null;
        var newEnd = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            var (s, e, _) = segments[i];
            s += delta;
            e += delta;

            if (removing && states[i] == Marked.Outside)
            {
                text = text.Remove(e, width).Remove(s - width, width);
                s -= width;
                e -= width;
                delta -= 2 * width;
            }
            else if (removing && states[i] == Marked.Inside)
            {
                text = text.Remove(e - width, width).Remove(s, width);
                e -= 2 * width;
                delta -= 2 * width;
            }
            else if (!removing && states[i] == Marked.No)
            {
                text = text.Insert(e, new string(Marker, width)).Insert(s, new string(Marker, width));
                s += width;
                e += width;
                delta += 2 * width;
            }

            // A line that already has the emphasis while others do not is left as it
            // is: the selection is being made consistent, not flipped line by line.
            newStart ??= s;
            newEnd = e;
        }

        return new MarkdownEdit(text, newStart ?? start, newEnd);
    }

    private enum Marked
    {
        No,

        /// <summary>The markers sit just outside the selection: <c>**[milk]**</c>.</summary>
        Outside,

        /// <summary>The selection took the markers in: <c>[**milk**]</c>.</summary>
        Inside,
    }

    private readonly record struct Segment(int Start, int End, (int Start, int End) Line);

    /// <summary>
    /// The pieces of the selection to wrap: one per line it touches, starting no
    /// earlier than the line's own text and trimmed of spaces at both ends.
    /// </summary>
    private static List<Segment> SegmentsOf(string content, int start, int end)
    {
        var segments = new List<Segment>();

        foreach (var block in NoteMarkdown.Parse(content))
        {
            if (block.Kind == MarkdownBlockKind.Image)
            {
                continue;
            }

            var lineEnd = block.SourceStart + block.SourceLength;
            var s = Math.Max(start, block.TextStart);
            var e = Math.Min(end, lineEnd);

            while (s < e && char.IsWhiteSpace(content[s]))
            {
                s++;
            }

            while (e > s && char.IsWhiteSpace(content[e - 1]))
            {
                e--;
            }

            if (s < e)
            {
                segments.Add(new Segment(s, e, (block.SourceStart, lineEnd)));
            }
        }

        return segments;
    }

    private static (int Start, int End) LineBounds(string content, int at)
    {
        var lineStart = at == 0 ? 0 : content.LastIndexOf('\n', at - 1) + 1;
        var newline = content.IndexOf('\n', at);
        var lineEnd = newline < 0 ? content.Length : newline;

        if (lineEnd > lineStart && content[lineEnd - 1] == '\r')
        {
            lineEnd--;
        }

        return (lineStart, lineEnd);
    }

    /// <summary>
    /// Whether this piece already has the emphasis, counting the asterisks around
    /// it. Three is bold and italic; two is bold; one is italic.
    /// </summary>
    private static Marked StateOf(string content, Segment segment, Emphasis emphasis)
    {
        var (s, e, line) = segment;

        var before = 0;
        while (s - before > line.Start && content[s - before - 1] == Marker)
        {
            before++;
        }

        var after = 0;
        while (e + after < line.End && content[e + after] == Marker)
        {
            after++;
        }

        if (Has(Math.Min(before, after), emphasis))
        {
            return Marked.Outside;
        }

        var leading = 0;
        while (s + leading < e && content[s + leading] == Marker)
        {
            leading++;
        }

        var trailing = 0;
        while (e - trailing - 1 >= s && content[e - trailing - 1] == Marker)
        {
            trailing++;
        }

        var inside = Math.Min(leading, trailing);
        return 2 * inside < e - s && Has(inside, emphasis) ? Marked.Inside : Marked.No;
    }

    private static bool Has(int asterisks, Emphasis emphasis) => emphasis == Emphasis.Bold
        ? asterisks >= 2
        : asterisks % 2 == 1;
}
