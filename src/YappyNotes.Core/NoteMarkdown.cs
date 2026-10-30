using System.Text;

namespace YappyNotes.Core;

/// <summary>What a line of a note is.</summary>
public enum MarkdownBlockKind
{
    /// <summary>An empty line, kept so the spacing a reader typed survives.</summary>
    Blank,
    Paragraph,
    Heading,
    Bullet,

    /// <summary>A checklist item, <c>- [ ]</c> or <c>- [x]</c>.</summary>
    Task,

    /// <summary>A standalone reference to a local image.</summary>
    Image,
}

[Flags]
public enum RunStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
}

/// <summary>
/// A stretch of text drawn one way, and where in the note it came from.
/// </summary>
/// <param name="SourceStart">
/// The index in the note's content of <paramref name="Text"/>'s first character.
/// A run's text is always one unbroken piece of the source - the markers sit
/// outside it - so character <c>k</c> on screen is <c>SourceStart + k</c> in the
/// Markdown. That is what lets a click on formatted text put the caret in the
/// right place.
/// </param>
/// <param name="Link">Where it goes, already through the allow-list, or null.</param>
/// <param name="Due">The date this is, if it is a to-do's due date: <c>@2026-09-20</c>.</param>
public sealed record MarkdownRun(string Text, int SourceStart, RunStyle Style = RunStyle.None, Uri? Link = null, DateOnly? Due = null);

/// <summary>One line of a note, parsed.</summary>
/// <param name="SourceStart">Where the line starts in the note's content.</param>
/// <param name="SourceLength">The line's length, without its line break.</param>
/// <param name="Level">A heading's level, or a list item's depth of indentation.</param>
/// <param name="TextStart">
/// Where the line's own text begins in the note, past any heading, bullet or
/// checkbox marker - so an edit to the text can leave the marker alone.
/// </param>
/// <param name="CheckMarkIndex">
/// For a task, the index of the character between its brackets - what
/// <see cref="NoteMarkdown.ToggleTask"/> rewrites. -1 for anything else.
/// </param>
/// <param name="ImageDescription">The image's description, for an image block only.</param>
/// <param name="ImageUri">The local file URI, for an image block only.</param>
public sealed record MarkdownBlock(
    MarkdownBlockKind Kind,
    IReadOnlyList<MarkdownRun> Runs,
    int SourceStart,
    int SourceLength,
    int Level = 0,
    bool IsDone = false,
    int CheckMarkIndex = -1,
    int TextStart = 0,
    string? ImageDescription = null,
    Uri? ImageUri = null);

/// <summary>
/// The small piece of Markdown a note understands: headings, bullets, checklists,
/// bold, italic, code and links.
/// </summary>
/// <remarks>
/// <para>
/// A note stores Markdown as the plain text it is, in <c>Content</c>. Nothing
/// about storage changed for rich text: search still matches the words, export
/// is still the text, and the database is still readable in any SQL browser. This
/// only decides how that text is drawn.
/// </para>
/// <para>
/// Two departures from Markdown proper, both for a sticky note. Every line is its
/// own block - Markdown joins neighbouring lines into one paragraph, which on a
/// note reads as the app eating your line breaks. And nothing is ever an error:
/// anything that does not parse is shown as the characters that were typed.
/// </para>
/// <para>
/// A subset on purpose. Tables, numbered lists and HTML are not coming;
/// a note is a few lines to act on today.
/// </para>
/// </remarks>
public static class NoteMarkdown
{
    public static IReadOnlyList<MarkdownBlock> Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blocks = new List<MarkdownBlock>();
        var lineStart = 0;

        while (lineStart <= content.Length && content.Length > 0)
        {
            var newline = content.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? content.Length : newline;
            var length = lineEnd - lineStart;

            if (length > 0 && content[lineEnd - 1] == '\r')
            {
                length--;
            }

            blocks.Add(ParseLine(content, lineStart, length));

            if (newline < 0)
            {
                break;
            }

            lineStart = newline + 1;
        }

