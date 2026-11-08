namespace YappyNotes.Core;

/// <summary>Creates a labelled Markdown link or changes one already under the cursor.</summary>
public static class MarkdownLinks
{
    public sealed record Draft(string Text, string Address);

    /// <summary>Wrap selected text and place the caret where its address will be typed.</summary>
    public static MarkdownEdit BeginAtSelection(string content, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(content);
        var unchanged = new MarkdownEdit(content, start, end);
        if (start == end || !CanEdit(content, start, end) || TryExisting(content, start, end) is not null)
        {
            return unchanged;
        }

        var label = Escape(content[start..end]);
        var result = content.Remove(start, end - start).Insert(start, $"[{label}]()");
        var addressCaret = start + label.Length + 3;
        return new MarkdownEdit(result, addressCaret, addressCaret);
    }

    public static bool CanEdit(string content, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (start < 0 || end < start || end > content.Length)
        {
            return false;
        }

        if (content.Length == 0)
        {
            return start == 0 && end == 0;
        }

        var block = NoteMarkdown.Parse(content).SingleOrDefault(line =>
            start >= line.TextStart && end <= line.SourceStart + line.SourceLength
            && start <= line.SourceStart + line.SourceLength);
        if (block is null || (block.Kind == MarkdownBlockKind.Blank && start != end))
        {
            return false;
        }

        var existing = TryExisting(content, start, end);
        if (LinksIn(content, block).Any(link => link != existing && start < link.End + 1 && end > link.Open))
        {
            return false;
        }

        return !block.Runs.Any(run => run.Style.HasFlag(RunStyle.Code)
            && start < run.SourceStart + run.Text.Length && end > run.SourceStart);
    }

    public static Draft Initial(string content, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (TryExisting(content, start, end) is { } existing)
        {
            return new Draft(Unescape(content[existing.LabelStart..existing.LabelEnd]),
                content[existing.AddressStart..existing.End]);
        }

        return start >= 0 && end >= start && end <= content.Length
            ? new Draft(content[start..end], string.Empty)
            : new Draft(string.Empty, string.Empty);
    }

    public static MarkdownEdit Apply(string content, int start, int end, string label, string address)
    {
        ArgumentNullException.ThrowIfNull(content);
        var unchanged = new MarkdownEdit(content, start, end);
        if (label is null || address is null || !CanEdit(content, start, end)
            || string.IsNullOrWhiteSpace(label) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || !LinkScanner.IsAllowed(uri) || address.Any(char.IsWhiteSpace))
        {
            return unchanged;
        }

        if (label.Contains('\n') || label.Contains('\r'))
        {
            return unchanged;
        }

        var existing = TryExisting(content, start, end);
        if (existing is not null)
        {
            start = existing.Open;
            end = existing.End + 1;
        }
        else if (start == end && content.Length > start && content[start] is '[' or ']')
        {
            return unchanged;
        }

        var escaped = Escape(label);
        var replacement = $"[{escaped}]({address.Trim()})";
        var result = content.Remove(start, end - start).Insert(start, replacement);
        var parsed = NoteMarkdown.Parse(result).SelectMany(block => block.Runs)
            .Where(run => run.SourceStart >= start + 1 && run.SourceStart < start + 1 + escaped.Length)
            .ToList();
        if (parsed.Count == 0 || parsed.Any(run => run.Link != uri)
            || string.Concat(parsed.Select(run => run.Text)) != label)
        {
            return unchanged;
        }

        return new MarkdownEdit(result, start + 1, start + 1 + escaped.Length);
    }

    private sealed record Link(int Open, int LabelStart, int LabelEnd, int AddressStart, int End);

    private static Link? TryExisting(string content, int start, int end)
        => NoteMarkdown.Parse(content).SelectMany(block => LinksIn(content, block))
            .FirstOrDefault(link => start >= link.LabelStart && end <= link.LabelEnd);

    private static List<Link> LinksIn(string content, MarkdownBlock block)
    {
        var result = new List<Link>();
        var limit = block.SourceStart + block.SourceLength;
        for (var open = block.TextStart; open < limit; open++)
        {
            if (content[open] != '[' || (open > 0 && content[open - 1] == '\\'))
            {
                continue;
            }

            var closeLabel = open + 1;
            while (closeLabel < limit && (content[closeLabel] != ']' || content[closeLabel - 1] == '\\'))
            {
                closeLabel++;
            }

            if (closeLabel + 2 >= limit || content[closeLabel + 1] != '(')
            {
                continue;
            }

            var depth = 1;
            var close = closeLabel + 2;
            for (; close < limit && depth > 0; close++)
            {
                depth += content[close] switch { '(' => 1, ')' => -1, _ => 0 };
            }

            if (depth != 0)
            {
                continue;
            }

            var addressStart = closeLabel + 2;
            var address = content[addressStart..(close - 1)];
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && LinkScanner.IsAllowed(uri)
                && block.Runs.Any(run => run.Link is not null
                    && run.SourceStart >= open + 1 && run.SourceStart < closeLabel))
            {
                result.Add(new Link(open, open + 1, closeLabel, addressStart, close - 1));
                open = close - 1;
            }
        }

        return result;
    }

    private static string Escape(string label) => label.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal);

    private static string Unescape(string label) => label.Replace("\\[", "[", StringComparison.Ordinal)
        .Replace("\\]", "]", StringComparison.Ordinal)
        .Replace("\\\\", "\\", StringComparison.Ordinal);
}
