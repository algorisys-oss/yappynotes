using YappyNotes.App.Views;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App;

/// <summary>
/// Keeps one window - and one view-model - per note.
/// </summary>
/// <remarks>
/// Building view-models is this class's job rather than its callers', because
/// two view-models over one note would mean two <c>Note</c> objects, both held by
/// the autosave, racing each other's writes. Asking for a note already on screen
/// brings its window forward instead of opening a second one.
/// </remarks>
public sealed class WindowManager : IWindowManager
{
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private readonly Func<SettingsViewModel>? _newSettings;
    private readonly PastedImageStore? _pastedImages;
    private SettingsWindow? _settingsWindow;
    private readonly Dictionary<Guid, NoteWindow> _open = [];
    private readonly Dictionary<Guid, NoteViewModel> _viewModels = [];

    /// <param name="newSettings">
    /// How to build the settings view-model, when one is wanted. A factory
    /// rather than an instance so that nothing reads the database until someone
    /// actually opens the window - and optional, because the tests that are
    /// about note windows have no business standing up a settings store.
    /// </param>
    public WindowManager(NoteService notes, AutoSaveService autoSave, Func<SettingsViewModel>? newSettings = null,
        UserPaths? paths = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(autoSave);

        _notes = notes;
        _autoSave = autoSave;
        _newSettings = newSettings;
        _pastedImages = paths is null ? null : new PastedImageStore(paths);
    }

    public IReadOnlyCollection<Guid> OpenNotes => _open.Keys;

    /// <summary>The live view-model for a note, if one has been built.</summary>
    public NoteViewModel? ViewModelFor(Guid noteId) => _viewModels.GetValueOrDefault(noteId);

    /// <summary>The settings window's view-model, while that window is open.</summary>
    public SettingsViewModel? OpenSettings => _settingsWindow?.DataContext as SettingsViewModel;

    public async Task ShowNoteAsync(Guid noteId)
    {
        if (_open.TryGetValue(noteId, out var already))
        {
            already.Activate();
            return;
        }

        var viewModel = await ViewModelForAsync(noteId);
        if (viewModel is null)
        {
            // Archived or purged between the list being drawn and the click.
            return;
        }

        var window = new NoteWindow(viewModel);
        window.ImageStore = _pastedImages;
        _open[noteId] = window;

        // However it closed - its own button, or the archive command - the
        // bookkeeping is the same. The view-model goes with the window: keeping
        // it would hand back a stale copy after an archive or a restore, and the
        // only reason to hold one at all is so that two windows cannot both be
        // editing the same note at once.
        window.Closed += (_, _) =>
        {
            _open.Remove(noteId);
            _viewModels.Remove(noteId);
        };

        window.Show();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        if (_newSettings is null)
        {
            return;
        }

        var settings = _newSettings();
        _settingsWindow = new SettingsWindow(settings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();

        // After Show, so the controls exist to be filled in.
        _ = settings.LoadAsync();
    }

    /// <summary>The manager window, while it is open.</summary>
    public ManagerWindow? OpenManager { get; private set; }

    /// <remarks>
    /// A fresh window each time it has been closed: Avalonia cannot show a closed
    /// window again, and since the app lives in the tray, closing the manager no
    /// longer ends it. Hiding on close instead was the alternative, and it is a
    /// trap - a window that cancels its close also cancels the app's shutdown.
    /// </remarks>
    public void ShowManager()
    {
        if (OpenManager is not null)
        {
            OpenManager.Activate();
            return;
        }

        var window = new ManagerWindow();
        window.Bind(new ManagerViewModel(_notes, this));
        window.Closed += (_, _) => OpenManager = null;
        OpenManager = window;
        window.Show();
    }

    public async Task ChangeNoteAsync(Guid noteId, Func<string, string> changeContent)
    {
        ArgumentNullException.ThrowIfNull(changeContent);

        // A note with a view-model is changed through it. Its Note is the one the
        // autosave holds, possibly with typing not yet written; changing a stored
        // copy instead would be overwritten by that autosave a moment later.
        if (_viewModels.TryGetValue(noteId, out var live))
        {
            live.Content = changeContent(live.Content);

            // Written now: whoever made the change reads the note back next, and
            // would otherwise find the debounce still holding it.
            await _autoSave.FlushAsync(noteId);
            return;
        }

        if ((await _notes.GetActiveAsync()).FirstOrDefault(note => note.Id == noteId) is { } stored)
        {
            stored.Content = changeContent(stored.Content);
            await _notes.SaveAsync(stored);
        }
    }

    public void CloseNote(Guid noteId)
    {
        if (_open.TryGetValue(noteId, out var window))
        {
            window.Close();
        }
    }

    private async Task<NoteViewModel?> ViewModelForAsync(Guid noteId)
    {
        if (_viewModels.TryGetValue(noteId, out var already))
        {
            return already;
        }

        var note = (await _notes.GetActiveAsync()).FirstOrDefault(n => n.Id == noteId);
        if (note is null)
        {
            return null;
        }

        var viewModel = new NoteViewModel(
            note, _notes, _autoSave, this,
            TimeProvider.System,
            new AvaloniaUiDispatcher(),
            new AvaloniaLinkLauncher(() => _open.GetValueOrDefault(note.Id)),
            new AvaloniaImageFilePicker(() => _open.GetValueOrDefault(note.Id)));
        _viewModels[noteId] = viewModel;
        return viewModel;
    }
}
