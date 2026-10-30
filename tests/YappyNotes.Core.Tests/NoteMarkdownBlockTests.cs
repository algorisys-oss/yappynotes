using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

/// <summary>
/// How a note's lines become blocks: headings, bullets, checklist items and
/// plain lines.
/// </summary>
public class NoteMarkdownBlockTests
{
    private static MarkdownBlock Single(string content) => Assert.Single(NoteMarkdown.Parse(content));

    private static string TextOf(MarkdownBlock block) => string.Concat(block.Runs.Select(run => run.Text));

    [Fact]
    public void Parse_EmptyContent_HasNoBlocks()
    {
        Assert.Empty(NoteMarkdown.Parse(string.Empty));
    }

    [Fact]
    public void Parse_APlainLine_IsAParagraphShowingThatText()
    {
        var block = Single("milk and bread");

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal("milk and bread", TextOf(block));
    }

    /// <summary>
    /// Markdown proper joins neighbouring lines into one paragraph. On a sticky
    /// note that reads as the app eating your line breaks, so every line stays its
    /// own line.
    /// </summary>
    [Fact]
    public void Parse_TwoNeighbouringLines_StayTwoLines()
    {
        var blocks = NoteMarkdown.Parse("milk\nbread");

        Assert.Equal(["milk", "bread"], blocks.Select(TextOf));
    }

    [Fact]
    public void Parse_StandaloneLocalImage_ProducesImageBlock()
    {
        const string reference = "![diagrama](file:///home/andre/diagram.png)";
        var content = $"antes\n{reference}\ndepois";

        var blocks = NoteMarkdown.Parse(content);

        Assert.Equal(3, blocks.Count);
        Assert.Equal(MarkdownBlockKind.Paragraph, blocks[0].Kind);
        Assert.Equal("antes", TextOf(blocks[0]));
        Assert.Equal(MarkdownBlockKind.Image, blocks[1].Kind);
        Assert.Equal("diagrama", blocks[1].ImageDescription);
        Assert.Equal(new Uri("file:///home/andre/diagram.png"), blocks[1].ImageUri);
        Assert.Equal(content.IndexOf(reference, StringComparison.Ordinal), blocks[1].SourceStart);
        Assert.Equal(reference.Length, blocks[1].SourceLength);
        Assert.Equal(MarkdownBlockKind.Paragraph, blocks[2].Kind);
        Assert.Equal("depois", TextOf(blocks[2]));
    }

    [Fact]
    public void Parse_ImageSyntaxInsideInlineCode_RemainsLiteralText()
    {
        var block = Single("`![diagrama](file:///tmp/diagram.png)`");

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        var code = Assert.Single(block.Runs);
        Assert.Equal("![diagrama](file:///tmp/diagram.png)", code.Text);
        Assert.Equal(RunStyle.Code, code.Style);
    }

