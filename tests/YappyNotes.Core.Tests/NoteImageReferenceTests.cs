using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

public class NoteImageReferenceTests
{
    [Fact]
    public void InsertImage_IntoEmptyContent_ProducesStandaloneReference()
    {
        var file = new Uri("file:///tmp/diagrama.png");

        var edit = NoteImageReference.Insert(string.Empty, 0, file);

        const string reference = "![diagrama](file:///tmp/diagrama.png)";
        Assert.Equal(reference, edit.Content);
        Assert.Equal(reference.Length, edit.SelectionStart);
        Assert.Equal(reference.Length, edit.SelectionEnd);
        Assert.Equal(MarkdownBlockKind.Image, Assert.Single(NoteMarkdown.Parse(edit.Content)).Kind);
    }

    [Fact]
    public void InsertImage_WithEncodedFilename_PreservesUriAndUsesReadableDescription()
    {
        var file = new Uri("file:///tmp/meu%20(diagrama)%20%C3%A9.png");

        var edit = NoteImageReference.Insert(string.Empty, 0, file);

        Assert.Equal("![meu (diagrama) é](file:///tmp/meu%20%28diagrama%29%20%C3%A9.png)", edit.Content);
        var image = Assert.Single(NoteMarkdown.Parse(edit.Content));
        Assert.Equal(MarkdownBlockKind.Image, image.Kind);
        Assert.Equal("meu (diagrama) é", image.ImageDescription);
    }

    [Fact]
    public void InsertImage_WithCustomDescription_EscapesMarkdownDelimiters()
    {
        var file = new Uri("file:///tmp/diagrama.png");

        var edit = NoteImageReference.Insert(string.Empty, 0, file, "plano ] \\ final\r\npróximo");

        Assert.Equal("![plano \\] \\\\ final próximo](file:///tmp/diagrama.png)", edit.Content);
        Assert.Equal("plano ] \\ final próximo", Assert.Single(NoteMarkdown.Parse(edit.Content)).ImageDescription);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void InsertImage_InsideParagraph_PlacesReferenceOnSeparateLine(string newline)
    {
        var content = $"primeira{newline}Olá!  Tchau!";
        var caret = content.IndexOf(" Tchau!", StringComparison.Ordinal);
        var file = new Uri("file:///tmp/diagrama.png");

        var edit = NoteImageReference.Insert(content, caret, file);

        var expected = $"primeira{newline}Olá! {newline}![diagrama](file:///tmp/diagrama.png){newline} Tchau!";
        Assert.Equal(expected, edit.Content);
        Assert.Equal(expected.IndexOf(" Tchau!", StringComparison.Ordinal), edit.SelectionStart);
        Assert.Equal(edit.SelectionStart, edit.SelectionEnd);
        Assert.Equal(MarkdownBlockKind.Image, NoteMarkdown.Parse(edit.Content)[2].Kind);
    }

    [Fact]
    public void InsertImage_AtExistingLineBreak_DoesNotAddABlankLine()
    {
        const string content = "antes\napós";
        var file = new Uri("file:///tmp/diagrama.png");

        var edit = NoteImageReference.Insert(content, content.IndexOf('\n'), file);

        Assert.Equal("antes\n![diagrama](file:///tmp/diagrama.png)\napós", edit.Content);
        Assert.Equal(edit.Content.IndexOf("após", StringComparison.Ordinal), edit.SelectionStart);
        Assert.Equal(edit.SelectionStart, edit.SelectionEnd);
    }

    [Fact]
    public void InsertImage_AtEndOfContent_PutsCaretOnFollowingLine()
    {
        const string content = "Olá!";
        var file = new Uri("file:///tmp/diagrama.png");

        var edit = NoteImageReference.Insert(content, content.Length, file);

        Assert.Equal("Olá!\n![diagrama](file:///tmp/diagrama.png)\n", edit.Content);
        Assert.Equal(edit.Content.Length, edit.SelectionStart);
        Assert.Equal(edit.SelectionStart, edit.SelectionEnd);
    }

    [Theory]
    [InlineData("https://example.com/diagram.png")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("diagram.png")]
    [InlineData("file://localhost/tmp/diagram.png")]
    [InlineData("file://server/share/diagram.png")]
    [InlineData("file:////server/share/diagram.png")]
    [InlineData("file:///tmp/diagram.bmp")]
    [InlineData("file:///tmp/diagram.svg")]
    [InlineData("file:///tmp/diagram.png?version=1")]
    public void InsertImage_NonLocalOrUnsupportedFile_LeavesContentUnchanged(string address)
    {
        const string content = "Olá!  Tchau!";
        const int caret = 6;
        var file = new Uri(address, UriKind.RelativeOrAbsolute);

        var edit = NoteImageReference.Insert(content, caret, file);

        Assert.Equal(content, edit.Content);
        Assert.Equal(caret, edit.SelectionStart);
        Assert.Equal(caret, edit.SelectionEnd);
    }
}
