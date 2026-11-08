using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

public class MarkdownHeadingTests
{
    [Theory]
    [InlineData(0, "milk")]
    [InlineData(1, "# milk")]
    [InlineData(2, "## milk")]
    [InlineData(3, "### milk")]
    [InlineData(4, "#### milk")]
    public void SetLevel_OnAParagraph_ReplacesOnlyItsPrefix(int level, string expected)
    {
        var edit = MarkdownHeading.SetLevel("milk\nbread", 2, 2, level);

        Assert.Equal(expected + "\nbread", edit.Content);
        Assert.Equal(expected.Length - 2, edit.SelectionStart);
    }

    [Fact]
    public void SetLevel_OnAHeading_ReplacesItsOldPrefix()
    {
        var edit = MarkdownHeading.SetLevel("## milk", 3, 7, 4);

        Assert.Equal("#### milk", edit.Content);
        Assert.Equal((5, 9), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Theory]
    [InlineData("- [ ] milk")]
    [InlineData("- milk")]
    [InlineData("  ")]
    public void SetLevel_OnANonHeadingBlock_LeavesTheNoteUntouched(string content)
    {
        var edit = MarkdownHeading.SetLevel(content, 2, 2, 2);

        Assert.Equal(content, edit.Content);
        Assert.Equal((2, 2), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void SetLevel_AcrossTwoBlocks_StylesBothTouchedLines()
    {
        var edit = MarkdownHeading.SetLevel("milk\nbread", 2, 7, 2);

        Assert.Equal("## milk\n## bread", edit.Content);
        Assert.Equal((5, 13), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void SetLevel_WithCrLfAndUnicode_KeepsBothIntact()
    {
        var edit = MarkdownHeading.SetLevel("🐄 milk\r\nbread", 5, 5, 3);

        Assert.Equal("### 🐄 milk\r\nbread", edit.Content);
        Assert.Equal((9, 9), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void SetLevel_ToNormal_RemovesOnlyTheHeadingMarker()
    {
        var edit = MarkdownHeading.SetLevel("### milk\nnext", 4, 8, 0);

        Assert.Equal("milk\nnext", edit.Content);
        Assert.Equal((0, 4), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void CanSetLevel_OnATask_ReturnsFalse()
    {
        Assert.False(MarkdownHeading.CanSetLevel("- [ ] milk", 7, 7));
    }

    [Fact]
    public void SetLevel_WhenSelectionReachesTheSecondLine_StylesBothLines()
    {
        var edit = MarkdownHeading.SetLevel("alpha\nbeta", 1, 8, 2);

        Assert.Equal("## alpha\n## beta", edit.Content);
        Assert.Equal((4, 14), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void SetLevel_WhenSelectionEndsAtTheNextLineStart_StylesOnlyTheFirst()
    {
        var edit = MarkdownHeading.SetLevel("alpha\r\nbeta", 0, 7, 2);

        Assert.Equal("## alpha\r\nbeta", edit.Content);
        Assert.Equal((3, 10), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void SetLevel_WhenSelectionIncludesABlankLine_ChangesNothing()
    {
        var edit = MarkdownHeading.SetLevel("alpha\n\nbeta", 1, 8, 2);

        Assert.Equal("alpha\n\nbeta", edit.Content);
        Assert.Equal((1, 8), (edit.SelectionStart, edit.SelectionEnd));
    }
}