    [Theory]
    [InlineData("antes ![diagrama](file:///tmp/diagram.png)")]
    [InlineData("![diagrama](file:///tmp/diagram.png) depois")]
    [InlineData(" ![diagrama](file:///tmp/diagram.png)")]
    [InlineData("![diagrama](file:///tmp/diagram.png) ")]
    public void Parse_ImageMixedWithText_RemainsLiteralText(string line)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(line, TextOf(block));
        Assert.All(block.Runs, run => Assert.Null(run.Link));
    }

    [Theory]
    [InlineData("![diagrama](https://example.com/diagram.png)")]
    [InlineData("![diagrama](data:image/png;base64,AAAA)")]
    [InlineData("![diagrama](diagram.png)")]
    [InlineData("![diagrama](file://server/share/diagram.png)")]
    [InlineData("![diagrama](file:////server/share/diagram.png)")]
    [InlineData("![diagrama](file:///tmp/diagram.bmp)")]
    public void Parse_UnsupportedImageAddress_RemainsLiteralText(string line)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(line, TextOf(block));
        Assert.All(block.Runs, run => Assert.Null(run.Link));
    }

    [Fact]
    public void Parse_LocalImagePathWithEncodedSpacesAndUnicode_KeepsSourceOffsets()
    {
        const string reference = "![café](file:///tmp/caf%C3%A9%20da%20manh%C3%A3.webp)";
        var content = $"📷 antes\r\n{reference}\r\ndepois";

        var image = NoteMarkdown.Parse(content)[1];

        Assert.Equal(MarkdownBlockKind.Image, image.Kind);
        Assert.Equal("café", image.ImageDescription);
        Assert.Equal(new Uri("file:///tmp/caf%C3%A9%20da%20manh%C3%A3.webp"), image.ImageUri);
        Assert.Equal(content.IndexOf(reference, StringComparison.Ordinal), image.SourceStart);
        Assert.Equal(reference.Length, image.SourceLength);
        Assert.Equal(reference, content.Substring(image.SourceStart, image.SourceLength));
    }

    [Fact]
    public void Parse_WindowsDriveImageUri_KeepsSourceOffsetsOnEveryPlatform()
    {
        const string reference = "![captura](file:///C:/Minhas%20Imagens/captura.jpg)";
        var content = $"antes\n{reference}\ndepois";

        var image = NoteMarkdown.Parse(content)[1];

        Assert.Equal(MarkdownBlockKind.Image, image.Kind);
        Assert.Equal(new Uri("file:///C:/Minhas%20Imagens/captura.jpg"), image.ImageUri);
        Assert.Equal(content.IndexOf(reference, StringComparison.Ordinal), image.SourceStart);
        Assert.Equal(reference.Length, image.SourceLength);
    }

    [Theory]
    [InlineData("![diagrama](file:///tmp/diagram.png")]
    [InlineData("![diagrama](file:///tmp/diagram.png))")]
    [InlineData("![diagrama](file:///tmp/diagram(1).png)")]
    [InlineData("![diagrama\\](file:///tmp/diagram.png)")]
    public void Parse_MalformedImageSyntax_RemainsLiteralText(string line)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(line, TextOf(block));
    }

    [Theory]
    [InlineData("![plano \\] final](file:///tmp/diagram.png)", "plano ] final")]
    [InlineData("![cópia \\\\ final](file:///tmp/diagram.png)", "cópia \\ final")]
    public void Parse_EscapedImageDescription_ProducesUnescapedDescription(string line, string description)
    {
        var image = Single(line);

        Assert.Equal(MarkdownBlockKind.Image, image.Kind);
        Assert.Equal(description, image.ImageDescription);
        Assert.Equal(line, line.Substring(image.SourceStart, image.SourceLength));
    }

    [Fact]
    public void Parse_AnEmptyLineBetweenTwo_IsKeptAsABlankBlock()
    {
        var blocks = NoteMarkdown.Parse("milk\n\nbread");

        Assert.Equal(
            [MarkdownBlockKind.Paragraph, MarkdownBlockKind.Blank, MarkdownBlockKind.Paragraph],
            blocks.Select(block => block.Kind));
    }

    [Fact]
    public void Parse_WindowsLineEndings_LeaveNoCarriageReturnInTheText()
    {
        var blocks = NoteMarkdown.Parse("milk\r\nbread");

        Assert.Equal(["milk", "bread"], blocks.Select(TextOf));
    }

    [Theory]
    [InlineData("# Today", 1)]
    [InlineData("## Today", 2)]
    [InlineData("###### Today", 6)]
    public void Parse_HashesAndASpace_IsAHeadingOfThatLevel(string line, int level)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Heading, block.Kind);
        Assert.Equal(level, block.Level);
        Assert.Equal("Today", TextOf(block));
    }

    /// <summary>A hashtag is not a heading, and neither is a seventh level.</summary>
    [Theory]
    [InlineData("#stream")]
    [InlineData("####### too deep")]
    public void Parse_HashesWithoutAHeadingShape_StayAParagraph(string line)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Paragraph, block.Kind);
        Assert.Equal(line, TextOf(block));
    }

    [Theory]
    [InlineData("- milk")]
    [InlineData("* milk")]
    [InlineData("+ milk")]
    public void Parse_AMarkerAndASpace_IsABullet(string line)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Bullet, block.Kind);
        Assert.Equal("milk", TextOf(block));
    }

    [Fact]
    public void Parse_AnIndentedBullet_KeepsItsDepth()
    {
        var blocks = NoteMarkdown.Parse("- groceries\n  - milk\n    - oat");

        Assert.Equal([0, 1, 2], blocks.Select(block => block.Level));
    }

    [Fact]
    public void Parse_AnEmptyCheckbox_IsATaskStillToDo()
    {
        var block = Single("- [ ] milk");

        Assert.Equal(MarkdownBlockKind.Task, block.Kind);
        Assert.False(block.IsDone);
        Assert.Equal("milk", TextOf(block));
    }

    [Theory]
    [InlineData("- [x] milk")]
    [InlineData("- [X] milk")]
    [InlineData("* [x] milk")]
    public void Parse_ATickedCheckbox_IsATaskDone(string line)
    {
        var block = Single(line);

        Assert.Equal(MarkdownBlockKind.Task, block.Kind);
        Assert.True(block.IsDone);
    }

    [Fact]
    public void Parse_ACheckboxWithNothingAfterIt_IsStillATask()
    {
        var block = Single("- [ ]");

        Assert.Equal(MarkdownBlockKind.Task, block.Kind);
        Assert.Empty(TextOf(block));
    }

    /// <summary>
    /// Ticking a box rewrites one character of the note, so the block has to say
    /// which one.
    /// </summary>
    [Fact]
    public void Parse_ATaskOnALaterLine_KnowsWhereItsCheckMarkIs()
    {
        const string content = "# Shopping\n  - [ ] milk";

        var task = NoteMarkdown.Parse(content)[1];

        Assert.Equal(' ', content[task.CheckMarkIndex]);
        Assert.Equal("[ ]", content.Substring(task.CheckMarkIndex - 1, 3));
    }

    [Fact]
    public void Parse_ANumberedLine_StaysAParagraph()
    {
        // Out of scope for now, and "1. " is as readable as plain text as it is
        // formatted.
        Assert.Equal(MarkdownBlockKind.Paragraph, Single("1. milk").Kind);
    }

    /// <summary>
    /// Where a line's own text begins, past any heading, bullet or checkbox marker -
    /// so an edit to the text can leave the marker alone.
    /// </summary>
    [Theory]
    [InlineData("milk", 0)]
    [InlineData("## milk", 3)]
    [InlineData("- milk", 2)]
    [InlineData("  - [x] **milk**", 8)]
    public void Parse_ALine_KnowsWhereItsTextStarts(string line, int textStart)
    {
        Assert.Equal(textStart, Single(line).TextStart);
    }
}
