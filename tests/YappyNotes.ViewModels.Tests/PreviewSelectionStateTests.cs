using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.ViewModels.Tests;

public class PreviewSelectionStateTests
{
    [Fact]
    public async Task PreviewSelection_AfterContentChanges_IsDiscarded()
    {
        var notes = new NoteService(new InMemoryNoteRepository(), TimeProvider.System);
        var note = await notes.CreateAsync(TestContext.Current.CancellationToken);
        note.Content = "first\nsecond";
        var view = new NoteViewModel(note, notes, new AutoSaveService(notes, TimeProvider.System), new FakeWindowManager());
        view.SelectPreviewText(0, 1, 1, 3);
        Assert.NotNull(view.PreviewTextSelection);

        view.Content = "changed";

        Assert.Null(view.PreviewTextSelection);
    }

    [Fact]
    public async Task PreviewSelection_AfterEnteringEdit_IsDiscarded()
    {
        var notes = new NoteService(new InMemoryNoteRepository(), TimeProvider.System);
        var note = await notes.CreateAsync(TestContext.Current.CancellationToken);
        note.Content = "first";
        var view = new NoteViewModel(note, notes, new AutoSaveService(notes, TimeProvider.System), new FakeWindowManager());
        view.SelectPreviewText(0, 0, 0, 3);
        Assert.NotNull(view.PreviewTextSelection);

        view.ShowEdit();

        Assert.Null(view.PreviewTextSelection);
    }
}
