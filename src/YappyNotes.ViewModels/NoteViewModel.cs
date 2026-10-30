using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YappyNotes.Core;

namespace YappyNotes.ViewModels;

/// <summary>
/// One sticky note, as the window bound to it sees it.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="Note"/> is the single copy of the state: every property here
/// reads and writes straight through to it rather than keeping a second copy in
/// sync. That is why <c>[ObservableProperty]</c> is not used - the generated
/// backing field would be exactly the duplicate this is avoiding, and the two
/// would disagree the first time anything changed the note from elsewhere.
/// </para>
/// <para>
/// Every setter that actually changes something schedules an autosave. Setting a
/// property to the value it already holds writes nothing, so a window
/// re-applying its bindings costs no disk.
/// </para>
/// </remarks>
public sealed partial class NoteViewModel : ObservableObject
{
    private readonly Note _note;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private readonly IWindowManager _windows;
    private readonly TimeProvider _timeProvider;
    private readonly IUiDispatcher _ui;
    private readonly ILinkLauncher _links;
    private readonly IImageFilePicker? _images;

    /// <param name="timeProvider">
    /// Used by the timer, if the note has one. Defaults to the system clock so
    /// that the many tests which do not care about timers need not supply it.
    /// </param>
    /// <param name="ui">
    /// How a tick gets back to the UI thread. Defaults to running the work where
    /// it was posted from, which is what a test wants and what a note with no
    /// timer never exercises.
    /// </param>
    public NoteViewModel(
        Note note,
        NoteService notes,
        AutoSaveService autoSave,
        IWindowManager windows,
        TimeProvider? timeProvider = null,
        IUiDispatcher? ui = null,
        ILinkLauncher? links = null,
        IImageFilePicker? images = null)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(autoSave);
        ArgumentNullException.ThrowIfNull(windows);

        _note = note;
        _notes = notes;
        _autoSave = autoSave;
        _windows = windows;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ui = ui ?? new ImmediateUiDispatcher();
        _links = links ?? new NoLinkLauncher();
        _images = images;

