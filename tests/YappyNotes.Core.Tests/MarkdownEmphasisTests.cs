using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

/// <summary>
/// Ctrl+B and Ctrl+I in the editor: wrap the selection in markers, or take them
/// off again.
/// </summary>
/// <remarks>
/// Content and selection are written together as one string, with <c>«</c> and
/// <c>»</c> marking the selection - or a lone <c>|</c> for a caret - so each case
/// reads as what somebody would see.
/// </remarks>
public class MarkdownEmphasisTests
{
    private static (string Content, int Start, int End) Parse(string marked)
    {
        var caret = marked.IndexOf('|', StringComparison.Ordinal);
        if (caret >= 0)
        {
            return (marked.Remove(caret, 1), caret, caret);
        }

        var start = marked.IndexOf('«', StringComparison.Ordinal);
        var end = marked.IndexOf('»', StringComparison.Ordinal) - 1;
        return (marked.Remove(start, 1).Remove(end, 1), start, end);
    }

    private static string Show(MarkdownEdit edit)
        => edit.SelectionStart == edit.SelectionEnd
            ? edit.Content.Insert(edit.SelectionStart, "|")
            : edit.Content.Insert(edit.SelectionEnd, "»").Insert(edit.SelectionStart, "«");

    private static string Bold(string marked)
    {
        var (content, start, end) = Parse(marked);
        return Show(MarkdownEmphasis.Toggle(content, start, end, Emphasis.Bold));
    }

    private static string Italic(string marked)
    {
        var (content, start, end) = Parse(marked);
        return Show(MarkdownEmphasis.Toggle(content, start, end, Emphasis.Italic));
    }

    [Fact]
    public void ToggleBold_OnASelectedWord_WrapsItAndKeepsItSelected()
    {
        Assert.Equal("buy **«milk»** now", Bold("buy «milk» now"));
    }

    [Fact]
    public void ToggleItalic_OnASelectedWord_WrapsItInOneAsterisk()
    {
        Assert.Equal("buy *«milk»* now", Italic("buy «milk» now"));
    }

    [Fact]
    public void ToggleBold_OnABoldWordSelectedInsideItsMarkers_TakesThemOff()
    {
        Assert.Equal("buy «milk» now", Bold("buy **«milk»** now"));
    }

    [Fact]
    public void ToggleBold_WithTheMarkersSelectedToo_TakesThemOff()
    {
        Assert.Equal("buy «milk» now", Bold("buy «**milk**» now"));
    }

    [Fact]
    public void ToggleItalic_OnABoldWord_MakesItBothRatherThanUndoingTheBold()
    {
        Assert.Equal("***«milk»***", Italic("**«milk»**"));
    }

    [Fact]
    public void ToggleItalic_OnABoldItalicWord_LeavesItBold()
    {
        Assert.Equal("**«milk»**", Italic("***«milk»***"));
    }

    [Fact]
    public void ToggleBold_OnABoldItalicWord_LeavesItItalic()
    {
        Assert.Equal("*«milk»*", Bold("***«milk»***"));
    }

    /// <summary>
    /// Double-clicking a word selects the space after it on some platforms, and
    /// "**milk **" is not bold - a closing marker straight after a space closes
    /// nothing.
    /// </summary>
    [Fact]
    public void ToggleBold_WithSpacesAtTheEdgesOfTheSelection_WrapsOnlyTheText()
    {
        Assert.Equal("buy  **«milk»**  now", Bold("buy « milk » now"));
    }

    [Fact]
    public void ToggleBold_WithNothingSelected_InsertsAPairWithTheCaretBetween()
    {
        Assert.Equal("buy **|**", Bold("buy |"));
    }

    [Theory]
    [InlineData("```cs|\ncode\n```")]
    [InlineData("```\nco|de\n```")]
    [InlineData("```\ncode\n`|``")]
    public void ToggleBold_WithTheCaretInsideCode_ChangesNothing(string marked)
    {
        Assert.Equal(marked, Bold(marked));
    }

    [Fact]
    public void ToggleBold_WithTheCaretInAnEmptyPair_TakesThePairAway()
    {
        Assert.Equal("buy |", Bold("buy **|**"));
    }

    /// <summary>
    /// A selection across a checklist bolds each item's text. Wrapping the whole
    /// selection would put markers around "- [ ]" and across a line break, and the
    /// note would stop being a checklist.
    /// </summary>
    [Fact]
    public void ToggleBold_AcrossAChecklist_WrapsEachItemsTextAndNotItsBox()
    {
        Assert.Equal("- [ ] **«milk**\n- [ ] **bread»**", Bold("«- [ ] milk\n- [ ] bread»"));
    }

    [Fact]
    public void ToggleBold_AcrossCode_FormatsOnlyOrdinaryBlocks()
    {
        var (content, start, end) = Parse("«before\n```\ncode\n```\nafter»");

        var edit = MarkdownEmphasis.Toggle(content, start, end, Emphasis.Bold);

        Assert.Equal("**before**\n```\ncode\n```\n**after**", edit.Content);
    }

    [Fact]
    public void ToggleBold_AcrossLinesThatAreAllBold_TakesTheMarkersOffEach()
    {
        var (content, start, end) = Parse("«**milk**\n**bread**»");

        var edit = MarkdownEmphasis.Toggle(content, start, end, Emphasis.Bold);

        Assert.Equal("milk\nbread", edit.Content);
    }

    /// <summary>Mixed lines are made consistent, the way a word processor does it.</summary>
    [Fact]
    public void ToggleBold_AcrossLinesWhereOnlySomeAreBold_BoldsTheRest()
    {
        var (content, start, end) = Parse("«**milk**\nbread»");

        var edit = MarkdownEmphasis.Toggle(content, start, end, Emphasis.Bold);

        Assert.Equal("**milk**\n**bread**", edit.Content);
    }

    [Fact]
    public void ToggleBold_AcrossABlankLine_LeavesTheBlankLineAlone()
    {
        var (content, start, end) = Parse("«milk\n\nbread»");

        var edit = MarkdownEmphasis.Toggle(content, start, end, Emphasis.Bold);

        Assert.Equal("**milk**\n\n**bread**", edit.Content);
    }

    [Fact]
    public void Toggle_WithTheSelectionMadeBackwards_WorksTheSame()
    {
        var edit = MarkdownEmphasis.Toggle("buy milk now", 8, 4, Emphasis.Bold);

        Assert.Equal("buy **milk** now", edit.Content);
    }

    /// <summary>
    /// The point of the shortcut is what the note then shows, so every result is
    /// checked by parsing it rather than only by its characters.
    /// </summary>
    [Theory]
    [InlineData("buy «milk» now", "milk", RunStyle.Bold)]
    [InlineData("- [ ] «cold milk»", "cold milk", RunStyle.Bold)]
    [InlineData("«# Today »", "Today", RunStyle.Bold)]
    public void ToggleBold_Result_IsDrawnBold(string marked, string text, RunStyle style)
    {
        var (content, start, end) = Parse(marked);

        var edit = MarkdownEmphasis.Toggle(content, start, end, Emphasis.Bold);

        var run = Assert.Single(NoteMarkdown.Parse(edit.Content)[0].Runs, r => r.Text == text);
        Assert.Equal(style, run.Style);
    }
}
