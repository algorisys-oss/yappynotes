using Microsoft.Extensions.Time.Testing;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.ViewModels.Tests;

public class NoteToolbarTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryNoteRepository _repository = new();
    private readonly CountingNoteRepository _counting;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;

    public NoteToolbarTests()
    {
        _counting = new CountingNoteRepository(_repository);
        _notes = new NoteService(_counting, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, TimeSpan.FromMilliseconds(750));
    }

    private async Task<NoteViewModel> OpenAsync(string content)
    {
        var note = await _notes.CreateAsync();
        note.Content = content;
        await _notes.SaveAsync(note);
        var viewModel = new NoteViewModel(note, _notes, _autoSave, new FakeWindowManager());
        viewModel.ShowEdit();
        return viewModel;
    }

    [Fact]
    public async Task SetHeading_AfterEditing_UpdatesContentAndLinesOnce()
    {
        var note = await OpenAsync("milk");
        var before = _counting.Updates;

        var selection = note.SetHeading(0, 4, 2);
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();

        Assert.Equal("## milk", note.Content);
        Assert.Equal((3, 7), selection);
        Assert.Equal(MarkdownBlockKind.Heading, Assert.Single(note.Lines).Block.Kind);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [Fact]
    public async Task SetHeading_WhenSelectionTouchesTwoLines_UpdatesBothInOneSave()
    {
        var note = await OpenAsync("alpha\nbeta");
        var before = _counting.Updates;

        var selection = note.SetHeading(1, 8, 2);
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();

        Assert.Equal("## alpha\n## beta", note.Content);
        Assert.Equal((4, 14), selection);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [Fact]
    public async Task StartLinkAtSelection_WithSelectedText_LeavesTheCaretInTheAddress()
    {
        var note = await OpenAsync("read docs");

        var selection = note.StartLinkAtSelection(5, 9);

        Assert.Equal("read [docs]()", note.Content);
        Assert.Equal((12, 12), selection);
    }

    [Fact]
    public async Task ToggleEmphasis_FromToolbarAndShortcut_UsesTheSameTransformation()
    {
        var note = await OpenAsync("milk");

        var selection = note.ToggleEmphasis(Emphasis.Bold, 0, 4);

        Assert.Equal("**milk**", note.Content);
        Assert.Equal((2, 6), selection);
    }

    [Fact]
    public async Task EditLink_WithAValidDialogResult_UpdatesContentOnce()
    {
        var note = await OpenAsync("read docs");
        note.SetLinkEditor(new FakeLinkEditor(new MarkdownLinks.Draft("docs", "https://example.com")));
        var before = _counting.Updates;

        var selection = await note.EditLinkAsync(5, 9);
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();

        Assert.Equal("read [docs](https://example.com)", note.Content);
        Assert.Equal((6, 10), selection);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("file:///tmp/a")]
    public async Task EditLink_WhenCancelledOrInvalid_DoesNotWrite(string? address)
    {
        var note = await OpenAsync("read docs");
        note.SetLinkEditor(new FakeLinkEditor(address is null ? null : new MarkdownLinks.Draft("docs", address)));
        var before = _counting.Updates;

        var selection = await note.EditLinkAsync(5, 9);
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();

        Assert.Equal("read docs", note.Content);
        Assert.Equal((5, 9), selection);
        Assert.Equal(before, _counting.Updates);
    }

    private sealed class FakeLinkEditor(MarkdownLinks.Draft? answer) : ILinkEditor
    {
        public Task<MarkdownLinks.Draft?> EditAsync(MarkdownLinks.Draft initial) => Task.FromResult(answer);
    }
}