        AttachTimer();
        ScanForLinks();
        Reparse();
    }

    public Guid Id => _note.Id;

    public string Title
    {
        get => _note.Title;
        set => Set(_note.Title, value, v => _note.Title = v);
    }

    public string Content
    {
        get => _note.Content;
        set
        {
            // An empty note shows the editor because there is nothing to format, not
            // because it is being edited. A change arriving while that editor is the
            // one showing is typing into it, and the first letter must not turn the
            // note formatted and take the keyboard away - which it did.
            var typedIntoEmptyEditor = ShowsEditor && !IsEditing && !string.IsNullOrEmpty(value);

            Set(_note.Content, value, v => _note.Content = v);

            if (typedIntoEmptyEditor)
            {
                EditCaret = _note.Content.Length;
                OnPropertyChanged(nameof(EditCaret));
                SetEditing(true);
            }

            ScanForLinks();
            Reparse();
        }
    }

    /// <summary>
    /// The links in this note's text, as they are typed.
    /// </summary>
    /// <remarks>
    /// Only allow-listed schemes get this far. See <see cref="ShowsLinkBar"/> for
    /// when they are offered.
    /// </remarks>
    public IReadOnlyList<LinkSpan> Links { get; private set; } = [];

    public bool HasLinks => Links.Count > 0;

    /// <summary>
    /// Whether the links are offered under the note: only while editing. Formatted,
    /// each link is clickable where it is written, and the bar would offer it twice.
    /// </summary>
    public bool ShowsLinkBar => HasLinks && ShowsEditor;

    /// <summary>The note's Markdown, parsed into the lines the formatted view draws.</summary>
    public IReadOnlyList<NoteLine> Lines { get; private set; } = [];

    /// <summary>
    /// Whether the reader is typing in the Markdown rather than looking at it
    /// formatted. How the note is being looked at, not part of the note, so it is
    /// never stored and changing it writes nothing.
    /// </summary>
    public bool IsEditing { get; private set; }

    /// <summary>
    /// Whether the editor is showing. Also true of an empty note, which has
    /// nothing to format - and which is what a new note is, so it opens ready to
    /// type into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not while a to-do is being added: the new line is drawn in the formatted note,
    /// and an empty note showing the editor instead hid that line and kept the
    /// keyboard - the first letter typed became the note's text.
    /// </para>
    /// <para>
    /// Nor straight after "Clear completed" has emptied a note of nothing but ticked
    /// to-dos: the footer with its Undo is part of the formatted note, and would be
    /// hidden at the one moment it is needed.
    /// </para>
    /// </remarks>
    public bool ShowsEditor => IsEditing || (string.IsNullOrWhiteSpace(_note.Content) && !IsAddingTodo && !CanUndoClear);

    /// <summary>Where the caret goes when the editor opens.</summary>
    public int EditCaret { get; private set; }

    /// <summary>
    /// A press on the formatted note. It ticks a checklist box, opens a link, or
    /// starts editing with the caret on the character that was pressed.
    /// </summary>
    /// <param name="renderedIndex">Which character of the drawn line was pressed.</param>
    /// <param name="trailing">Whether the press was on that character's right half.</param>
    public async Task PressAsync(NoteLine line, int renderedIndex, bool trailing = false)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.Block.Kind == MarkdownBlockKind.Image)
        {
            BeginEditing(line.Block.SourceStart);
            return;
        }

        if (line.IsCheckBoxAt(renderedIndex))
        {
            // The same path as typing, so the save is the same debounced one.
            Content = NoteMarkdown.ToggleTask(_note.Content, line.Block.CheckMarkIndex);
            return;
        }

        if (line.RunAt(renderedIndex)?.Link is { } address)
        {
            await _links.OpenAsync(address);
            return;
        }

        // The right half of a letter puts the caret after it, as in any text box.
        // Only over text: past the end, or over the marker, there is no letter to
        // be after.
        var after = trailing && line.RunAt(renderedIndex) is not null ? 1 : 0;
        BeginEditing(line.SourceIndexAt(renderedIndex) + after);
    }

    /// <summary>Starts editing with the caret after the last character - a press below the text.</summary>
    public void BeginEditingAtEnd() => BeginEditing(_note.Content.Length);

    /// <summary>Resumes editing at a position after a note menu action.</summary>
    public void BeginEditingAt(int caret) => BeginEditing(caret);

    public void EndEditing() => SetEditing(false);

    /// <summary>
    /// Chooses an image and inserts its reference at the editor selection, or at
    /// the end of a formatted note. The content setter handles parse and autosave.
    /// </summary>
    /// <returns>The edit to restore in the editor, or null on cancellation or an unsupported file.</returns>
    public async Task<MarkdownEdit?> AddImageAsync(int? selectionStart = null, int? selectionEnd = null)
    {
        if (_images is null || await _images.PickAsync() is not { } file)
        {
            return null;
        }

        return InsertImageReference(file, null, selectionStart, selectionEnd);
    }

    /// <summary>Inserts an already saved clipboard PNG using a neutral description.</summary>
    public MarkdownEdit? PasteSavedImage(Uri file, int? selectionStart = null, int? selectionEnd = null)
        => InsertImageReference(file, "imagem", selectionStart, selectionEnd);

    private MarkdownEdit? InsertImageReference(Uri file, string? description, int? selectionStart, int? selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(file);

        var start = Math.Clamp(selectionStart ?? Content.Length, 0, Content.Length);
        var end = Math.Clamp(selectionEnd ?? start, 0, Content.Length);
        (start, end) = (Math.Min(start, end), Math.Max(start, end));
        var withoutSelection = Content.Remove(start, end - start);
        var edit = NoteImageReference.Insert(withoutSelection, start, file, description);
        if (edit.Content == withoutSelection)
        {
            return null;
        }

        Content = edit.Content;
        return edit;
    }

    /// <summary>Whether any line of the note is a to-do.</summary>
    public bool HasTodos => Lines.Any(line => line.Block.Kind == MarkdownBlockKind.Task);

    /// <summary>
    /// Whether a checklist offers "+ Add a to-do" as its last line: on a formatted
    /// note with to-dos, while nothing is being added.
    /// </summary>
    public bool ShowsTodoPrompt => !ShowsEditor && HasTodos && !IsAddingTodo;

    /// <summary>
    /// Whether a new to-do is being typed, on its own line of the list with its own
    /// box. Like editing, it is how the note is being looked at and never stored.
    /// </summary>
    /// <remarks>
    /// On the list itself rather than in a separate field under the note: reported
    /// from real use, a field below with each item appearing above it read as the
    /// note doing something odd.
    /// </remarks>
    public bool IsAddingTodo { get; private set; }

    /// <summary>
    /// Where the new line is drawn: before this index of <see cref="Lines"/>, which is
    /// straight after the last to-do - where <see cref="TodoList.Add"/> will put the
    /// item - or at the end of a note with none.
    /// </summary>
    public int NewTodoPosition => LastTodoIndex() is var last and >= 0 ? last + 1 : Lines.Count;

    /// <summary>The new line's indent: the last to-do's, as the item will be stored.</summary>
    public int NewTodoLevel => LastTodoIndex() is var last and >= 0 ? Lines[last].Block.Level : 0;

    /// <summary>What is typed on the new to-do's line, before it is added.</summary>
    public string NewTodoText
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>
    /// "Add a to-do", from the note's menu or the list's own prompt: opens a new line
    /// with a box to type on, formatted.
    /// </summary>
    [RelayCommand]
    public void StartTodoList()
    {
        SetEditing(false);
        SetAddingTodo(true);
    }

    /// <summary>
    /// The new line was left - a click elsewhere. What was typed is kept, as the item
    /// it was going to be; leaving is not "never mind", Escape is.
    /// </summary>
    public void StopAddingTodos()
    {
        if (!IsAddingTodo)
        {
            return;
        }

        AddTyped();
        SetAddingTodo(false);
    }

    /// <summary>Escape on the new line: what was typed is thrown away.</summary>
    public void CancelAddingTodo()
    {
        NewTodoText = string.Empty;
        SetAddingTodo(false);
    }

    /// <summary>
    /// Enter on the new line: adds the item and opens the next line - or, on an empty
    /// line, finishes, the same rule as a list in the editor.
    /// </summary>
    [RelayCommand]
    public void AddTodo()
    {
        if (string.IsNullOrWhiteSpace(NewTodoText))
        {
            NewTodoText = string.Empty;
            SetAddingTodo(false);
            return;
        }

        AddTyped();
    }

    private void AddTyped()
    {
        if (string.IsNullOrWhiteSpace(NewTodoText))
        {
            return;
        }

        // "@tomorrow" becomes the date it means now, because stored as typed it would
        // be wrong by tomorrow.
        Content = TodoList.Add(_note.Content, TodoDue.ResolveShorthand(NewTodoText, DueStates.TodayBy(_timeProvider)));
        NewTodoText = string.Empty;
    }

    private int LastTodoIndex()
    {
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].Block.Kind == MarkdownBlockKind.Task)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Enter in the editor, on a list line: continues, splits or ends the list.
    /// </summary>
    /// <returns>Where the caret goes, or null where Enter is an ordinary line break.</returns>
    public int? ContinueListOnEnter(int caret)
    {
        if (!IsEditing || TodoList.ContinueOnEnter(_note.Content, caret) is not { } edit)
        {
            return null;
        }

        Content = edit.Content;
        return edit.SelectionStart;
    }

    /// <summary>"2 of 5 done", for the footer under a formatted checklist.</summary>
    public string TodoProgressText
    {
        get
        {
            var progress = TodoList.Progress(_note.Content);
            return progress.Total == 0 ? string.Empty : $"{progress.Done} of {progress.Total} done";
        }
    }

    /// <summary>
    /// Whether the footer under a formatted note shows: on a checklist, and straight
    /// after clearing one, even if nothing is left - that is where its Undo is.
    /// </summary>
    public bool ShowsTodoFooter => !ShowsEditor && (HasTodos || CanUndoClear);

    /// <summary>
    /// The note as it was before the last "Clear completed", and as that clear left
    /// it. The clear can be taken back only while the note is still as it left it:
    /// after any other change, putting the old text back would undo that too.
    /// </summary>
    private (string Before, string After)? _lastClear;

    /// <summary>Whether any to-do is ticked - otherwise there is nothing to clear.</summary>
    public bool HasCompletedTodos => TodoList.Progress(_note.Content).Done > 0;

    public bool CanUndoClear => _lastClear is { } clear && clear.After == _note.Content;

    /// <summary>
    /// Removes the ticked to-dos. Deleting text on one click is the kind of thing this
    /// app is careful about, so it can be undone straight away rather than being
    /// asked about first every time.
    /// </summary>
    [RelayCommand]
    public void ClearCompleted()
    {
        var before = _note.Content;
        var after = TodoList.ClearCompleted(before);

        if (after == before)
        {
            return;
        }

        _lastClear = (before, after);
        Content = after;
    }

    [RelayCommand]
    public void UndoClear()
    {
        if (!CanUndoClear)
        {
            return;
        }

        // The text first, the record after: while the note is still the cleared one,
        // putting the text back is Undo, not somebody typing into an empty note.
        Content = _lastClear!.Value.Before;
        _lastClear = null;
        OnPropertyChanged(nameof(CanUndoClear));
    }

    private void SetAddingTodo(bool adding)
    {
        if (IsAddingTodo == adding)
        {
            return;
        }

        IsAddingTodo = adding;
        OnPropertyChanged(nameof(IsAddingTodo));
        OnPropertyChanged(nameof(ShowsTodoPrompt));

        // On an empty note, adding is what decides between the editor and the list.
        OnPropertyChanged(nameof(ShowsEditor));
        OnPropertyChanged(nameof(ShowsLinkBar));
        OnPropertyChanged(nameof(ShowsTodoFooter));
    }

    /// <summary>
    /// Ctrl+B or Ctrl+I in the editor: wraps the selection in markers, or takes them
    /// off, and saves the way typing does.
    /// </summary>
    /// <returns>
    /// What the editor should select afterwards - the same text, which has moved by
    /// the width of whatever markers went in or came out.
    /// </returns>
    public (int Start, int End) ToggleEmphasis(Emphasis emphasis, int selectionStart, int selectionEnd)
    {
        if (!IsEditing)
        {
            return (selectionStart, selectionEnd);
        }

        var edit = MarkdownEmphasis.Toggle(_note.Content, selectionStart, selectionEnd, emphasis);
        Content = edit.Content;
        return (edit.SelectionStart, edit.SelectionEnd);
    }

    private void BeginEditing(int caret)
    {
        EditCaret = Math.Clamp(caret, 0, _note.Content.Length);
        OnPropertyChanged(nameof(EditCaret));
        SetEditing(true);
    }

    private void SetEditing(bool editing)
    {
        if (IsEditing == editing)
        {
            return;
        }

        IsEditing = editing;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(ShowsEditor));
        OnPropertyChanged(nameof(ShowsLinkBar));
        OnPropertyChanged(nameof(ShowsTodoPrompt));
        OnPropertyChanged(nameof(ShowsTodoFooter));
    }

    private void Reparse()
    {
        var today = DueStates.TodayBy(_timeProvider);
        Lines = [.. NoteMarkdown.Parse(_note.Content).Select(block => NoteLine.For(block, today))];
        OnPropertyChanged(nameof(Lines));

        // Emptying a note, or typing into an empty one, changes whether there is
        // anything to format.
        OnPropertyChanged(nameof(ShowsEditor));
        OnPropertyChanged(nameof(ShowsLinkBar));
        OnPropertyChanged(nameof(HasTodos));
        OnPropertyChanged(nameof(ShowsTodoPrompt));
        OnPropertyChanged(nameof(NewTodoPosition));
        OnPropertyChanged(nameof(NewTodoLevel));
        OnPropertyChanged(nameof(TodoProgressText));
        OnPropertyChanged(nameof(HasCompletedTodos));
        OnPropertyChanged(nameof(CanUndoClear));
        OnPropertyChanged(nameof(ShowsTodoFooter));
    }

    [RelayCommand]
    public async Task OpenLinkAsync(LinkSpan? link)
    {
        if (link is not null)
        {
            await _links.OpenAsync(link.Uri);
        }
    }

    private void ScanForLinks()
    {
        var found = LinkScanner.Scan(_note.Content);

        if (found.Count == 0 && Links.Count == 0)
        {
            return;
        }

        Links = found;
        OnPropertyChanged(nameof(Links));
        OnPropertyChanged(nameof(HasLinks));
        OnPropertyChanged(nameof(ShowsLinkBar));
    }

    public NoteColor Color
    {
        get => _note.Color;
        set => Set(_note.Color, value, v => _note.Color = v);
    }

    public bool IsAlwaysOnTop
    {
        get => _note.IsAlwaysOnTop;
        set => Set(_note.IsAlwaysOnTop, value, v => _note.IsAlwaysOnTop = v);
    }

    public int X
    {
        get => _note.X;
        set => Set(_note.X, value, v => _note.X = v);
    }

    public int Y
    {
        get => _note.Y;
        set => Set(_note.Y, value, v => _note.Y = v);
    }

    public int Width
    {
        get => _note.Width;
        set => Set(_note.Width, value, v => _note.Width = v);
    }

    public int Height
    {
        get => _note.Height;
        set => Set(_note.Height, value, v => _note.Height = v);
    }

    /// <summary>The note's counter, if it has one.</summary>
    public NoteTimerViewModel? Timer { get; private set; }

    public bool HasTimer => Timer is not null;

    /// <summary>Gives this note a five-minute countdown, ready to start.</summary>
    [RelayCommand]
    public void AddTimer()
    {
        if (_note.Timer is not null)
        {
            return;
        }

        _note.Timer = new NoteTimer
        {
            Direction = TimerDirection.CountDown,
            Duration = TimeSpan.FromMinutes(5),
            Label = "Back in",
        };

        AttachTimer();
        AnnounceTimer();
        _autoSave.Schedule(_note);
    }

    [RelayCommand]
    public void RemoveTimer()
    {
        if (_note.Timer is null)
        {
            return;
        }

        Timer?.Dispose();
        Timer = null;
        _note.Timer = null;

        AnnounceTimer();
        _autoSave.Schedule(_note);
    }

    private void AttachTimer()
        => Timer = _note.Timer is null
            ? null
            : new NoteTimerViewModel(_note.Timer, _timeProvider, _ui, () => _autoSave.Schedule(_note));

    private void AnnounceTimer()
    {
        OnPropertyChanged(nameof(Timer));
        OnPropertyChanged(nameof(HasTimer));
    }

    private static readonly IReadOnlyList<NoteColor> Palette = Enum.GetValues<NoteColor>();

    /// <summary>
    /// The colours this note could be painted. An instance property because that
    /// is what a XAML ItemsSource can bind to; the list itself is shared.
    /// </summary>
    public IReadOnlyList<NoteColor> AvailableColors => Palette;

    /// <summary>Pins the note above other windows, or lets it back down.</summary>
    [RelayCommand]
    public void TogglePin() => IsAlwaysOnTop = !IsAlwaysOnTop;

    /// <summary>
    /// Repaints the note. Going through the property rather than the note means
    /// an unchanged colour still costs nothing.
    /// </summary>
    [RelayCommand]
    public void SetColor(NoteColor color) => Color = color;

    /// <summary>
    /// What the delete button does: the note is filed away, not destroyed, and
    /// its window goes. Anything typed a moment ago is written first.
    /// </summary>
    [RelayCommand]
    public async Task ArchiveAsync()
    {
        await _autoSave.FlushAsync(Id);
        await _notes.ArchiveAsync(Id);
        _windows.CloseNote(Id);
    }

    /// <summary>
    /// The window is closing. Writes whatever the debounce was still holding -
    /// cancelling it would lose the last sentence someone typed.
    /// </summary>
    public Task CloseAsync() => _autoSave.FlushAsync(Id);

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        assign(value);
        OnPropertyChanged(propertyName);
        _autoSave.Schedule(_note);
    }
}
