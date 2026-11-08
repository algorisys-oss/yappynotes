using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.ViewModels.Tests;

/// <summary>
/// A note shows formatted text until it is clicked, and the click decides what
/// happens: tick a box, open a link, or start editing where it landed.
/// </summary>
public class NoteFormattingTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryNoteRepository _repository = new();
    private readonly CountingNoteRepository _counting;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private readonly FakeLinkLauncher _links = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public NoteFormattingTests()
    {
        _counting = new CountingNoteRepository(_repository);
        _notes = new NoteService(_counting, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, Debounce);
    }

    private async Task<NoteViewModel> NoteSayingAsync(string content)
    {
        var note = await _notes.CreateAsync(Token);
        note.Content = content;
        await _notes.SaveAsync(note, Token);
        return new NoteViewModel(note, _notes, _autoSave, new FakeWindowManager(), links: _links);
    }

    private async Task SettleAsync()
    {
        _clock.Advance(Debounce);
        await _autoSave.WhenIdleAsync();
    }

    [Fact]
    public async Task Lines_ForAChecklist_DrawABoxForEachItem()
    {
        var note = await NoteSayingAsync("- [ ] milk\n- [x] bread");

        Assert.Equal(["☐ ", "☑ "], note.Lines.Select(line => line.Marker));
    }

    [Fact]
    public async Task Lines_ForABullet_DrawADot()
    {
        var note = await NoteSayingAsync("- milk");

        Assert.Equal("• ", Assert.Single(note.Lines).Marker);
    }

    [Fact]
    public async Task Lines_WhenTheContentChanges_AreRedrawn()
    {
        var note = await NoteSayingAsync("milk");
        var changed = new List<string?>();
        ((INotifyPropertyChanged)note).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        note.Content = "- [ ] milk";

        Assert.Contains(nameof(NoteViewModel.Lines), changed);
        Assert.Equal(MarkdownBlockKind.Task, Assert.Single(note.Lines).Block.Kind);
    }

    [Fact]
    public async Task ShowsEditor_ForANoteWithText_IsFalseUntilClicked()
    {
        var note = await NoteSayingAsync("milk");

        Assert.False(note.ShowsEditor);
        Assert.False(note.IsEditing);
    }

    /// <summary>
    /// An empty note has nothing to format, and a new note should be ready to type
    /// into without a click.
    /// </summary>
    [Fact]
    public async Task ShowsEditor_ForAnEmptyNote_IsTrue()
    {
        var note = await NoteSayingAsync(string.Empty);

        Assert.True(note.ShowsEditor);
        Assert.False(note.IsEditing);
    }

    [Fact]
    public async Task ShowEdit_ThenShowPreview_SwitchesWithoutSaving()
    {
        var note = await NoteSayingAsync("milk");
        var writesBefore = _counting.Updates;

        note.ShowEdit();
        Assert.True(note.IsEditing);
        Assert.True(note.ShowsEditor);
        Assert.Equal(4, note.EditCaret);

        note.ShowPreview();
        await SettleAsync();

        Assert.False(note.IsEditing);
        Assert.False(note.ShowsEditor);
        Assert.Equal("milk", note.Content);
        Assert.Equal(writesBefore, _counting.Updates);
    }

    [Fact]
    public async Task ShowEdit_AfterPreview_RestoresTheLastCaret()
    {
        var note = await NoteSayingAsync("milk");
        note.ShowEdit();
        note.RememberEditCaret(2);
        note.ShowPreview();

        note.ShowEdit();

        Assert.True(note.ShowsEditor);
        Assert.Equal(2, note.EditCaret);
    }

    [Fact]
    public async Task ShowPreview_OnAnEmptyNote_KeepsTheEditorReady()
    {
        var note = await NoteSayingAsync(string.Empty);

        note.ShowPreview();

        Assert.True(note.ShowsEditor);
        Assert.False(note.IsEditing);
        Assert.False(note.HasPreviewContent);
    }

    [Fact]
    public async Task PressAsync_OnATasksBox_TicksItAndSavesIt()
    {
        var note = await NoteSayingAsync("- [ ] milk");

        await note.PressAsync(note.Lines[0], renderedIndex: 0);
        await SettleAsync();

        Assert.Equal("- [x] milk", note.Content);
        Assert.Equal("- [x] milk", (await _repository.GetByIdAsync(note.Id, Token))!.Content);
    }

    /// <summary>
    /// Ticking is something done to a formatted note. Dropping into the editor as
    /// well would hide the very box that was just ticked.
    /// </summary>
    [Fact]
    public async Task PressAsync_OnATasksBox_DoesNotStartEditing()
    {
        var note = await NoteSayingAsync("- [ ] milk");

        await note.PressAsync(note.Lines[0], renderedIndex: 0);

        Assert.False(note.IsEditing);
    }

    [Fact]
    public async Task PressAsync_OnALink_OpensItWithoutEditing()
    {
        var note = await NoteSayingAsync("see [the docs](https://example.com/docs)");

        await note.PressAsync(note.Lines[0], renderedIndex: "see th".Length);

        Assert.Equal([new Uri("https://example.com/docs")], _links.Opened);
        Assert.False(note.IsEditing);
    }

    [Fact]
    public async Task PressAsync_OnFormattedText_KeepsPreviewAndClearsTheSelection()
    {
        const string content = "milk bread";
        var note = await NoteSayingAsync(content);
        note.SelectPreviewText(0, 0, 0, 4);

        await note.PressAsync(note.Lines[0], renderedIndex: 6);

        Assert.False(note.IsEditing);
        Assert.Null(note.PreviewTextSelection);
        Assert.Equal(content, note.Content);
    }

    [Fact]
    public async Task PressAsync_OnABulletsDot_KeepsPreview()
    {
        const string content = "- milk";
        var note = await NoteSayingAsync(content);

        await note.PressAsync(note.Lines[0], renderedIndex: 0);

        Assert.False(note.ShowsEditor);
        Assert.Equal(content, note.Content);
    }

    [Fact]
    public async Task PressAsync_PastTheEndOfALine_KeepsPreview()
    {
        const string content = "milk\nbread";
        var note = await NoteSayingAsync(content);

        await note.PressAsync(note.Lines[0], renderedIndex: 40);

        Assert.False(note.ShowsEditor);
        Assert.Equal(content, note.Content);
    }

    [Fact]
    public async Task BeginEditingAtEnd_PutsTheCaretAfterTheLastCharacter()
    {
        var note = await NoteSayingAsync("milk\nbread");

        note.BeginEditingAtEnd();

        Assert.True(note.ShowsEditor);
        Assert.Equal("milk\nbread".Length, note.EditCaret);
    }

    [Fact]
    public async Task EndEditing_OnANoteWithText_ShowsItFormattedAgain()
    {
        var note = await NoteSayingAsync("milk");
        note.BeginEditingAtEnd();

        note.EndEditing();

        Assert.False(note.ShowsEditor);
    }

    [Fact]
    public async Task EndEditing_WhenEverythingWasDeleted_KeepsTheEditor()
    {
        var note = await NoteSayingAsync("milk");
        note.BeginEditingAtEnd();
        note.Content = string.Empty;

        note.EndEditing();

        Assert.True(note.ShowsEditor);
    }

    /// <summary>
    /// Switching between formatted and editing is how a note is looked at, not a
    /// change to it: clicking in and out of a note all day must cost no disk.
    /// </summary>
    [Fact]
    public async Task EditingInAndOut_WithoutTyping_WritesNothing()
    {
        var note = await NoteSayingAsync("milk");
        var writesBefore = _counting.Updates;

        note.ShowEdit();
        note.EndEditing();
        await SettleAsync();

        Assert.Equal(writesBefore, _counting.Updates);
    }

    /// <summary>
    /// Formatted, a link is clickable where it is written, so offering it again
    /// under the note is the same link twice.
    /// </summary>
    [Fact]
    public async Task ShowsLinkBar_WhileFormatted_IsFalse()
    {
        var note = await NoteSayingAsync("see https://example.com");

        Assert.False(note.ShowsLinkBar);
    }

    /// <summary>The editor draws plain text, so while typing the bar is the way to a link.</summary>
    [Fact]
    public async Task ShowsLinkBar_WhileEditingANoteWithLinks_IsTrue()
    {
        var note = await NoteSayingAsync("see https://example.com");

        note.BeginEditingAtEnd();

        Assert.True(note.ShowsLinkBar);
    }

    [Fact]
    public async Task ShowsLinkBar_WhileEditingANoteWithoutLinks_IsFalse()
    {
        var note = await NoteSayingAsync("milk");

        note.BeginEditingAtEnd();

        Assert.False(note.ShowsLinkBar);
    }

    [Fact]
    public async Task ToggleEmphasis_WhileEditing_ChangesTheMarkdownAndSaysWhatToSelect()
    {
        var note = await NoteSayingAsync("buy milk now");
        note.BeginEditingAtEnd();

        var selection = note.ToggleEmphasis(Emphasis.Bold, 4, 8);
        await SettleAsync();

        Assert.Equal("buy **milk** now", note.Content);
        Assert.Equal((6, 10), selection);
        Assert.Equal("buy **milk** now", (await _repository.GetByIdAsync(note.Id, Token))!.Content);
    }

    /// <summary>
    /// A shortcut acts on a selection, and a formatted note has none: nothing is
    /// selected in it that the reader can see.
    /// </summary>
    [Fact]
    public async Task ToggleEmphasis_WhileFormatted_ChangesNothing()
    {
        var note = await NoteSayingAsync("buy milk now");

        var selection = note.ToggleEmphasis(Emphasis.Bold, 4, 8);

        Assert.Equal("buy milk now", note.Content);
        Assert.Equal((4, 8), selection);
    }

    /// <summary>
    /// Reported from real use: in a new note, the first letter typed swapped the
    /// editor for the formatted note and took the keyboard with it. An empty note
    /// shows the editor because there is nothing to format, not because it is being
    /// edited - so the first letter made it formatted. Typing into it is editing.
    /// </summary>
    [Fact]
    public async Task Content_TypedIntoAnEmptyNote_KeepsTheEditor()
    {
        var note = await NoteSayingAsync(string.Empty);

        note.Content = "S";

        Assert.True(note.ShowsEditor);
        Assert.True(note.IsEditing);
    }

    /// <summary>A to-do added to an empty note is not typing into its editor.</summary>
    [Fact]
    public async Task Content_AddedAsATodoToAnEmptyNote_StaysFormatted()
    {
        var note = await NoteSayingAsync(string.Empty);
        note.StartTodoListCommand.Execute(null);
        note.NewTodoText = "milk";

        note.AddTodoCommand.Execute(null);

        Assert.False(note.ShowsEditor);
    }
}
