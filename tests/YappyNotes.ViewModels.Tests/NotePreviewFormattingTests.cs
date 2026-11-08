using Microsoft.Extensions.Time.Testing;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.ViewModels.Tests;

public class NotePreviewFormattingTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryNoteRepository _repository = new();
    private readonly CountingNoteRepository _counting;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;

    public NotePreviewFormattingTests()
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
        return new NoteViewModel(note, _notes, _autoSave, new FakeWindowManager());
    }

    private async Task SettleAsync()
    {
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();
    }

    [Fact]
    public async Task TogglePreviewEmphasis_OnSelectedWord_ReparsesAndRestoresItsSelection()
    {
        var note = await OpenAsync("read docs");
        note.SelectPreviewText(0, 5, 0, 9);
        var before = _counting.Updates;

        var changed = note.TogglePreviewEmphasis(Emphasis.Bold);
        await SettleAsync();

        Assert.True(changed);
        Assert.Equal("read **docs**", note.Content);
        Assert.Equal("docs", note.PreviewTextSelection!.VisibleText(note.Content));
        Assert.Equal(RunStyle.Bold, note.Lines[0].Block.Runs.Last().Style);
        Assert.False(note.IsEditing);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [Fact]
    public async Task TogglePreviewEmphasis_OnUnmappableSelection_LeavesTheNoteUntouched()
    {
        var note = await OpenAsync("buy **milk**");
        note.SelectPreviewText(0, 0, 0, 8);
        var before = _counting.Updates;

        var changed = note.TogglePreviewEmphasis(Emphasis.Bold);
        await SettleAsync();

        Assert.False(changed);
        Assert.False(note.CanPreviewBold);
        Assert.Equal("buy **milk**", note.Content);
        Assert.Null(note.PreviewTextSelection);
        Assert.Equal(before, _counting.Updates);
    }

    [Fact]
    public async Task SetPreviewHeading_AcrossTwoLines_KeepsBothLinesVisuallySelected()
    {
        var note = await OpenAsync("one\ntwo");
        note.SelectPreviewText(0, 0, 1, 3);
        var before = _counting.Updates;

        var changed = note.SetPreviewHeading(2);
        await SettleAsync();

        Assert.True(changed);
        Assert.Equal("## one\n## two", note.Content);
        Assert.Equal("one\ntwo", note.PreviewTextSelection!.VisibleText(note.Content));
        Assert.Equal([0, 1], note.PreviewTextSelection.Blocks);
        Assert.False(note.IsEditing);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [Fact]
    public async Task SetPreviewHeading_AfterReverseDrag_KeepsTheSelectionDirection()
    {
        var note = await OpenAsync("one\ntwo");
        note.SelectPreviewText(1, 3, 0, 0);

        var changed = note.SetPreviewHeading(2);

        Assert.True(changed);
        Assert.True(note.PreviewTextSelection!.Anchor > note.PreviewTextSelection.Focus);
        Assert.Equal("one\ntwo", note.PreviewTextSelection.VisibleText(note.Content));
    }

    [Fact]
    public async Task EditPreviewLink_WithValidAddress_KeepsTheLabelSelected()
    {
        var note = await OpenAsync("read docs");
        note.SetLinkEditor(new FakeLinkEditor(new MarkdownLinks.Draft("docs", "https://example.com")));
        note.SelectPreviewText(0, 5, 0, 9);
        var before = _counting.Updates;

        var changed = await note.EditPreviewLinkAsync();
        await SettleAsync();

        Assert.True(changed);
        Assert.Equal("read [docs](https://example.com)", note.Content);
        Assert.Equal("docs", note.PreviewTextSelection!.VisibleText(note.Content));
        Assert.False(note.IsEditing);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("file:///tmp/a")]
    public async Task EditPreviewLink_WhenCancelledOrInvalid_KeepsContentAndSelection(string? address)
    {
        var note = await OpenAsync("read docs");
        note.SetLinkEditor(new FakeLinkEditor(address is null ? null :
            new MarkdownLinks.Draft("docs", address)));
        note.SelectPreviewText(0, 5, 0, 9);
        var selected = note.PreviewTextSelection;
        var before = _counting.Updates;

        var changed = await note.EditPreviewLinkAsync();
        await SettleAsync();

        Assert.False(changed);
        Assert.Equal("read docs", note.Content);
        Assert.Same(selected, note.PreviewTextSelection);
        Assert.Equal(before, _counting.Updates);
    }

    [Fact]
    public async Task EditPreviewLink_WhenSelectionChangesDuringPrompt_DoesNotApplyStaleOffsets()
    {
        var note = await OpenAsync("read docs");
        var editor = new PendingLinkEditor();
        note.SetLinkEditor(editor);
        note.SelectPreviewText(0, 5, 0, 9);
        var pending = note.EditPreviewLinkAsync();
        note.SelectPreviewText(0, 0, 0, 4);
        var before = _counting.Updates;

        editor.Complete(new MarkdownLinks.Draft("docs", "https://example.com"));
        var changed = await pending;
        await SettleAsync();

        Assert.False(changed);
        Assert.Equal("read docs", note.Content);
        Assert.Equal("read", note.PreviewTextSelection!.VisibleText(note.Content));
        Assert.Equal(before, _counting.Updates);
    }

    [Fact]
    public async Task CanPreviewLink_OnPartOfAnExistingLabel_DisablesTheAmbiguousEdit()
    {
        var note = await OpenAsync("[documentation](https://old.test)");
        note.SelectPreviewText(0, 0, 0, 2);

        Assert.False(note.CanPreviewLink);
        Assert.Equal("do", note.PreviewTextSelection!.VisibleText(note.Content));
    }

    private sealed class FakeLinkEditor(MarkdownLinks.Draft? answer) : ILinkEditor
    {
        public Task<MarkdownLinks.Draft?> EditAsync(MarkdownLinks.Draft initial) => Task.FromResult(answer);
    }

    private sealed class PendingLinkEditor : ILinkEditor
    {
        private readonly TaskCompletionSource<MarkdownLinks.Draft?> _answer = new();

        public Task<MarkdownLinks.Draft?> EditAsync(MarkdownLinks.Draft initial) => _answer.Task;

        public void Complete(MarkdownLinks.Draft answer) => _answer.SetResult(answer);
    }
}
