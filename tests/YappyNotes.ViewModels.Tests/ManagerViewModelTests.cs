using Microsoft.Extensions.Time.Testing;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.ViewModels.Tests;

public class ManagerViewModelTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly InMemoryNoteRepository _repository = new();
    private readonly NoteService _notes;
    private readonly FakeWindowManager _windows = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ManagerViewModelTests() => _notes = new NoteService(_repository, _clock);

    private ManagerViewModel NewManager() => new(_notes, _windows);

    private async Task<Note> NoteAsync(string title, string content = "", bool archived = false)
    {
        var note = await _notes.CreateAsync(Token);
        note.Title = title;
        note.Content = content;
        await _notes.SaveAsync(note, Token);
        if (archived)
        {
            await _notes.ArchiveAsync(note.Id, Token);
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        return note;
    }

    [Fact]
    public async Task RefreshAsync_OnAnEmptyBoard_ShowsNothing()
    {
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Empty(manager.Items);
        Assert.True(manager.IsEmpty);
    }

    [Fact]
    public async Task RefreshAsync_ListsTheActiveNotes()
    {
        await NoteAsync("Shopping");
        await NoteAsync("Standup", archived: true);
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal(["Shopping"], manager.Items.Select(i => i.DisplayTitle));
    }

    /// <summary>
    /// Newest first: the manager is for finding the note you were just working
    /// on, which is the opposite order from the repository's oldest-first.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_PutsTheMostRecentlyChangedNoteFirst()
    {
        var oldest = await NoteAsync("oldest");
        await NoteAsync("middle");
        await NoteAsync("newest");

        // Touching the oldest note moves it to the front.
        oldest.Content = "touched just now";
        await _notes.SaveAsync(oldest, Token);

        var manager = NewManager();
        await manager.RefreshAsync(Token);

        Assert.Equal(["oldest", "newest", "middle"], manager.Items.Select(i => i.DisplayTitle));
    }

    [Fact]
    public async Task RefreshAsync_ForANoteWithNoTitle_NamesItAfterItsFirstLine()
    {
        await NoteAsync(string.Empty, "buy milk\nand bread");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("buy milk", manager.Items[0].DisplayTitle);
    }

    [Fact]
    public async Task RefreshAsync_ForANoteWithNothingInItAtAll_CallsItUntitled()
    {
        await NoteAsync(string.Empty);
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("Untitled", manager.Items[0].DisplayTitle);
    }

    [Fact]
    public async Task RefreshAsync_ForANoteWithBlankLinesFirst_SkipsThemWhenNamingIt()
    {
        await NoteAsync(string.Empty, "\n   \nthe actual first line");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("the actual first line", manager.Items[0].DisplayTitle);
    }

    [Fact]
    public async Task RefreshAsync_ShowsAOneLinePreviewOfTheBody()
    {
        await NoteAsync("Shopping", "milk\nbread\neggs");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("milk bread eggs", manager.Items[0].Preview);
    }

    [Fact]
    public async Task RefreshAsync_WithAnImageReference_ShowsACompactPreviewWithoutItsUri()
    {
        await NoteAsync("Teste", "Teste\n![diagrama](file:///home/andre/Imagens/diagrama.jpeg)");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("Teste [image]", manager.Items[0].Preview);
    }

    [Fact]
    public async Task RefreshAsync_WithAnUntitledImageOnly_UsesACompactTitle()
    {
        await NoteAsync(string.Empty, "![diagrama](file:///home/andre/Imagens/diagrama.jpeg)");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("[image]", manager.Items[0].DisplayTitle);
        Assert.Equal("[image]", manager.Items[0].Preview);
    }

    [Fact]
    public async Task SearchCommand_WithText_KeepsOnlyMatchingNotes()
    {
        await NoteAsync("Shopping", "milk");
        await NoteAsync("Standup", "deploy the thing");
        var manager = NewManager();
        await manager.RefreshAsync(Token);

        manager.SearchText = "deploy";
        await manager.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["Standup"], manager.Items.Select(i => i.DisplayTitle));
    }

    [Fact]
    public async Task SearchCommand_WithTheTextCleared_ShowsEverythingAgain()
    {
        await NoteAsync("Shopping");
        await NoteAsync("Standup");
        var manager = NewManager();
        manager.SearchText = "shopping";
        await manager.SearchCommand.ExecuteAsync(null);

        manager.SearchText = "";
        await manager.SearchCommand.ExecuteAsync(null);

        Assert.Equal(2, manager.Items.Count);
    }

    [Fact]
    public async Task ShowingArchive_WhenTurnedOn_ListsWhatWasFiledAway()
    {
        await NoteAsync("on the desktop");
        await NoteAsync("filed away", archived: true);
        var manager = NewManager();

        manager.ShowingArchive = true;
        await manager.RefreshAsync(Token);

        Assert.Equal(["filed away"], manager.Items.Select(i => i.DisplayTitle));
    }

    [Fact]
    public async Task SearchCommand_WhileShowingTheArchive_SearchesTheArchive()
    {
        await NoteAsync("milk", archived: false);
        await NoteAsync("milk run, done", archived: true);
        await NoteAsync("something else", archived: true);
        var manager = NewManager();
        manager.ShowingArchive = true;

        manager.SearchText = "milk";
        await manager.SearchCommand.ExecuteAsync(null);

        Assert.Equal(["milk run, done"], manager.Items.Select(i => i.DisplayTitle));
    }

    [Fact]
    public async Task NewNoteCommand_MakesANoteAndOpensItOnTheDesktop()
    {
        var manager = NewManager();

        await manager.NewNoteCommand.ExecuteAsync(null);

        var created = Assert.Single(await _notes.GetActiveAsync(Token));
        Assert.Equal([created.Id], _windows.Shown);
    }

    [Fact]
    public async Task NewNoteCommand_PutsTheNewNoteInTheList()
    {
        var manager = NewManager();
        await manager.RefreshAsync(Token);

        await manager.NewNoteCommand.ExecuteAsync(null);

        Assert.Single(manager.Items);
    }

    [Fact]
    public async Task OpenCommand_PutsThatNoteOnTheDesktop()
    {
        var note = await NoteAsync("Shopping");
        var manager = NewManager();
        await manager.RefreshAsync(Token);

        await manager.OpenCommand.ExecuteAsync(manager.Items[0]);

        Assert.Equal([note.Id], _windows.Shown);
    }

    [Fact]
    public async Task ArchiveCommand_FilesTheNoteAwayAndTakesItOffTheList()
    {
        var note = await NoteAsync("Shopping");
        var manager = NewManager();
        await manager.RefreshAsync(Token);

        await manager.ArchiveCommand.ExecuteAsync(manager.Items[0]);

        Assert.Empty(manager.Items);
        Assert.True((await _repository.GetByIdAsync(note.Id, Token))!.IsArchived);
    }

    [Fact]
    public async Task ArchiveCommand_ClosesTheNotesWindowIfItIsOpen()
    {
        var note = await NoteAsync("Shopping");
        var manager = NewManager();
        await manager.RefreshAsync(Token);

        await manager.ArchiveCommand.ExecuteAsync(manager.Items[0]);

        Assert.Equal([note.Id], _windows.Closed);
    }

    [Fact]
    public async Task RestoreCommand_BringsANoteBackToTheDesktop()
    {
        var note = await NoteAsync("filed away", archived: true);
        var manager = NewManager();
        manager.ShowingArchive = true;
        await manager.RefreshAsync(Token);

        await manager.RestoreCommand.ExecuteAsync(manager.Items[0]);

        Assert.Empty(manager.Items);
        Assert.False((await _repository.GetByIdAsync(note.Id, Token))!.IsArchived);
    }

    [Fact]
    public async Task DeleteForeverCommand_OnAnArchivedNote_RemovesItForGood()
    {
        var note = await NoteAsync("done with this", archived: true);
        var manager = NewManager();
        manager.ShowingArchive = true;
        await manager.RefreshAsync(Token);

        await manager.DeleteForeverCommand.ExecuteAsync(manager.Items[0]);

        Assert.Empty(manager.Items);
        Assert.Null(await _repository.GetByIdAsync(note.Id, Token));
    }

    /// <summary>
    /// The archive rule, at the layer a reader actually touches: there is no
    /// single action anywhere that destroys a note still on the desktop.
    /// </summary>
    [Fact]
    public async Task DeleteForeverCommand_IsNotOfferedForANoteThatIsStillOnTheDesktop()
    {
        await NoteAsync("still live");
        var manager = NewManager();
        await manager.RefreshAsync(Token);

        Assert.False(manager.DeleteForeverCommand.CanExecute(manager.Items[0]));
    }

    [Fact]
    public async Task EmptyMessage_OnTheDesktopAndInTheArchive_SaysSomethingDifferent()
    {
        var manager = NewManager();
        await manager.RefreshAsync(Token);
        var desktop = manager.EmptyMessage;

        manager.ShowingArchive = true;
        await manager.RefreshAsync(Token);

        Assert.NotEqual(desktop, manager.EmptyMessage);
    }

    [Fact]
    public void OpenSettingsCommand_AsksForTheSettingsWindow()
    {
        var manager = NewManager();

        manager.OpenSettingsCommand.Execute(null);

        Assert.Equal(1, _windows.SettingsShown);
    }

    [Fact]
    public async Task RefreshAsync_ForANoteWithTodos_ShowsHowManyAreDone()
    {
        await NoteAsync("Shopping", "- [x] bread\n- [ ] milk");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal("1/2", manager.Items[0].TodoProgress);
    }

    [Fact]
    public async Task RefreshAsync_ForANoteWithoutTodos_ShowsNoProgress()
    {
        await NoteAsync("Shopping", "milk");
        var manager = NewManager();

        await manager.RefreshAsync(Token);

        Assert.Equal(string.Empty, manager.Items[0].TodoProgress);
    }
}