        return blocks;
    }

    /// <summary>
    /// Ticks the checklist item whose mark is at <paramref name="checkMarkIndex"/>,
    /// or clears it if it was ticked. One character of the note changes.
    /// </summary>
    /// <remarks>
    /// The position comes from a parse of the note as it was drawn. It is checked
    /// against a fresh parse rather than trusted, so if the text has changed since,
    /// a stale position flips nothing instead of whatever character now sits there.
    /// </remarks>
    public static string ToggleTask(string content, int checkMarkIndex)
    {
        ArgumentNullException.ThrowIfNull(content);

        var isACheckMark = Parse(content).Any(block =>
            block.Kind == MarkdownBlockKind.Task && block.CheckMarkIndex == checkMarkIndex);

        if (!isACheckMark)
        {
            return content;
        }

        var mark = content[checkMarkIndex] == ' ' ? 'x' : ' ';
        return string.Concat(content.AsSpan(0, checkMarkIndex), [mark], content.AsSpan(checkMarkIndex + 1));
    }

    private static MarkdownBlock ParseLine(string content, int start, int length)
    {
        var line = content.AsSpan(start, length);

        if (line.IsWhiteSpace())
        {
            return new MarkdownBlock(MarkdownBlockKind.Blank, [], start, length, TextStart: start);
        }

        if (TryParseImage(line, out var description, out var imageUri))
        {
            return new MarkdownBlock(MarkdownBlockKind.Image, [], start, length,
                TextStart: start, ImageDescription: description, ImageUri: imageUri);
        }

        var hashes = CountLeading(line, '#');
        if (hashes is >= 1 and <= 6 && hashes < line.Length && line[hashes] == ' ')
        {
            return Block(MarkdownBlockKind.Heading, start, length, textStart: start + hashes + 1, content, level: hashes);
        }

        var indent = CountLeading(line, ' ');
        var rest = line[indent..];
        if (rest.Length >= 2 && rest[0] is '-' or '*' or '+' && rest[1] == ' ')
        {
            var depth = indent / 2;
            var afterMarker = rest[2..];

            if (afterMarker.Length >= 3 && afterMarker[0] == '[' && afterMarker[1] is ' ' or 'x' or 'X' && afterMarker[2] == ']'
                && (afterMarker.Length == 3 || afterMarker[3] == ' '))
            {
                var checkMark = start + indent + 3;
                var textStart = Math.Min(checkMark + 3, start + length);
                return Block(MarkdownBlockKind.Task, start, length, textStart, content, depth)
                    with
                { IsDone = content[checkMark] != ' ', CheckMarkIndex = checkMark };
            }

            return Block(MarkdownBlockKind.Bullet, start, length, textStart: start + indent + 2, content, depth);
        }

        return Block(MarkdownBlockKind.Paragraph, start, length, textStart: start, content);
    }

    private static bool TryParseImage(ReadOnlySpan<char> line, out string? description, out Uri? imageUri)
    {
        description = null;
        imageUri = null;

        if (!line.StartsWith("![", StringComparison.Ordinal) || line[^1] != ')')
        {
            return false;
        }

        var decodedDescription = new StringBuilder();
        var descriptionEnd = -1;
        for (var i = 2; i < line.Length; i++)
        {
            if (line[i] == '\\')
            {
                if (i + 1 >= line.Length || line[i + 1] is not ('\\' or ']'))
                {
                    return false;
                }

                decodedDescription.Append(line[++i]);
            }
            else if (line[i] == ']')
            {
                if (i + 1 >= line.Length || line[i + 1] != '(')
                {
                    return false;
                }

                descriptionEnd = i;
                break;
            }
            else
            {
                decodedDescription.Append(line[i]);
            }
        }

        if (descriptionEnd < 0)
        {
            return false;
        }

        var destination = line[(descriptionEnd + 2)..^1];
        if (!destination.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            || destination.Length <= "file:///".Length
            || destination["file:///".Length] == '/'
            || destination.IndexOfAny("()?#\\".AsSpan()) >= 0
            || destination.IndexOfAny(' ', '\t') >= 0
            || !Uri.TryCreate(destination.ToString(), UriKind.Absolute, out var uri)
            || !uri.IsFile
            || uri.Host.Length != 0
            || uri.AbsolutePath.StartsWith("//", StringComparison.Ordinal)
            || !IsSupportedImageExtension(Path.GetExtension(uri.AbsolutePath)))
        {
            return false;
        }

        description = decodedDescription.ToString();
        imageUri = uri;
        return true;
    }

    private static bool IsSupportedImageExtension(string extension)
        => extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

    private static MarkdownBlock Block(
        MarkdownBlockKind kind, int lineStart, int lineLength, int textStart, string content, int level = 0)
    {
        var runs = new List<MarkdownRun>();
        ParseInlines(content, textStart, lineStart + lineLength, RunStyle.None, runs, dates: kind == MarkdownBlockKind.Task);

        return new MarkdownBlock(kind, runs, lineStart, lineLength, level, TextStart: textStart);
    }

    /// <summary>
    /// Turns <c>content[start..end)</c> into runs, each pointing at its own text.
    /// </summary>
    /// <remarks>
    /// Deliberately forgiving: a marker that nothing closes, or that is not shaped
    /// like emphasis, is only a character. The text a reader typed is always all
    /// there on screen - formatting can hide a marker, never a word.
    /// </remarks>
    private static void ParseInlines(string content, int start, int end, RunStyle style, List<MarkdownRun> runs, bool dates)
    {
        // Bare addresses are found first and treated as whole, so an underscore or
        // an asterisk inside a URL is never read as emphasis.
        var addresses = LinkScanner.Scan(content[start..end])
            .ToDictionary(link => start + link.Start);

        var plainStart = start;
        var i = start;

        while (i < end)
        {
            var c = content[i];

            // An unsupported image reference stays literal, including an http:
            // destination that could otherwise become a labelled or bare link.
            if (c == '!' && i + 1 < end && content[i + 1] == '[')
            {
                var imageLabelEnd = content.AsSpan(i + 2, end - i - 2).IndexOf("](", StringComparison.Ordinal);
                if (imageLabelEnd >= 0)
                {
                    var destinationStart = i + 2 + imageLabelEnd + 2;
                    var imageEnd = content.IndexOf(')', destinationStart, end - destinationStart);
                    i = imageEnd < 0 ? end : imageEnd + 1;
                    continue;
                }
            }

            if (addresses.TryGetValue(i, out var address))
            {
                Plain(content, plainStart, i, style, runs);
                runs.Add(new MarkdownRun(content.Substring(i, address.Length), i, style, address.Uri));
                i += address.Length;
                plainStart = i;
                continue;
            }

            if (c == '@' && dates && TryDueDate(content, i, end, out var due))
            {
                Plain(content, plainStart, i, style, runs);
                runs.Add(new MarkdownRun(content.Substring(i, DueDateLength), i, style, Due: due));
                i += DueDateLength;
                plainStart = i;
                continue;
            }

            if (c == '`' && TryCode(content, i, end, out var codeEnd))
            {
                Plain(content, plainStart, i, style, runs);
                runs.Add(new MarkdownRun(content[(i + 1)..codeEnd], i + 1, style | RunStyle.Code));
                i = codeEnd + 1;
                plainStart = i;
                continue;
            }

            if (c == '[' && TryLabelledLink(content, i, end, out var labelEnd, out var linkEnd, out var uri))
            {
                Plain(content, plainStart, i, style, runs);
                var label = new List<MarkdownRun>();
                ParseInlines(content, i + 1, labelEnd, style, label, dates);
                runs.AddRange(label.Select(run => run with { Link = uri }));
                i = linkEnd + 1;
                plainStart = i;
                continue;
            }

            if (c is '*' or '_' && TryEmphasis(content, i, start, end, out var width, out var closer))
            {
                Plain(content, plainStart, i, style, runs);
                var emphasis = width == 2 ? RunStyle.Bold : RunStyle.Italic;
                ParseInlines(content, i + width, closer, style | emphasis, runs, dates);
                i = closer + width;
                plainStart = i;
                continue;
            }

            i++;
        }

        Plain(content, plainStart, end, style, runs);
    }

    private static void Plain(string content, int start, int end, RunStyle style, List<MarkdownRun> runs)
    {
        if (end > start)
        {
            runs.Add(new MarkdownRun(content[start..end], start, style));
        }
    }

    /// <summary>"@2026-09-20": the at sign and an ISO date.</summary>
    private const int DueDateLength = 11;

    /// <summary>
    /// A to-do's due date, <c>@2026-09-20</c>. Only a whole word - not the tail of an
    /// address like me@2026-09-20 - and only a date that exists.
    /// </summary>
    /// <remarks>
    /// Written as an absolute date on purpose. A note is text that outlives the day
    /// it was written, so "@tomorrow" stored as typed would be wrong by tomorrow;
    /// <see cref="TodoDue.ResolveShorthand"/> turns it into a date as it is added.
    /// </remarks>
    private static bool TryDueDate(string content, int at, int end, out DateOnly due)
    {
        due = default;

        if (at + DueDateLength > end
            || (at > 0 && !char.IsWhiteSpace(content[at - 1]))
            || (at + DueDateLength < end && (char.IsLetterOrDigit(content[at + DueDateLength]) || content[at + DueDateLength] == '-')))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            content.AsSpan(at + 1, DueDateLength - 1),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out due);
    }

    private static bool TryCode(string content, int open, int end, out int close)
    {
        close = content.IndexOf('`', open + 1, end - open - 1);
        return close > open + 1;
    }

    /// <summary>
    /// <c>[label](address)</c>, where the address passes the same allow-list as a
    /// bare link. Anything else is left as the text it is.
    /// </summary>
    private static bool TryLabelledLink(
        string content, int open, int end, out int labelEnd, out int linkEnd, out Uri? uri)
    {
        uri = null;
        linkEnd = -1;
        labelEnd = content.IndexOf(']', open + 1, end - open - 1);

        if (labelEnd <= open + 1 || labelEnd + 1 >= end || content[labelEnd + 1] != '(')
        {
            return false;
        }

        // Balanced, so an address with brackets of its own - a Wikipedia page -
        // keeps them.
        var depth = 0;
        for (var j = labelEnd + 1; j < end; j++)
        {
            depth += content[j] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                linkEnd = j;
                break;
            }
        }

        if (linkEnd < 0)
        {
            return false;
        }

        var address = content[(labelEnd + 2)..linkEnd];
        return Uri.TryCreate(address, UriKind.Absolute, out uri) && LinkScanner.IsAllowed(uri);
    }

    /// <summary>
    /// Emphasis with a partner: <c>**</c> or <c>__</c> for bold, <c>*</c> or <c>_</c>
    /// for italic.
    /// </summary>
    /// <remarks>
    /// An opener has text straight after it and a closer has text straight before
    /// it, which is what keeps "5 * 3" plain. An underscore also has to sit
    /// outside a word at both ends, which is what keeps user_first_name plain. A
    /// single marker never pairs with half of a double one.
    /// </remarks>
    private static bool TryEmphasis(string content, int open, int lineStart, int end, out int width, out int close)
    {
        var marker = content[open];
        width = open + 1 < end && content[open + 1] == marker ? 2 : 1;
        close = -1;

        var textStart = open + width;
        if (textStart >= end || char.IsWhiteSpace(content[textStart]))
        {
            return false;
        }

        if (marker == '_' && open > lineStart && char.IsLetterOrDigit(content[open - 1]))
        {
            return false;
        }

        for (var j = textStart + 1; j + width <= end; j++)
        {
            if (!IsRun(content, j, width, marker, end) || char.IsWhiteSpace(content[j - 1]))
            {
                continue;
            }

            if (marker == '_' && j + width < end && char.IsLetterOrDigit(content[j + width]))
            {
                continue;
            }

            close = j;
            return true;
        }

        return false;
    }

    /// <summary>Exactly <paramref name="width"/> markers at this position, no more and no fewer.</summary>
    private static bool IsRun(string content, int at, int width, char marker, int end)
    {
        for (var k = 0; k < width; k++)
        {
            if (content[at + k] != marker)
            {
                return false;
            }
        }

        var before = at > 0 && content[at - 1] == marker;
        var after = at + width < end && content[at + width] == marker;
        return !before && !after;
    }

    private static int CountLeading(ReadOnlySpan<char> line, char character)
    {
        var count = 0;
        while (count < line.Length && line[count] == character)
        {
            count++;
        }

        return count;
    }
}
