using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using YappyNotes.App.Views;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Tests;

public class NoteWindowTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly InMemoryNoteRepository _repository = new();
    private readonly CountingNoteRepository _counting;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private WindowManager _windows = null!;

    public NoteWindowTests()
    {
        _counting = new CountingNoteRepository(_repository);
        _notes = new NoteService(_counting, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, Debounce);
        _windows = new WindowManager(_notes, _autoSave);
    }

    private NoteViewModel NewViewModel(Action<Note>? arrange = null)
    {
        var note = _notes.CreateAsync().GetAwaiter().GetResult();
        arrange?.Invoke(note);
        return new NoteViewModel(note, _notes, _autoSave, _windows);
    }

    private static void Click(NoteWindow window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Header_CloseIsTheLastActionAndKeepsTheNoteAvailable()
    {
        var note = await _notes.CreateAsync();
        await _windows.ShowNoteAsync(note.Id);
        var window = _windows.WindowFor(note.Id)!;
        Dispatcher.UIThread.RunJobs();

        var close = window.FindControl<Button>("CloseButton")!;
        var color = window.FindControl<Button>("ColorButton")!;
        var pin = window.FindControl<ToggleButton>("PinButton")!;
        var archive = window.FindControl<Button>("ArchiveButton")!;
        Assert.True(color.Bounds.Left < pin.Bounds.Left
            && pin.Bounds.Left < archive.Bounds.Left
            && archive.Bounds.Left < close.Bounds.Left);
        Assert.Same(_windows.ViewModelFor(note.Id)!.ArchiveCommand, archive.Command);

        Click(window, close);
        await _autoSave.WhenIdleAsync();

        Assert.Empty(_windows.OpenNotes);
        Assert.False((await _repository.GetByIdAsync(note.Id))!.IsArchived);
    }

    [AvaloniaFact]
    public async Task ModeButtons_WithoutTyping_DoNotScheduleAnAutosave()
    {
        var viewModel = NewViewModel(note => note.Content = "milk");
        var window = new NoteWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var writesBefore = _counting.Updates;

        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);
        _clock.Advance(Debounce * 2);
        await _autoSave.WhenIdleAsync();

        Assert.Equal("milk", viewModel.Content);
        Assert.Equal(writesBefore, _counting.Updates);
    }

    [AvaloniaFact]
    public async Task ModeButtons_AfterTyping_CloseStillFlushesThePendingSave()
    {
        var viewModel = NewViewModel(note => note.Content = "milk");
        var window = new NoteWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        viewModel.Content = "milk and bread";
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);
        window.Close();
        await _autoSave.WhenIdleAsync();

        Assert.Equal("milk and bread", (await _repository.GetByIdAsync(viewModel.Id))!.Content);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task NoteWindow_OnLinux_IsAvailableToWindowSwitcher()
    {
        var note = await _notes.CreateAsync();
        await _windows.ShowNoteAsync(note.Id);

        Assert.Equal(OperatingSystem.IsLinux(), _windows.WindowFor(note.Id)!.ShowInTaskbar);
    }

    [AvaloniaFact]
    public void NoteWindow_BoundToANote_TakesTheSizeTheNoteRemembers()
    {
        var viewModel = NewViewModel(note =>
        {
            note.Width = 420;
            note.Height = 380;
        });

        var window = new NoteWindow(viewModel);

        Assert.Equal(420, window.Width);
        Assert.Equal(380, window.Height);
    }

    [AvaloniaFact]
    public void NoteWindow_BoundToAPinnedNote_OpensOnTop()
    {
        var viewModel = NewViewModel(note => note.IsAlwaysOnTop = true);

        var window = new NoteWindow(viewModel);

        Assert.True(window.Topmost);
    }

    [AvaloniaFact]
    public void NoteWindow_BoundToANote_ShowsItsTextAndColour()
    {
        var viewModel = NewViewModel(note =>
        {
            note.Content = "milk and bread";
            note.Color = NoteColor.Blue;
        });

        var window = new NoteWindow(viewModel);

        Assert.Same(viewModel, window.DataContext);
        Assert.Equal("milk and bread", viewModel.Content);
    }

    /// <summary>
    /// The feedback loop this guards against: applying a note's saved geometry to
    /// the window raises the window's own change events, which write the geometry
    /// back to the note, which schedules a save. Opening a note nobody touched
    /// would rewrite every one of them on every start.
    /// </summary>
    [AvaloniaFact]
    public async Task NoteWindow_OpenedAtTheGeometryItWasSavedWith_WritesNothing()
    {
        var viewModel = NewViewModel(note =>
        {
            note.X = 300;
            note.Y = 150;
            note.Width = 420;
            note.Height = 380;
        });
        var writesBefore = _counting.Updates;

        var window = new NoteWindow(viewModel);
        window.Show();

        _clock.Advance(Debounce * 2);
        await _autoSave.WhenIdleAsync();

        Assert.Equal(writesBefore, _counting.Updates);
    }

    /// <summary>
    /// The persistence, not the gesture: this sets the size directly, the way the
    /// window manager does at the end of a real resize drag. Whether a reader can
    /// start one is NoteWindowDragTests' question.
    /// </summary>
    [AvaloniaFact]
    public async Task NoteWindow_WhenItsSizeChanges_WritesTheNewSizeToTheNote()
    {
        var viewModel = NewViewModel();
        var window = new NoteWindow(viewModel);
        window.Show();

        window.Width = 500;
        window.Height = 450;

        _clock.Advance(Debounce);
        await _autoSave.WhenIdleAsync();

        var stored = await _repository.GetByIdAsync(viewModel.Id);
        Assert.Equal((500, 450), (stored!.Width, stored.Height));
    }

    [AvaloniaFact]
    public void ShowNote_ForANoteAlreadyOnScreen_DoesNotOpenASecondWindow()
    {
        // Two windows over one note would both be editing the same object and
        // racing each other's autosave.
        var viewModel = NewViewModel();

        _windows.ShowNoteAsync(viewModel.Id).GetAwaiter().GetResult();
        _windows.ShowNoteAsync(viewModel.Id).GetAwaiter().GetResult();

        Assert.Single(_windows.OpenNotes);
    }

    [AvaloniaFact]
    public void CloseNote_ForAnOpenNote_TakesItsWindowOffTheDesktop()
    {
        var viewModel = NewViewModel();
        _windows.ShowNoteAsync(viewModel.Id).GetAwaiter().GetResult();

        _windows.CloseNote(viewModel.Id);

        Assert.Empty(_windows.OpenNotes);
    }

    [AvaloniaFact]
    public void CloseNote_ForANoteThatWasNeverShown_DoesNothing()
    {
        _windows.CloseNote(Guid.CreateVersion7());

        Assert.Empty(_windows.OpenNotes);
    }
}
