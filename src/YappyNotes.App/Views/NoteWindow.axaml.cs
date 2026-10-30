using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Views;

/// <summary>
/// One sticky note on the desktop: a borderless window that remembers where it
/// was put.
/// </summary>
public partial class NoteWindow : Window
{
    private NoteViewModel? _note;

    /// <summary>
    /// True while the window is applying the view-model's geometry to itself.
    /// Without it, restoring a note's saved position raises PositionChanged,
    /// which writes the position back, which schedules a save - so opening a note
    /// you never touched would rewrite it.
    /// </summary>
    private bool _applyingGeometry;

    private bool _closeConfirmed;

    public NoteWindow()
    {
        AvaloniaXamlLoader.Load(this);

        var header = this.FindControl<Grid>("Header");
        if (header is not null)
        {
            header.PointerPressed += OnHeaderPressed;
        }

        var grip = this.FindControl<Panel>("ResizeGrip");
        if (grip is not null)
        {
            grip.PointerPressed += OnResizeGripPressed;
        }

        var formatted = this.FindControl<FormattedNote>("Formatted");
        if (formatted is not null)
        {
            formatted.LinePressed += (_, e) => _ = _note?.PressAsync(e.Line, e.DrawnIndex, e.Trailing);
        }

        // Presses on the paper around the text: a line would have handled its own.
        this.FindControl<ScrollViewer>("FormattedScroll")?.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    _note?.BeginEditingAtEnd();
                }
            });

        var body = this.FindControl<TextBox>("Body");
        if (body is not null)
        {
            body.LostFocus += OnBodyLostFocus;

            // Tunnelling, because the TextBox takes Enter for a line break itself
            // before anything bubbling would hear it.
            body.AddHandler(KeyDownEvent, OnBodyKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        this.FindControl<ToggleButton>("EditModeButton")!.Click += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_note is { ShowsEditor: true } note && this.FindControl<TextBox>("Body") is { } editor)
                {
                    editor.Focus();
                    editor.CaretIndex = note.EditCaret;
                }
            });

        this.FindControl<ToggleButton>("PreviewModeButton")!.Click += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_note is { ShowsEditor: false })
                {
                    GetTopLevel(this)?.FocusManager?.Focus(null);
                }
            });

        var adder = this.FindControl<TextBox>("TodoAdder");
        if (adder is not null)
        {
            adder.AddHandler(KeyDownEvent, OnAdderKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            adder.LostFocus += (_, _) => _note?.StopAddingTodos();
        }

        var titleBox = this.FindControl<TextBox>("TitleBox");
        if (titleBox is not null)
        {
            // Editing ends when the title loses focus, and the strip goes back
            // to being something you can pick the window up by.
            titleBox.LostFocus += (_, _) => titleBox.IsHitTestVisible = false;
        }

        // Window exposes no styled property for its position, so this is the
        // only way to hear about a drag finishing somewhere new.
        PositionChanged += OnPositionChanged;

        // Which note the keystrokes are going into. With several open there was
        // nothing saying so.
        Activated += (_, _) => MarkActive(true);
        Deactivated += (_, _) =>
        {
            MarkActive(false);

            // Clicking another window is the most common way to finish typing.
            _note?.EndEditing();
        };
        MarkActive(false);
    }

    public NoteWindow(NoteViewModel note) : this() => Bind(note);

    public void Bind(NoteViewModel note)
    {
        ArgumentNullException.ThrowIfNull(note);

        if (_note is not null)
        {
            _note.PropertyChanged -= OnNoteChanged;
        }

        _note = note;
        DataContext = note;
        note.PropertyChanged += OnNoteChanged;

        _applyingGeometry = true;
        try
        {
            Width = note.Width;
            Height = note.Height;
            Position = new Avalonia.PixelPoint(note.X, note.Y);
            // Topmost is bound in XAML, so pinning follows the view-model rather
            // than being frozen at whatever it was when the window opened.
        }
        finally
        {
            _applyingGeometry = false;
        }
    }

    /// <summary>
    /// Ctrl+W and Escape close the window and keep the note. Handled here rather
    /// than as a KeyBinding because closing is the window's business, not the
    /// view-model's - the view-model has no idea a window exists.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.B or Key.I
            && _note is { IsEditing: true } typing
            && this.FindControl<TextBox>("Body") is { IsFocused: true } body)
        {
            e.Handled = true;
            ApplyEmphasis(typing, body, e.Key == Key.B ? Emphasis.Bold : Emphasis.Italic);
            return;
        }

        // Escape while typing puts the note back to formatted; a second one closes
        // it. Closing a note mid-sentence on the key that also means "stop
        // editing" would be a surprise every time.
        if (e.Key == Key.Escape && _note is { IsEditing: true, ShowsEditor: true } editing
            && !string.IsNullOrWhiteSpace(editing.Content))
        {
            e.Handled = true;
            editing.EndEditing();
            Focus();
            return;
        }

        if (e.Key == Key.Enter && _note is { ShowsEditor: false } resting)
        {
            e.Handled = true;
            resting.BeginEditingAtEnd();
            return;
        }

        if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// Enter on a list line continues the list; Shift+Enter, or Enter anywhere else,
    /// is the editor's own line break.
    /// </summary>
    private void OnBodyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None
            || sender is not TextBox body || _note is null || body.SelectionStart != body.SelectionEnd)
        {
            return;
        }

        if (_note.ContinueListOnEnter(body.CaretIndex) is { } caret)
        {
            e.Handled = true;
            body.CaretIndex = caret;
        }
    }

    /// <summary>
    /// Enter adds the to-do and leaves the keyboard in the field for the next one.
    /// Escape empties and leaves the field - handled here, or it would reach the
    /// window and close the note.
    /// </summary>
    private void OnAdderKeyDown(object? sender, KeyEventArgs e)
    {
        if (_note is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _note.AddTodoCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _note.CancelAddingTodo();

            // Not Focus() on the window, which leaves the keyboard where it was.
            // Avalonia 12 has no ClearFocus; focusing nothing is how it is said.
            GetTopLevel(this)?.FocusManager?.Focus(null);
        }
    }

    /// <summary>
    /// Ctrl+B or Ctrl+I: the view-model rewrites the Markdown, and the same text is
    /// selected again afterwards so a second shortcut - or a second press to undo
    /// the first - acts on it.
    /// </summary>
    /// <remarks>
    /// The selection is put back after the new text has reached the editor. The
    /// binding writes the text synchronously when the view-model's content changes,
    /// and replacing a TextBox's text resets its selection, so setting it any earlier
    /// is undone.
    /// </remarks>
    private static void ApplyEmphasis(NoteViewModel note, TextBox body, Emphasis emphasis)
    {
        var (start, end) = note.ToggleEmphasis(emphasis, body.SelectionStart, body.SelectionEnd);


        // Caret first: in Avalonia 12, setting it clears the selection, and set last
        // it left the second press with nothing selected to unbold.
        body.CaretIndex = end;
        body.SelectionStart = start;
        body.SelectionEnd = end;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // A new note should be ready to type into without a click.
        this.FindControl<TextBox>("Body")?.Focus();
    }

    /// <summary>
    /// Whether a close for this reason is called off until the note is flushed.
    /// </summary>
    /// <remarks>
    /// Only when the reader closes the note. When the app is ending, the flush has
    /// already happened - <c>ShutdownRequested</c> is raised before any window is
    /// asked to close, and the app disposes the autosave there. Holding the close
    /// up anyway makes <c>TryShutdown</c> give up, and since the app lives in the
    /// tray nothing else would end it. It gets away with that today only because
    /// the second flush finds nothing pending and finishes synchronously, closing
    /// the window again before <c>TryShutdown</c> has looked. A shutdown should not
    /// rest on that.
    /// </remarks>
    public static bool HoldsCloseToFlush(WindowCloseReason reason)
        => reason is not (WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_closeConfirmed || _note is null || !HoldsCloseToFlush(e.CloseReason))
        {
            return;
        }

        // Closing is synchronous and flushing is not, so the close is called off
        // and repeated once whatever the debounce was holding has been written.
        // Letting it through first would lose the last sentence typed.
        e.Cancel = true;
        _ = FlushThenCloseAsync(_note);
    }

    private async Task FlushThenCloseAsync(NoteViewModel note)
    {
        await note.CloseAsync();

        _closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// Opening the editor has to put the keyboard in it, at the caret the press
    /// asked for. Posted rather than done here, because the editor only becomes
    /// visible once the binding has heard the same change, and a hidden control
    /// cannot take focus.
    /// </summary>
    private void OnNoteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.IsAddingTodo) && _note is { IsAddingTodo: true })
        {
            // The new line has only just become visible, and a hidden control cannot
            // take the keyboard.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("TodoAdder")?.Focus());
            return;
        }

        if (e.PropertyName != nameof(NoteViewModel.IsEditing) || _note is not { IsEditing: true } note)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var body = this.FindControl<TextBox>("Body");
            if (body is null || !note.IsEditing)
            {
                return;
            }

            // Already typing in it - a new note that has just become "editing" -
            // and the caret is where the typing put it. Moving it would jump it.
            if (body.IsFocused)
            {
                return;
            }

            body.Focus();
            body.CaretIndex = note.EditCaret;
            body.SelectionStart = body.SelectionEnd = note.EditCaret;
        });
    }

    /// <summary>
    /// Leaving the editor shows the note formatted again - unless the keyboard only
    /// went into the editor's own Cut/Copy/Paste menu, which would otherwise swap the
    /// editor out from under the paste.
    /// </summary>
    private void OnBodyLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox editor)
        {
            _note?.RememberEditCaret(editor.CaretIndex);
        }

        if (sender is TextBox { ContextFlyout: Avalonia.Controls.Primitives.PopupFlyoutBase { IsOpen: true } })
        {
            return;
        }

        // The mode button's command performs the switch after it gets focus.
        // Ending the edit here would swap the view before the click is processed.
        if (this.FindControl<ToggleButton>("EditModeButton")?.IsFocused == true
            || this.FindControl<ToggleButton>("PreviewModeButton")?.IsFocused == true)
        {
            return;
        }

        _note?.EndEditing();
    }

    /// <summary>Whether this is the note with the keyboard.</summary>
    public bool IsActiveNote { get; private set; }

    /// <summary>
    /// Marks this note as the active one, or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the edge changes. The paper keeps its colour - a note is paper, not a
    /// form, and repainting it on focus would turn the desktop into a flicker.
    /// A darker, slightly thicker border reads at a glance without asking to be
    /// looked at.
    /// </para>
    /// <para>
    /// Purely visual: nothing here touches the note, so clicking between windows
    /// costs no disk write. <c>FocusedNoteTests</c> holds both of those.
    /// </para>
    /// </remarks>
    public void MarkActive(bool active)
    {
        IsActiveNote = active;

        var chrome = this.FindControl<Border>("NoteChrome");
        if (chrome is null)
        {
            return;
        }

        chrome.BorderBrush = active ? ActiveEdge : RestingEdge;
        chrome.BorderThickness = new Thickness(active ? 2 : 1);
    }

    private static readonly IBrush ActiveEdge = new SolidColorBrush(Color.FromArgb(0xBB, 0x00, 0x00, 0x00));
    private static readonly IBrush RestingEdge = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00));

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    /// <summary>
    /// The strip is how you pick a note up, and - on a double-click - how you
    /// rename it.
    /// </summary>
    /// <remarks>
    /// The buttons in the strip mark their own presses handled, and this is
    /// attached for unhandled events only, so pressing one never starts a drag.
    /// Checking <c>e.Source</c> for a Button or a TextBox would not have worked:
    /// the source is the template part under the pointer - a ContentPresenter, a
    /// TextPresenter - and never the control itself.
    /// </remarks>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            BeginEditingTitle();
            return;
        }

        BeginMoveDrag(e);
    }

    private void BeginEditingTitle()
    {
        var formatted = this.FindControl<FormattedNote>("Formatted");
        if (formatted is not null)
        {
            formatted.LinePressed += (_, e) => _ = _note?.PressAsync(e.Line, e.DrawnIndex, e.Trailing);
        }

        // Presses on the paper around the text: a line would have handled its own.
        this.FindControl<ScrollViewer>("FormattedScroll")?.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    _note?.BeginEditingAtEnd();
                }
            });

        var body = this.FindControl<TextBox>("Body");
        if (body is not null)
        {
            body.LostFocus += OnBodyLostFocus;
        }

        var titleBox = this.FindControl<TextBox>("TitleBox");
        if (titleBox is null)
        {
            return;
        }

        titleBox.IsHitTestVisible = true;
        titleBox.Focus();
        titleBox.SelectAll();
    }

    /// <summary>
    /// The corner grip. A window with no decorations gets no resize handles from
    /// the OS, so without this a note is stuck at whatever size it was created.
    /// </summary>
    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginResizeDrag(WindowEdge.SouthEast, e);
        }
    }

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_note is null || _applyingGeometry)
        {
            return;
        }

        _note.X = e.Point.X;
        _note.Y = e.Point.Y;
    }

    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_note is null || _applyingGeometry)
        {
            return;
        }

        if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            _note.Width = (int)Width;
            _note.Height = (int)Height;
        }
    }
}
