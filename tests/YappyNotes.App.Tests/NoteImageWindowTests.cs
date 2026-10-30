using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using YappyNotes.App.Views;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Tests;

public class NoteImageWindowTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryNoteRepository _repository = new();
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;

    public NoteImageWindowTests()
    {
        _notes = new NoteService(_repository, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, TimeSpan.FromMilliseconds(750));
    }

    private NoteWindow OpenWith(string content, Uri? picked)
    {
        var note = _notes.CreateAsync().GetAwaiter().GetResult();
        note.Content = content;
        var window = new NoteWindow(new NoteViewModel(
            note, _notes, _autoSave, new FakeWindowManager(), images: new ImagePicker(picked)));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void ChooseImage(NoteWindow window)
    {
        var chrome = window.FindControl<Border>("NoteChrome")!;
        var body = window.FindControl<TextBox>("Body")!;
        var point = body.IsEffectivelyVisible
            ? body.TranslatePoint(new Point(8, 8), window)!.Value
            : chrome.TranslatePoint(new Point(8, 8), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        if (!chrome.ContextMenu!.IsOpen)
        {
            chrome.ContextMenu.Open(chrome);
        }

        Dispatcher.UIThread.RunJobs();
        var item = chrome.ContextMenu.Items.OfType<MenuItem>()
            .Single(menuItem => (string?)menuItem.Header == "Add image…");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        chrome.ContextMenu.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void AddImage_FromNoteMenu_InsertsAtCaretAndCreatesImageBlock()
    {
        var window = OpenWith("Olá!  Tchau!", new Uri("file:///tmp/meu%20diagrama.png"));
        var note = (NoteViewModel)window.DataContext!;
        note.BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        var body = window.FindControl<TextBox>("Body")!;
        body.CaretIndex = 5;

        ChooseImage(window);

        Assert.Equal("Olá! \n![meu diagrama](file:///tmp/meu%20diagrama.png)\n Tchau!", note.Content);
        Assert.Equal(MarkdownBlockKind.Image, note.Lines[1].Block.Kind);
        Assert.True(body.IsEffectivelyVisible);
        Assert.Equal(note.Content.IndexOf(" Tchau!", StringComparison.Ordinal), body.CaretIndex);
    }

    [AvaloniaFact]
    public void AddImage_WhenPickerIsCancelled_KeepsEditorAndSelection()
    {
        var window = OpenWith("Olá!  Tchau!", null);
        var note = (NoteViewModel)window.DataContext!;
        note.BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        var body = window.FindControl<TextBox>("Body")!;
        body.SelectionStart = 5;
        body.SelectionEnd = 7;

        ChooseImage(window);

        Assert.Equal("Olá!  Tchau!", note.Content);
        Assert.True(body.IsEffectivelyVisible);
        Assert.Equal((5, 7), (body.SelectionStart, body.SelectionEnd));
    }

    [AvaloniaFact]
    public void AddImage_WithSelectedText_ReplacesOnlyThatText()
    {
        var window = OpenWith("antes depois", new Uri("file:///tmp/diagrama.png"));
        var note = (NoteViewModel)window.DataContext!;
        note.BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();
        var body = window.FindControl<TextBox>("Body")!;
        body.SelectionStart = 6;
        body.SelectionEnd = 12;

        ChooseImage(window);

        Assert.Equal("antes \n![diagrama](file:///tmp/diagrama.png)\n", note.Content);
        Assert.Equal(note.Content.Length, body.CaretIndex);
    }

    [AvaloniaFact]
    public void AddImage_FromFormattedNoteMenu_AppendsReference()
    {
        var window = OpenWith("antes", new Uri("file:///tmp/diagrama.png"));
        var note = (NoteViewModel)window.DataContext!;

        ChooseImage(window);

        Assert.Equal("antes\n![diagrama](file:///tmp/diagrama.png)\n", note.Content);
        Assert.False(note.IsEditing);
        Assert.Equal(MarkdownBlockKind.Image, note.Lines[1].Block.Kind);
    }

    [AvaloniaFact]
    public async Task NoteWindow_ImageReferenceChanges_ReleasesOldBitmap()
    {
        var uri = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "sample.png"));
        var window = OpenWith($"![image]({uri.AbsoluteUri})", null);
        var note = (NoteViewModel)window.DataContext!;
        var session = window.ImageSession!;
        var loaded = await WaitForImageAsync(session, uri);

        note.Content = "other text";
        Dispatcher.UIThread.RunJobs();

        Assert.Null(session.Get(uri));
        Assert.True(loaded.IsDisposed);
    }

    [AvaloniaFact]
    public async Task NoteWindow_WhenClosed_ReleasesLoadedBitmap()
    {
        var uri = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "sample.png"));
        var window = OpenWith($"![image]({uri.AbsoluteUri})", null);
        var session = window.ImageSession!;
        var loaded = await WaitForImageAsync(session, uri);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(loaded.IsDisposed);
    }

    private static async Task<ImageLoadResult> WaitForImageAsync(NoteImageSession session, Uri uri)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (session.Get(uri) is { Status: ImageLoadStatus.Ready } loaded)
            {
                return loaded;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("the image loader did not publish the bitmap");
    }

    private sealed class ImagePicker(Uri? picked) : IImageFilePicker
    {
        public Task<Uri?> PickAsync() => Task.FromResult(picked);
    }
}
