using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

/// <summary>
/// Formatting within a line: bold, italic, code and links.
/// </summary>
public class NoteMarkdownInlineTests
{
    private static IReadOnlyList<MarkdownRun> RunsOf(string line) => Assert.Single(NoteMarkdown.Parse(line)).Runs;

    private static IReadOnlyList<(string Text, RunStyle Style)> Styled(string line)
        => [.. RunsOf(line).Select(run => (run.Text, run.Style))];

    [Theory]
    [InlineData("buy **milk** now")]
    [InlineData("buy __milk__ now")]
    public void Parse_DoubleMarkers_MakeTheirTextBold(string line)
    {
        Assert.Equal(
            [("buy ", RunStyle.None), ("milk", RunStyle.Bold), (" now", RunStyle.None)],
            Styled(line));
    }

    [Theory]
    [InlineData("buy *milk* now")]
    [InlineData("buy _milk_ now")]
    public void Parse_SingleMarkers_MakeTheirTextItalic(string line)
    {
        Assert.Equal(
            [("buy ", RunStyle.None), ("milk", RunStyle.Italic), (" now", RunStyle.None)],
            Styled(line));
    }

    [Fact]
    public void Parse_ItalicInsideBold_IsBoth()
    {
        Assert.Equal(
            [("very ", RunStyle.Bold), ("cold", RunStyle.Bold | RunStyle.Italic), (" milk", RunStyle.Bold)],
            Styled("**very *cold* milk**"));
    }

    [Fact]
    public void Parse_TripleAsterisks_MakeTheirTextBoldAndItalic()
    {
        Assert.Equal([("milk", RunStyle.Bold | RunStyle.Italic)], Styled("***milk***"));
    }

    [Fact]
    public void Parse_Backticks_MakeCode()
    {
        Assert.Equal(
            [("run ", RunStyle.None), ("dotnet test", RunStyle.Code)],
            Styled("run `dotnet test`"));
    }

    /// <summary>What is inside code is shown exactly as typed.</summary>
    [Fact]
    public void Parse_MarkersInsideCode_StayLiteral()
    {
        Assert.Equal([("**not bold**", RunStyle.Code)], Styled("`**not bold**`"));
    }

    /// <summary>
    /// A marker nothing closes is just a character. A note says "5 * 3" far more
    /// often than it means italics.
    /// </summary>
    [Theory]
    [InlineData("5 * 3 = 15")]
    [InlineData("an **unfinished thought")]
    [InlineData("a `stray backtick")]
    [InlineData("2 * 3 * 4")]
    public void Parse_AMarkerWithNoPartner_StaysPlainText(string line)
    {
        Assert.Equal([(line, RunStyle.None)], Styled(line));
    }

    /// <summary>Identifiers pasted into a note are not emphasis.</summary>
    [Fact]
    public void Parse_UnderscoresInsideAWord_StayPlainText()
    {
        Assert.Equal([("rename user_first_name please", RunStyle.None)], Styled("rename user_first_name please"));
    }

    [Fact]
    public void Parse_ALabelledLink_ShowsTheLabelAndCarriesTheAddress()
    {
        var run = Assert.Single(RunsOf("[the docs](https://example.com/docs)"));

        Assert.Equal("the docs", run.Text);
        Assert.Equal(new Uri("https://example.com/docs"), run.Link);
    }

    /// <summary>
    /// The same allow-list as a bare link: a note is text pasted from anywhere,
    /// and a label is exactly how a file: or javascript: address would be
    /// disguised.
    /// </summary>
    [Theory]
    [InlineData("[holiday photos](file:///etc/passwd)")]
    [InlineData("[click](javascript:alert(1))")]
    public void Parse_ALabelledLinkToAScheme_NotAllowed_StaysPlainText(string line)
    {
        var run = Assert.Single(RunsOf(line));

        Assert.Null(run.Link);
        Assert.Equal(line, run.Text);
    }

    [Fact]
    public void Parse_ABareAddress_IsALink()
    {
        var runs = RunsOf("back at https://twitch.tv/rajesh soon");

        var link = Assert.Single(runs, run => run.Link is not null);
        Assert.Equal("https://twitch.tv/rajesh", link.Text);
    }

    /// <summary>
    /// Underscores and asterisks are ordinary in addresses. Read as emphasis they
    /// would cut the link in half and send the click somewhere else.
    /// </summary>
    [Fact]
    public void Parse_AnAddressWithUnderscoresInIt_StaysOneWholeLink()
    {
        var runs = RunsOf("see https://example.com/_drafts_/a*b*c today");

        var link = Assert.Single(runs, run => run.Link is not null);
        Assert.Equal("https://example.com/_drafts_/a*b*c", link.Text);
        Assert.All(runs, run => Assert.Equal(RunStyle.None, run.Style));
    }

    [Fact]
    public void Parse_AnAddressInsideCode_IsNotALink()
    {
        Assert.All(RunsOf("`https://example.com`"), run => Assert.Null(run.Link));
    }

    [Fact]
    public void Parse_FormattingInAHeadingOrATask_IsParsedToo()
    {
        var blocks = NoteMarkdown.Parse("# **Today**\n- [ ] *milk*");

        Assert.Equal(RunStyle.Bold, Assert.Single(blocks[0].Runs).Style);
        Assert.Equal(RunStyle.Italic, Assert.Single(blocks[1].Runs).Style);
    }

    /// <summary>
    /// The property clicking into a note depends on: every run says where its
    /// text sits in the note, so a click on the formatted text can put the caret
    /// at the same character in the Markdown.
    /// </summary>
    [Theory]
    [InlineData("# **Today** and *tomorrow*\n\n- [ ] buy `milk` at https://shop.example\n  - [x] [bread](https://example.com)\r\n__done__ 5 * 3")]
    [InlineData("rename user_first_name, then **very *cold* milk**")]
    [InlineData("an **unfinished thought and a `stray backtick")]
    public void Parse_EveryRun_PointsAtItsOwnTextInTheNote(string content)
    {
        var runs = NoteMarkdown.Parse(content).SelectMany(block => block.Runs).ToList();

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.Equal(run.Text, content.Substring(run.SourceStart, run.Text.Length)));
    }
}
