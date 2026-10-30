using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.Sqlite;
using YappyNotes.App.Views;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Tests;

public sealed class NotePasteWindowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"yappynotes-paste-window-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryNoteRepository _repository = new();
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;

    public NotePasteWindowTests()
    {
        _notes = new NoteService(_repository, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, TimeSpan.FromMilliseconds(750));
    }

    private UserPaths Paths => new(_directory);
    private static WriteableBitmap Bitmap() => new(new PixelSize(8, 6), new Vector(96, 96));

    private NoteWindow Open(string content, PastedImageStore? store = null)
    {
        var stored = _notes.CreateAsync().GetAwaiter().GetResult();
        stored.Content = content;
        var window = new NoteWindow(new NoteViewModel(
            stored, _notes, _autoSave, new FakeWindowManager(), _clock, new InlineUiDispatcher()))
        {
            ImageStore = store ?? new PastedImageStore(Paths),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static NoteViewModel Note(NoteWindow window) => (NoteViewModel)window.DataContext!;
    private static TextBox Editor(NoteWindow window) => window.FindControl<TextBox>("Body")!;
    private static void Paste(NoteWindow window)
    {
        window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return;
            await Task.Delay(10);
        }

        throw new TimeoutException("paste did not finish");
    }

    private static Uri ImageUri(NoteWindow window)
        => Assert.Single(Note(window).Lines, line => line.Block.Kind == MarkdownBlockKind.Image).Block.ImageUri!;

    [AvaloniaFact]
    public async Task CtrlV_WithImageWhileEditing_SavesFileAndInsertsOneImageReference()
    {
        var window = Open("before selected after");
        Note(window).BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        var editor = Editor(window);
        editor.SelectionStart = 7;
        editor.SelectionEnd = 15;
        using var bitmap = Bitmap();
        await window.Clipboard!.SetBitmapAsync(bitmap);

        Paste(window);
        await WaitFor(() => Note(window).Lines.Any(line => line.Block.Kind == MarkdownBlockKind.Image));

        var uri = ImageUri(window);
        Assert.Equal($"before \n![imagem]({uri.AbsoluteUri})\n after", Note(window).Content);
        Assert.True(File.Exists(uri.LocalPath));
        Assert.Equal(Paths.ImageDirectory, Path.GetDirectoryName(uri.LocalPath));
        Assert.Equal(Note(window).Content.IndexOf(" after", StringComparison.Ordinal), editor.CaretIndex);

        Note(window).EndEditing();
        Dispatcher.UIThread.RunJobs();
        await WaitFor(() => window.ImageSession?.Get(uri)?.Status == ImageLoadStatus.Ready);
        Assert.Single(window.FindControl<FormattedNote>("Formatted")!.Children.OfType<Border>(),
            row => row.Tag is NoteLine { Block.Kind: MarkdownBlockKind.Image } && row.Child is Image);
    }

    [AvaloniaFact]
    public async Task CtrlV_WithImageWhileFormatted_AppendsReferenceAndShowsThumbnail()
    {
        var window = Open("before");
        window.Focus();
        using var bitmap = Bitmap();
        await window.Clipboard!.SetBitmapAsync(bitmap);

        Paste(window);
        await WaitFor(() => Note(window).Lines.Any(line => line.Block.Kind == MarkdownBlockKind.Image));

        var uri = ImageUri(window);
        await WaitFor(() => window.ImageSession?.Get(uri)?.Status == ImageLoadStatus.Ready);
        Assert.Equal($"before\n![imagem]({uri.AbsoluteUri})\n", Note(window).Content);
        Assert.True(window.IsVisible);
        Assert.False(Note(window).IsEditing);
        Assert.Single(window.FindControl<FormattedNote>("Formatted")!.Children.OfType<Border>(),
            row => row.Tag is NoteLine { Block.Kind: MarkdownBlockKind.Image } && row.Child is Image);
    }

    [AvaloniaFact]
    public async Task CtrlV_WithTextOnly_UsesNormalTextPaste()
    {
        var window = Open("before selected after");
        Note(window).BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        Editor(window).SelectionStart = 7;
        Editor(window).SelectionEnd = 15;
        await window.Clipboard!.SetTextAsync("plain");

        Paste(window);
        await WaitFor(() => Note(window).Content.Contains("plain", StringComparison.Ordinal));

        Assert.Equal("before plain after", Note(window).Content);
        Assert.False(Directory.Exists(Paths.ImageDirectory));
    }

    [AvaloniaFact]
    public async Task CtrlV_WithImageAndText_PrefersImageExactlyOnce()
    {
        var window = Open("before");
        Note(window).BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        using var bitmap = Bitmap();
        var item = new DataTransferItem();
        item.SetBitmap(bitmap);
        item.SetText("clipboard text");
        var data = new DataTransfer();
        data.Add(item);
        await window.Clipboard!.SetDataAsync(data);

        Paste(window);
        await WaitFor(() => Note(window).Lines.Any(line => line.Block.Kind == MarkdownBlockKind.Image));

        Assert.DoesNotContain("clipboard text", Note(window).Content);
        Assert.Single(Note(window).Lines, line => line.Block.Kind == MarkdownBlockKind.Image);
        Assert.Single(Directory.GetFiles(Paths.ImageDirectory));
    }

    [AvaloniaFact]
    public async Task CtrlV_WhenImageSaveFails_KeepsSelectedTextAndShowsFailure()
    {
        var store = new PastedImageStore(Paths, path =>
        {
            File.WriteAllText(path, "partial");
            throw new IOException("disk failed");
        });
        var window = Open("before selected after", store);
        Note(window).BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        Editor(window).SelectionStart = 7;
        Editor(window).SelectionEnd = 15;
        using var bitmap = Bitmap();
        await window.Clipboard!.SetBitmapAsync(bitmap);

        Paste(window);
        await WaitFor(() => window.FindControl<TextBlock>("PasteFailure")!.IsVisible);

        Assert.Equal("before selected after", Note(window).Content);
        Assert.Equal((7, 15), (Editor(window).SelectionStart, Editor(window).SelectionEnd));
        Assert.Empty(Directory.GetFiles(Paths.ImageDirectory));
    }

    [AvaloniaFact]
    public async Task PastedImageStore_RemovingReference_DoesNotDeleteManagedFile()
    {
        var window = Open("before");
        window.Focus();
        using var bitmap = Bitmap();
        await window.Clipboard!.SetBitmapAsync(bitmap);
        Paste(window);
        await WaitFor(() => Note(window).Lines.Any(line => line.Block.Kind == MarkdownBlockKind.Image));
        var uri = ImageUri(window);

        Note(window).Content = "no image";
        Note(window).ArchiveCommand.Execute(null);
        window.Close();

        Assert.True(File.Exists(uri.LocalPath));
    }

    [AvaloniaFact]
    public async Task PastedImage_AfterRestart_StillRendersFromManagedDirectory()
    {
        var paths = Paths;
        Guid id;
        Uri uri;
        await using (var first = await AppServices.StartAsync(paths, cancellationToken: TestContext.Current.CancellationToken))
        {
            var stored = await first.Notes.CreateAsync(TestContext.Current.CancellationToken);
            stored.Content = "before";
            await first.Notes.SaveAsync(stored, TestContext.Current.CancellationToken);
            id = stored.Id;
            var window = new NoteWindow(new NoteViewModel(
                stored, first.Notes, first.AutoSave, new FakeWindowManager(), _clock, new InlineUiDispatcher()))
            {
                ImageStore = new PastedImageStore(paths),
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Focus();
            using var bitmap = Bitmap();
            await window.Clipboard!.SetBitmapAsync(bitmap);
            Paste(window);
            await WaitFor(() => Note(window).Lines.Any(line => line.Block.Kind == MarkdownBlockKind.Image));
            uri = ImageUri(window);
            await first.AutoSave.FlushAsync(id, TestContext.Current.CancellationToken);
            window.Close();
        }

        await using (var reopened = await AppServices.StartAsync(paths, cancellationToken: TestContext.Current.CancellationToken))
        {
            var stored = Assert.Single(await reopened.Notes.GetActiveAsync(TestContext.Current.CancellationToken), note => note.Id == id);
            Assert.Contains(uri.AbsoluteUri, stored.Content);
            Assert.True(File.Exists(uri.LocalPath));
            var window = new NoteWindow(new NoteViewModel(
                stored, reopened.Notes, reopened.AutoSave, new FakeWindowManager(), _clock, new InlineUiDispatcher()));
            window.Show();
            await WaitFor(() => window.ImageSession?.Get(uri)?.Status == ImageLoadStatus.Ready);
            Assert.Single(window.FindControl<FormattedNote>("Formatted")!.Children.OfType<Border>(),
                row => row.Tag is NoteLine { Block.Kind: MarkdownBlockKind.Image } && row.Child is Image);
            window.Close();
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
