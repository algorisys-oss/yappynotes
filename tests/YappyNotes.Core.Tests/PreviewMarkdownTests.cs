using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

public class PreviewMarkdownTests
{
    [Theory]
    [InlineData(Emphasis.Bold, "buy **milk**", RunStyle.Bold)]
    [InlineData(Emphasis.Italic, "buy *milk*", RunStyle.Italic)]
    public void ToggleEmphasis_OnPlainPreviewText_MatchesTheEditorTransform(
        Emphasis emphasis, string expected, RunStyle style)
    {
        const string content = "buy milk";

        var edit = PreviewMarkdown.ToggleEmphasis(content, 4, 8, emphasis);

        Assert.NotNull(edit);
        Assert.Equal(MarkdownEmphasis.Toggle(content, 4, 8, emphasis), edit);
        Assert.Equal(expected, edit.Content);
        Assert.Equal(style, NoteMarkdown.Parse(edit.Content)[0].Runs.Last().Style);
    }

    [Theory]
    [InlineData(Emphasis.Bold, "*milk*", RunStyle.Italic)]
    [InlineData(Emphasis.Italic, "**milk**", RunStyle.Bold)]
    public void ToggleEmphasis_OnTripleAsterisks_RemovesOnlyTheRequestedStyle(
        Emphasis emphasis, string expected, RunStyle remainingStyle)
    {
        var edit = PreviewMarkdown.ToggleEmphasis("***milk***", 3, 7, emphasis);

        Assert.NotNull(edit);
        Assert.Equal(expected, edit.Content);
        Assert.Equal(remainingStyle, Assert.Single(NoteMarkdown.Parse(edit.Content)[0].Runs).Style);
    }

    [Theory]
    [InlineData("buy **milk**", 0, 10)]
    [InlineData("see [docs](https://example.com)", 0, 9)]
    [InlineData("use `code`", 5, 9)]
    [InlineData("# first\n## second", 2, 17)]
    public void ToggleEmphasis_AcrossHiddenOrUneditableSyntax_RejectsTheWholeSelection(
        string content, int start, int end)
    {
        Assert.Null(PreviewMarkdown.ToggleEmphasis(content, start, end, Emphasis.Bold));
    }

    [Fact]
    public void ToggleEmphasis_OnUnderscoreItalic_DoesNotCreateASecondItalicLayer()
    {
        Assert.Null(PreviewMarkdown.ToggleEmphasis("_milk_", 1, 5, Emphasis.Italic));
    }

    [Fact]
    public void ToggleEmphasis_OnUnderscoreItalic_CanAddBoldWithoutLosingItalic()
    {
        var edit = PreviewMarkdown.ToggleEmphasis("_milk_", 1, 5, Emphasis.Bold);

        Assert.NotNull(edit);
        Assert.Equal("_**milk**_", edit.Content);
        Assert.Equal(RunStyle.Bold | RunStyle.Italic,
            Assert.Single(NoteMarkdown.Parse(edit.Content)[0].Runs).Style);
    }

    [Theory]
    [InlineData("one\ntwo", "**one**\n**two**")]
    [InlineData("one\r\ntwo", "**one**\r\n**two**")]
    public void ToggleEmphasis_AcrossPlainBlocks_FormatsBothWithoutChangingLineBreaks(
        string content, string expected)
    {
        var edit = PreviewMarkdown.ToggleEmphasis(content, 0, content.Length, Emphasis.Bold);

        Assert.NotNull(edit);
        Assert.Equal(expected, edit.Content);
    }

    [Fact]
    public void ToggleEmphasis_OnLinkLabel_KeepsTheLinkClickable()
    {
        const string content = "see [docs](https://example.com)";

        var edit = PreviewMarkdown.ToggleEmphasis(content, 5, 9, Emphasis.Bold);

        Assert.NotNull(edit);
        Assert.Equal("see [**docs**](https://example.com)", edit.Content);
        Assert.Equal(new Uri("https://example.com"),
            Assert.Single(NoteMarkdown.Parse(edit.Content)[0].Runs, run => run.Text == "docs").Link);
    }

    [Fact]
    public void ToggleEmphasis_WithSpacesAtSelectionEdges_KeepsTheVisibleSelection()
    {
        const string content = "read docs now";

        var edit = PreviewMarkdown.ToggleEmphasis(content, 4, 10, Emphasis.Bold);

        Assert.NotNull(edit);
        Assert.Equal("read **docs** now", edit.Content);
        Assert.Equal((4, 14), (edit.SelectionStart, edit.SelectionEnd));
    }
}
