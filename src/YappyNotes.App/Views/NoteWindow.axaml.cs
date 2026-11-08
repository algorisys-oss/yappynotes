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
    private readonly NoteLinkEditor _linkEditor;
    private bool _keepEditorOpen;
    private (int Start, int End)? _toolbarSelection;

    internal NoteLinkEditor LinkEditor => _linkEditor;

    public NoteWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _linkEditor = new NoteLinkEditor(this.FindControl<Button>("LinkButton")!);

        var header = this.FindControl<Grid>("Header");
        if (header is not null)
        {
            header.PointerPressed += OnHeaderPressed;
        }

        SizeChanged += (_, _) => UpdateToolbarLayout();

        var grip = this.FindControl<Panel>("ResizeGrip");
        if (grip is not null)
        {
            grip.PointerPressed += OnResizeGripPressed;
        }

        var formatted = this.FindControl<FormattedNote>("Formatted");
        if (formatted is not null)
        {
            formatted.LinePressed += (_, e) => _ = _note?.PressAsync(e.Line, e.DrawnIndex);
            formatted.SelectionDragged += (_, e) => _note?.SelectPreviewText(
                e.AnchorLine, e.AnchorBoundary, e.FocusLine, e.FocusBoundary);
            formatted.SelectionCleared += (_, _) => _note?.ClearPreviewTextSelection();
        }

        // A click on empty paper clears the Preview selection without entering Edit.
        // Text clicks are handled by FormattedNote; links and tasks keep their actions.
        this.FindControl<ScrollViewer>("FormattedScroll")?.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    _note?.ClearPreviewTextSelection();
                }
            });

        var body = this.FindControl<TextBox>("Body");
        if (body is not null)
        {
            body.LostFocus += OnBodyLostFocus;
            body.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.SelectionStartProperty
                    || change.Property == TextBox.SelectionEndProperty
                    || change.Property == TextBox.CanUndoProperty
                    || change.Property == TextBox.CanRedoProperty)
                {
                    UpdateToolbarAvailability();
                }
            };

            // Tunnelling, because the TextBox takes Enter for a line break itself
            // before anything bubbling would hear it.
            body.AddHandler(KeyDownEvent, OnBodyKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        // A toolbar button can retain keyboard focus while Preview replaces the
        // editor. Enter must still open the note from anywhere in its chrome.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var editMode = this.FindControl<ToggleButton>("EditModeButton")!;
        var previewMode = this.FindControl<ToggleButton>("PreviewModeButton")!;
        foreach (var mode in new[] { editMode, previewMode })
        {
            // Focus leaves Body on pointer-down, before the button's command runs.
            // Let the mode command decide when editing ends; otherwise Preview can
            // become checked on focus loss and then toggle itself off on click.
            mode.AddHandler(PointerPressedEvent, (_, _) => _keepEditorOpen = true,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        editMode.Click += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _keepEditorOpen = false;
                SyncModeButtons();
                if (_note is { ShowsEditor: true } note && this.FindControl<TextBox>("Body") is { } editor)
                {
                    editor.Focus();
                    editor.CaretIndex = note.EditCaret;
                }
            });

        previewMode.Click += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _keepEditorOpen = false;
                SyncModeButtons();
                if (_note is { ShowsEditor: false })
                {
                    GetTopLevel(this)?.FocusManager?.Focus(null);
                }
            });

        var undo = this.FindControl<Button>("UndoButton")!;
        var redo = this.FindControl<Button>("RedoButton")!;
        var bold = this.FindControl<Button>("BoldButton")!;
        var italic = this.FindControl<Button>("ItalicButton")!;
        var heading = this.FindControl<ComboBox>("HeadingChoice")!;
        var link = this.FindControl<Button>("LinkButton")!;
        foreach (var control in new Control[] { undo, redo, bold, italic, heading, link })
        {
            control.AddHandler(PointerPressedEvent, (_, _) =>
            {
                _keepEditorOpen = true;
                // The ComboBox receives a second press when an item in its popup
                // is chosen. Keep the selection from the first press; by the
                // second one the TextBox may have collapsed it on losing focus.
                if (_toolbarSelection is null && this.FindControl<TextBox>("Body") is { } editor)
                {
                    _toolbarSelection = (editor.SelectionStart, editor.SelectionEnd);
                }

                UpdateToolbarAvailability();
            },
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        undo.Click += (_, _) => ApplyEditorUndoRedo(redo: false);
        redo.Click += (_, _) => ApplyEditorUndoRedo(redo: true);
        bold.Click += (_, _) => ApplyToolbarEmphasis(Emphasis.Bold);
        italic.Click += (_, _) => ApplyToolbarEmphasis(Emphasis.Italic);
        heading.SelectionChanged += (_, _) => ApplyToolbarHeading();
        heading.DropDownClosed += (_, _) =>
        {
            if (_toolbarSelection is { } selection)
            {
                RestoreEditorSelection(selection.Start, selection.End);
                _toolbarSelection = null;
            }

            _keepEditorOpen = false;
        };
        link.Click += OnLinkClicked;

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
            _note?.ClearPreviewTextSelection();

            // Clicking another window is the most common way to finish typing.
            if (!_keepEditorOpen)
            {
                _note?.EndEditing();
            }
        };
        MarkActive(false);
    }

    public NoteWindow(NoteViewModel note) : this() => Bind(note);

    public void Bind(NoteViewModel note)
    {
        ArgumentNullException.ThrowIfNull(note);

        if (_note is not null)
        {
            _note.ClearPreviewTextSelection();
            _note.PropertyChanged -= OnNoteChanged;
        }

        _note = note;
        DataContext = note;
        note.SetLinkEditor(_linkEditor);
        note.PropertyChanged += OnNoteChanged;
        SyncModeButtons();

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
            && _note is { IsEditing: true }
            && this.FindControl<TextBox>("Body") is { IsFocused: true } body)
        {
            e.Handled = true;
            ApplyEmphasis(body, e.Key == Key.B ? Emphasis.Bold : Emphasis.Italic);
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

        if (TodoList.ContinueOnEnter(body.Text ?? string.Empty, body.CaretIndex) is { } edit)
        {
            e.Handled = true;
            ApplyMarkdownEdit(body, edit);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None
            && _note is { ShowsEditor: false, IsAddingTodo: false } note)
        {
            e.Handled = true;
            note.BeginEditingAtEnd();
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
    /// Apply a Markdown edit through the TextBox so it shares Undo/Redo with typing.
    /// </summary>
    /// <remarks>
    /// Replace only the changed span. SelectedText records one native undo entry;
    /// replacing Text through the view-model binding does not. The binding still
    /// sends this edit to Content for autosave and preview.
    /// </remarks>
    private static void ApplyMarkdownEdit(TextBox body, MarkdownEdit edit)
    {
        var original = body.Text ?? string.Empty;
        if (original != edit.Content)
        {
            var prefix = 0;
            while (prefix < original.Length && prefix < edit.Content.Length
                && original[prefix] == edit.Content[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < original.Length - prefix && suffix < edit.Content.Length - prefix
                && original[original.Length - suffix - 1] == edit.Content[edit.Content.Length - suffix - 1])
            {
                suffix++;
            }

            body.SelectionStart = prefix;
            body.SelectionEnd = original.Length - suffix;
            body.SelectedText = edit.Content[prefix..(edit.Content.Length - suffix)];
        }

        body.CaretIndex = edit.SelectionEnd;
        body.SelectionStart = edit.SelectionStart;
        body.SelectionEnd = edit.SelectionEnd;
    }

    private static void ApplyEmphasis(TextBox body, Emphasis emphasis,
        int? selectionStart = null, int? selectionEnd = null)
    {
        ApplyMarkdownEdit(body, MarkdownEmphasis.Toggle(body.Text ?? string.Empty,
            selectionStart ?? body.SelectionStart, selectionEnd ?? body.SelectionEnd, emphasis));
    }

    private void ApplyEditorUndoRedo(bool redo)
    {
        if (_note is not { ShowsEditor: true } || this.FindControl<TextBox>("Body") is not { } body)
        {
            return;
        }

        if (redo)
        {
            body.Redo();
        }
        else
        {
            body.Undo();
        }

        _toolbarSelection = null;
        body.Focus();
        _keepEditorOpen = false;
        UpdateToolbarAvailability();
    }

    private void ApplyToolbarEmphasis(Emphasis emphasis)
    {
        if (_note is { ShowsEditor: false } preview)
        {
            preview.TogglePreviewEmphasis(emphasis);
            _toolbarSelection = null;
            _keepEditorOpen = false;
            return;
        }

        if (_note is not { ShowsEditor: true } note || this.FindControl<TextBox>("Body") is not { } body)
        {
            return;
        }

        if (!note.IsEditing)
        {
            note.ShowEdit();
        }

        var selection = _toolbarSelection ?? (body.SelectionStart, body.SelectionEnd);
        ApplyEmphasis(body, emphasis, selection.Item1, selection.Item2);
        _toolbarSelection = null;
        var start = body.SelectionStart;
        var end = body.SelectionEnd;
        RestoreEditorSelection(start, end);
        _keepEditorOpen = false;
    }

    private void ApplyToolbarHeading()
    {
        var choice = this.FindControl<ComboBox>("HeadingChoice")!;
        var level = choice.SelectedIndex;
        if (level >= 0 && _note is { ShowsEditor: false } preview)
        {
            preview.SetPreviewHeading(level);
            _toolbarSelection = null;
            choice.SelectedIndex = -1;
            _keepEditorOpen = false;
            return;
        }

        if (level < 0 || _note is not { ShowsEditor: true } note
            || this.FindControl<TextBox>("Body") is not { } body)
        {
            return;
        }

        if (!note.IsEditing)
        {
            note.ShowEdit();
        }

        var selection = _toolbarSelection ?? (body.SelectionStart, body.SelectionEnd);
        var edit = MarkdownHeading.SetLevel(body.Text ?? string.Empty,
            Math.Min(selection.Item1, selection.Item2), Math.Max(selection.Item1, selection.Item2), level);
        ApplyMarkdownEdit(body, edit);
        _toolbarSelection = null;
        choice.SelectedIndex = -1;
        RestoreEditorSelection(edit.SelectionStart, edit.SelectionEnd);
        _keepEditorOpen = false;
    }

    private async void OnLinkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_note is { ShowsEditor: false } preview)
        {
            _keepEditorOpen = true;
            try
            {
                await preview.EditPreviewLinkAsync();
            }
            finally
            {
                _toolbarSelection = null;
                _keepEditorOpen = false;
            }

            return;
        }

        if (_note is not { ShowsEditor: true } note || this.FindControl<TextBox>("Body") is not { } body)
        {
            return;
        }

        if (!note.IsEditing)
        {
            note.ShowEdit();
        }

        var selection = _toolbarSelection ?? (body.SelectionStart, body.SelectionEnd);
        var start = Math.Min(selection.Item1, selection.Item2);
        var end = Math.Max(selection.Item1, selection.Item2);
        var original = body.Text ?? string.Empty;
        _toolbarSelection = null;
        _keepEditorOpen = true;
        try
        {
            if (start != end && MarkdownLinks.Initial(original, start, end).Address.Length == 0)
            {
                var edit = MarkdownLinks.BeginAtSelection(original, start, end);
                ApplyMarkdownEdit(body, edit);
                (start, end) = (edit.SelectionStart, edit.SelectionEnd);
            }
            else
            {
                var answer = await _linkEditor.EditAsync(MarkdownLinks.Initial(original, start, end));
                if (answer is not null && _note == note && note.IsEditing && note.Content == original)
                {
                    var edit = MarkdownLinks.Apply(original, start, end, answer.Text, answer.Address);
                    ApplyMarkdownEdit(body, edit);
                    (start, end) = (edit.SelectionStart, edit.SelectionEnd);
                }
            }
        }
        finally
        {
            RestoreEditorSelection(start, end);
            _keepEditorOpen = false;
        }
    }

    private void RestoreEditorSelection(int start, int end)
    {
        if (this.FindControl<TextBox>("Body") is not { } body)
        {
            return;
        }

        body.Focus();
        body.CaretIndex = end;
        body.SelectionStart = start;
        body.SelectionEnd = end;
    }

    private void UpdateToolbarAvailability()
    {
        var body = this.FindControl<TextBox>("Body");
        var editing = _note is { ShowsEditor: true };
        var selection = _toolbarSelection ?? (body?.SelectionStart ?? 0, body?.SelectionEnd ?? 0);
        var content = _note?.Content ?? string.Empty;
        this.FindControl<Button>("UndoButton")!.IsEnabled = editing && body?.CanUndo == true;
        this.FindControl<Button>("RedoButton")!.IsEnabled = editing && body?.CanRedo == true;
        this.FindControl<Button>("BoldButton")!.IsEnabled = editing || _note?.CanPreviewBold == true;
        this.FindControl<Button>("ItalicButton")!.IsEnabled = editing || _note?.CanPreviewItalic == true;
        this.FindControl<ComboBox>("HeadingChoice")!.IsEnabled = editing
            ? MarkdownHeading.CanSetLevel(content, Math.Min(selection.Item1, selection.Item2),
                Math.Max(selection.Item1, selection.Item2))
            : _note?.CanPreviewHeading == true;
        this.FindControl<Button>("LinkButton")!.IsEnabled = editing || _note?.CanPreviewLink == true;
    }

    private void UpdateToolbarLayout()
    {
        var formatting = this.FindControl<ScrollViewer>("FormattingScroll")!;
        var narrow = ClientSize.Width < 280;
        Grid.SetRow(formatting, narrow ? 1 : 0);
        Grid.SetColumnSpan(formatting, narrow ? 2 : 1);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateToolbarLayout();

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

        _note?.ClearPreviewTextSelection();

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
        if (e.PropertyName == nameof(NoteViewModel.ShowsEditor))
        {
            SyncModeButtons();
        }

        if (e.PropertyName is nameof(NoteViewModel.ShowsEditor)
            or nameof(NoteViewModel.IsEditing) or nameof(NoteViewModel.Content)
            or nameof(NoteViewModel.PreviewTextSelection))
        {
            UpdateToolbarAvailability();
        }

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

    private void SyncModeButtons()
    {
        var editing = _note?.ShowsEditor == true;
        this.FindControl<ToggleButton>("EditModeButton")!.IsChecked = editing;
        this.FindControl<ToggleButton>("PreviewModeButton")!.IsChecked = !editing;
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

        if (_keepEditorOpen || this.FindControl<Button>("BoldButton")?.IsFocused == true
            || this.FindControl<Button>("ItalicButton")?.IsFocused == true
            || this.FindControl<ComboBox>("HeadingChoice")?.IsFocused == true
            || this.FindControl<Button>("LinkButton")?.IsFocused == true)
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

        _note?.ClearPreviewTextSelection();

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
            formatted.LinePressed += (_, e) => _ = _note?.PressAsync(e.Line, e.DrawnIndex);
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
