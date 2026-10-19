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
    public void Parse_ClosedFence_BecomesOneCodeBlock()
    {
        var blocks = NoteMarkdown.Parse("before\n```\n  one\n  two\n```\nafter");

        Assert.Equal(
            [MarkdownBlockKind.Paragraph, MarkdownBlockKind.CodeFence, MarkdownBlockKind.Paragraph],
            blocks.Select(block => block.Kind));
        Assert.Equal("before", TextOf(blocks[0]));
        Assert.Equal("  one\n  two\n", TextOf(blocks[1]));
        Assert.Equal("after", TextOf(blocks[2]));
    }

    [Fact]
    public void Parse_FenceWithLanguage_KeepsTheLabelOutOfCode()
    {
        var block = Single("```python\nprint('oi')\n```");

        Assert.Equal(MarkdownBlockKind.CodeFence, block.Kind);
        Assert.Equal("python", block.Language);
        Assert.Equal("print('oi')\n", Assert.Single(block.Runs).Text);
    }

    [Fact]
    public void Parse_CodeBody_TreatsMarkdownAsLiteral()
    {
        const string code = "# Heading\n- [ ] task\n**bold** and `inline`\nhttps://example.com\n";
        var block = Single("```\n" + code + "```");

        Assert.Equal(MarkdownBlockKind.CodeFence, block.Kind);
        var run = Assert.Single(block.Runs);
        Assert.Equal(code, run.Text);
        Assert.Equal(RunStyle.None, run.Style);
        Assert.Null(run.Link);
        Assert.Null(run.Due);
    }

    [Fact]
    public void Parse_CodeBody_PreservesIndentationAndLineEndings()
    {
        const string content = "before\r\n```bash\r\n  echo hello  \r\n\tprintf done\r\n```\r\nafter";
        var blocks = NoteMarkdown.Parse(content);

        Assert.Equal(3, blocks.Count);
        var code = Assert.Single(blocks[1].Runs).Text;
        Assert.Equal("  echo hello  \r\n\tprintf done\r\n", code);
        Assert.Equal(
            content[content.IndexOf("  echo", StringComparison.Ordinal)..content.LastIndexOf("```", StringComparison.Ordinal)],
            code);
    }

    [Fact]
    public void Parse_UnclosedFence_ConsumesToEndOfNote()
    {
        const string content = "before\n```sql\nSELECT 1;\n- [ ] still code";
        var blocks = NoteMarkdown.Parse(content);

        Assert.Equal([MarkdownBlockKind.Paragraph, MarkdownBlockKind.CodeFence], blocks.Select(block => block.Kind));
        Assert.Equal("sql", blocks[1].Language);
        Assert.Equal("SELECT 1;\n- [ ] still code", Assert.Single(blocks[1].Runs).Text);
        Assert.Equal(content.Length, blocks[1].SourceStart + blocks[1].SourceLength);
    }

    [Fact]
    public void Parse_FenceWithTrailingText_StaysInsideCode()
    {
        var blocks = NoteMarkdown.Parse("```\nfirst\n``` still code\nsecond\n```\nafter");

        Assert.Equal([MarkdownBlockKind.CodeFence, MarkdownBlockKind.Paragraph], blocks.Select(block => block.Kind));
        Assert.Equal("first\n``` still code\nsecond\n", TextOf(blocks[0]));
        Assert.Equal("after", TextOf(blocks[1]));
    }

    [Fact]
    public void Parse_CodeRun_PointsToItsOriginalCharacters()
    {
        const string content = "before\n```python\r\n  x = 1\r\nprint(x)\r\n```\nafter";
        var block = NoteMarkdown.Parse(content)[1];
        var run = Assert.Single(block.Runs);

        Assert.Equal(MarkdownBlockKind.CodeFence, block.Kind);
        Assert.Equal(content.IndexOf("  x = 1", StringComparison.Ordinal), run.SourceStart);
        Assert.Equal(block.TextStart, run.SourceStart);
        Assert.Equal("  x = 1\r\nprint(x)\r\n", run.Text);

        for (var index = 0; index < run.Text.Length; index++)
        {
            Assert.Equal(content[run.SourceStart + index], run.Text[index]);
        }
    }

    [Fact]
    public void Parse_BackticksInsideAParagraph_DoNotStartACodeBlock()
    {
        var blocks = NoteMarkdown.Parse("Use `git status` here\nKeep ```bash``` in this paragraph");

        Assert.Equal([MarkdownBlockKind.Paragraph, MarkdownBlockKind.Paragraph], blocks.Select(block => block.Kind));
        var inlineCode = Assert.Single(blocks[0].Runs, run => run.Style.HasFlag(RunStyle.Code));
        Assert.Equal("git status", inlineCode.Text);
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
