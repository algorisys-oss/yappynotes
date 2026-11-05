using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.ViewModels.Tests;

public class PreviewSelectionTests
{
    [Theory]
    [InlineData("buy milk", 4, 8, 4, 8)]
    [InlineData("buy **milk**", 4, 8, 6, 10)]
    [InlineData("## milk", 0, 4, 3, 7)]
    [InlineData("see [docs](https://example.com)", 4, 8, 5, 9)]
    public void FromRendered_VisibleText_UsesRunSourceOffsets(string content,
        int anchor, int focus, int sourceAnchor, int sourceFocus)
    {
        var lines = NoteMarkdown.Parse(content).Select(block => NoteLine.For(block)).ToArray();

        var selection = PreviewSelection.FromRendered(content, lines, 0, anchor, 0, focus);

        Assert.NotNull(selection);
        Assert.Equal((sourceAnchor, sourceFocus), (selection.Anchor, selection.Focus));
    }

    [Fact]
    public void FromRendered_ReversedAcrossVisualLines_PreservesDirection()
    {
        const string content = "first\nsecond";
        var lines = NoteMarkdown.Parse(content).Select(block => NoteLine.For(block)).ToArray();

        var selection = PreviewSelection.FromRendered(content, lines, 1, 4, 0, 2);

        Assert.NotNull(selection);
        Assert.Equal((10, 2), (selection.Anchor, selection.Focus));
        Assert.Equal([0, 1], selection.Blocks);
    }

    [Fact]
    public void FromRendered_OneWrappedLine_MapsBothVisualRowsIntoItsRun()
    {
        const string content = "alpha beta gamma delta";
        var lines = NoteMarkdown.Parse(content).Select(block => NoteLine.For(block)).ToArray();

        var selection = PreviewSelection.FromRendered(content, lines, 0, 3, 0, 16);

        Assert.NotNull(selection);
        Assert.Equal((3, 16), (selection.Start, selection.End));
    }

    [Theory]
    [InlineData("buy **milk**", 2, 8)]
    [InlineData("see [docs](https://example.com)", 2, 8)]
    [InlineData("use `code` now", 4, 8)]
    [InlineData("- [ ] milk", 0, 7)]
    [InlineData("- milk", 0, 5)]
    public void FromRendered_AmbiguousOrUneditableText_RejectsWholeRange(string content, int start, int end)
    {
        var lines = NoteMarkdown.Parse(content).Select(block => NoteLine.For(block)).ToArray();

        var selection = PreviewSelection.FromRendered(content, lines, 0, start, 0, end);

        Assert.Null(selection);
    }

    [Fact]
    public void TryCreate_VisibleWord_ReturnsSourceAnchorsAndBlock()
    {
        const string content = "buy **cold** milk";
        var start = content.IndexOf("cold", StringComparison.Ordinal);

        var selection = PreviewSelection.TryCreate(content, start, start + 4);

        Assert.NotNull(selection);
        Assert.Equal((start, start + 4), (selection.Anchor, selection.Focus));
        Assert.Equal((start, start + 4), (selection.Start, selection.End));
        Assert.Equal([0], selection.Blocks);
    }

    [Fact]
    public void TryCreate_ReversedDrag_PreservesDirectionAndOrdersBounds()
    {
        const string content = "buy **cold** milk";
        var start = content.IndexOf("cold", StringComparison.Ordinal);

        var selection = PreviewSelection.TryCreate(content, start + 4, start);

        Assert.NotNull(selection);
        Assert.Equal((start + 4, start), (selection.Anchor, selection.Focus));
        Assert.Equal((start, start + 4), (selection.Start, selection.End));
        Assert.Equal([0], selection.Blocks);
    }

    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("one\r\ntwo")]
    public void TryCreate_AdjacentTextBlocks_IncludesBothSourceBlocks(string content)
    {
        var start = content.IndexOf("ne", StringComparison.Ordinal);
        var end = content.IndexOf("wo", StringComparison.Ordinal) + 2;

        var selection = PreviewSelection.TryCreate(content, start, end);

        Assert.NotNull(selection);
        Assert.Equal([0, 1], selection.Blocks);
        Assert.Equal((start, end), (selection.Start, selection.End));
    }

    [Fact]
    public void TryCreate_HeadingText_ExcludesItsHiddenPrefix()
    {
        const string content = "# Today";

        var text = PreviewSelection.TryCreate(content, 2, content.Length);
        var marker = PreviewSelection.TryCreate(content, 0, content.Length);

        Assert.NotNull(text);
        Assert.Equal([0], text.Blocks);
        Assert.Null(marker);
    }

    [Theory]
    [InlineData("buy **cold** milk", "buy ", "cold")]
    [InlineData("buy *cold* milk", "buy ", "cold")]
    [InlineData("see [docs](https://example.com)", "see ", "docs")]
    public void TryCreate_RangeAcrossHiddenSyntax_RejectsWholeSelection(string content, string startText, string endText)
    {
        var start = content.IndexOf(startText, StringComparison.Ordinal);
        var end = content.IndexOf(endText, StringComparison.Ordinal) + endText.Length;

        var selection = PreviewSelection.TryCreate(content, start, end);

        Assert.Null(selection);
    }

    [Fact]
    public void TryCreate_LinkLabel_AcceptsOnlyVisibleLabel()
    {
        const string content = "see [docs](https://example.com)";
        var start = content.IndexOf("docs", StringComparison.Ordinal);

        var label = PreviewSelection.TryCreate(content, start, start + 4);
        var destination = PreviewSelection.TryCreate(content, start, content.Length);

        Assert.NotNull(label);
        Assert.Equal([0], label.Blocks);
        Assert.Null(destination);
    }

    [Fact]
    public void TryCreate_InlineCode_RejectsItsVisibleCharacters()
    {
        const string content = "use `code` now";
        var start = content.IndexOf("code", StringComparison.Ordinal);

        var selection = PreviewSelection.TryCreate(content, start, start + 4);

        Assert.Null(selection);
    }

    [Theory]
    [InlineData("- [ ] milk", 6)]
    [InlineData("- milk", 2)]
    public void TryCreate_ListMarker_RejectsWholeSelection(string content, int textStart)
    {
        var text = PreviewSelection.TryCreate(content, textStart, content.Length);
        var marker = PreviewSelection.TryCreate(content, 0, content.Length);

        Assert.NotNull(text);
        Assert.Null(marker);
    }

    [Fact]
    public void TryCreate_BlankBlockBetweenText_RejectsWholeSelection()
    {
        const string content = "one\n\nthree";

        var selection = PreviewSelection.TryCreate(content, 0, content.Length);

        Assert.Null(selection);
    }

    [Fact]
    public void TryCreate_EmojiSourceRange_RejectsSplitSurrogatePair()
    {
        const string content = "a😀b";

        var wholeEmoji = PreviewSelection.TryCreate(content, 1, 3);
        var halfEmoji = PreviewSelection.TryCreate(content, 2, 3);

        Assert.NotNull(wholeEmoji);
        Assert.Null(halfEmoji);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 4)]
    [InlineData(2, 2)]
    public void TryCreate_InvalidOrEmptySourceRange_ReturnsNull(int anchor, int focus)
    {
        var selection = PreviewSelection.TryCreate("one", anchor, focus);

        Assert.Null(selection);
    }
}
