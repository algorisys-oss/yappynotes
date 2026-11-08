using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

public class MarkdownLinksTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("mailto:me@example.com")]
    public void Apply_ToSelectedText_InsertsAnAllowedLink(string address)
    {
        var edit = MarkdownLinks.Apply("read docs now", 5, 9, "docs", address);

        Assert.Equal($"read [docs]({address}) now", edit.Content);
        Assert.Equal((6, 10), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void Apply_ToAnExistingLink_ChangesItsDestinationWithoutNesting()
    {
        var edit = MarkdownLinks.Apply("read [docs](https://old.test) now", 7, 9, "docs", "https://new.test");

        Assert.Equal("read [docs](https://new.test) now", edit.Content);
    }

    [Theory]
    [InlineData("file:///tmp/a")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a URL")]
    public void Apply_WithAnInvalidAddress_LeavesTheNoteUntouched(string address)
    {
        var edit = MarkdownLinks.Apply("read docs", 5, 9, "docs", address);

        Assert.Equal("read docs", edit.Content);
        Assert.Equal((5, 9), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void Apply_InsideAnExistingLink_KeepsItsSingleLabel()
    {
        var edit = MarkdownLinks.Apply("[docs](https://old.test)", 2, 2, "docs", "https://new.test");

        Assert.Equal("[docs](https://new.test)", edit.Content);
    }

    [Fact]
    public void Apply_AcrossAnExistingLink_LeavesTheNoteUntouched()
    {
        var edit = MarkdownLinks.Apply("read [docs](https://old.test) now", 0, 10, "read docs", "https://new.test");

        Assert.Equal("read [docs](https://old.test) now", edit.Content);
    }

    [Fact]
    public void Apply_WithBracketsInTheLabel_EscapesThem()
    {
        var edit = MarkdownLinks.Apply("item", 0, 4, "[item]", "https://example.com");

        Assert.Equal("[\\[item\\]](https://example.com)", edit.Content);
        Assert.Equal("[item]", string.Concat(NoteMarkdown.Parse(edit.Content)[0].Runs.Select(run => run.Text)));
    }

    [Fact]
    public void Apply_WithASelectionAcrossTwoLines_LeavesTheNoteUntouched()
    {
        var edit = MarkdownLinks.Apply("read\r\ndocs", 2, 8, "read docs", "https://example.com");

        Assert.Equal("read\r\ndocs", edit.Content);
    }

    [Fact]
    public void Apply_WithAParenthesisThatWouldBreakTheLink_LeavesTheNoteUntouched()
    {
        var edit = MarkdownLinks.Apply("docs", 0, 4, "docs", "https://example.com/a)");

        Assert.Equal("docs", edit.Content);
    }

    [Fact]
    public void Apply_AtAnEmptyCaret_InsertsThePromptedTextAndAddress()
    {
        var edit = MarkdownLinks.Apply(string.Empty, 0, 0, "docs", "https://example.com");

        Assert.Equal("[docs](https://example.com)", edit.Content);
        Assert.Equal((1, 5), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void CanEdit_AcrossTwoBlocks_ReturnsFalse()
    {
        Assert.False(MarkdownLinks.CanEdit("read\ndocs", 2, 7));
    }

    [Fact]
    public void BeginAtSelection_WithText_InsertsAddressSlotAtTheCaret()
    {
        var edit = MarkdownLinks.BeginAtSelection("read docs", 5, 9);

        Assert.Equal("read [docs]()", edit.Content);
        Assert.Equal((12, 12), (edit.SelectionStart, edit.SelectionEnd));
    }

    [Fact]
    public void BeginAtSelection_InsideAnExistingLink_DoesNotNestIt()
    {
        var edit = MarkdownLinks.BeginAtSelection("[docs](https://old.test)", 1, 5);

        Assert.Equal("[docs](https://old.test)", edit.Content);
    }
}
