using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using YappyNotes.App.Views;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Tests;

/// <summary>
/// A note drawn as formatted text, and clicked into.
/// </summary>
/// <remarks>
/// Every click here lands on a character where it was actually drawn: the point
/// is asked of the line's own text layout. Clicking a fixed coordinate would test
/// the font instead of the note.
/// </remarks>
public class FormattedNoteWindowTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryNoteRepository _repository = new();
    private readonly CountingNoteRepository _counting;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private readonly FakeLinkLauncher _links = new();

    public FormattedNoteWindowTests()
    {
        _counting = new CountingNoteRepository(_repository);
        _notes = new NoteService(_counting, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, TimeSpan.FromMilliseconds(750));
    }

    private NoteWindow OpenWith(string content)
    {
        var note = _notes.CreateAsync().GetAwaiter().GetResult();
        note.Content = content;

        var window = new NoteWindow(new NoteViewModel(
            note, _notes, _autoSave, new FakeWindowManager(), _clock, new InlineUiDispatcher(), _links));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static NoteViewModel NoteOf(NoteWindow window) => (NoteViewModel)window.DataContext!;

    private static FormattedNote Formatted(NoteWindow window) => window.FindControl<FormattedNote>("Formatted")!;

    /// <summary>
    /// Asked of the scroll viewer holding the formatted note, not of the note
    /// itself: a hidden scroll viewer does not lay out its content, so the content
    /// still reports itself visible.
    /// </summary>
    private static bool ShowsFormatted(NoteWindow window) => window.FindControl<ScrollViewer>("FormattedScroll")!.IsVisible;

    private static TextBox Editor(NoteWindow window) => window.FindControl<TextBox>("Body")!;

    private static TextBlock Line(NoteWindow window, int index)
    {
        var lines = Formatted(window).Children.OfType<TextBlock>().ToList();
        Assert.True(lines.Count > index, $"the formatted note draws {lines.Count} lines, not {index + 1}");

        var line = lines[index];
        Assert.True(line.Bounds.Width > 0, "the line has not been laid out, so a click would land nowhere");
        return line;
    }

    private static string DrawnText(TextBlock line) => string.Concat(line.Inlines!.OfType<Run>().Select(run => run.Text));

    /// <summary>Presses the middle of the character drawn at this index of a line.</summary>
    private static void Press(NoteWindow window, TextBlock line, int drawnIndex)
    {
        var box = line.TextLayout.HitTestTextPosition(drawnIndex);
        var inLine = new Point(box.X + (box.Width / 4), box.Y + (box.Height / 2));
        var inWindow = line.TranslatePoint(inLine, window)!.Value;

        window.MouseDown(inWindow, MouseButton.Left);
        window.MouseUp(inWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Point InCharacter(NoteWindow window, TextBlock line, int index, bool trailing)
    {
        var box = line.TextLayout.HitTestTextPosition(index);
        return line.TranslatePoint(new Point(box.X + box.Width * (trailing ? 0.75 : 0.25),
            box.Y + box.Height / 2), window)!.Value;
    }

    private static void Drag(NoteWindow window, TextBlock fromLine, int fromIndex,
        TextBlock toLine, int toIndex)
    {
        var from = InCharacter(window, fromLine, fromIndex, trailing: false);
        var to = InCharacter(window, toLine, toIndex, trailing: true);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(NoteWindow window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void ModeButtons_OnAPinnedNote_SwitchInsideTheSameActiveWindow()
    {
        var window = OpenWith("milk");
        var note = NoteOf(window);
        note.IsAlwaysOnTop = true;
        window.MarkActive(true);
        Dispatcher.UIThread.RunJobs();

        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.True(note.IsEditing);
        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.True(Editor(window).IsFocused);
        Assert.False(ShowsFormatted(window));
        Assert.True(window.Topmost);
        Assert.True(window.IsActiveNote);

        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Assert.False(note.IsEditing);
        Assert.False(Editor(window).IsEffectivelyVisible);
        Assert.True(ShowsFormatted(window));
        Assert.True(window.Topmost);
        Assert.True(window.IsActiveNote);
    }

    [AvaloniaFact]
    public void ModeButtons_AfterSwitchingToPreview_KeepPreviewSelected()
    {
        var window = OpenWith("milk");
        var edit = window.FindControl<ToggleButton>("EditModeButton")!;
        var preview = window.FindControl<ToggleButton>("PreviewModeButton")!;
        Click(window, edit);
        Assert.True(edit.IsChecked);
        Assert.False(preview.IsChecked);

        Click(window, preview);

        Assert.False(edit.IsChecked);
        Assert.True(preview.IsChecked);
    }

    [AvaloniaFact]
    public void ModeButtons_WhenPreviewIsAlreadyActive_KeepItSelectedAfterAnotherClick()
    {
        var window = OpenWith("milk");
        var preview = window.FindControl<ToggleButton>("PreviewModeButton")!;
        Assert.True(preview.IsChecked);

        Click(window, preview);

        Assert.True(preview.IsChecked);
        Assert.False(NoteOf(window).ShowsEditor);
    }

    [AvaloniaFact]
    public void ModeButtons_AfterEditing_KeepTheLastCaretWhenReturning()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.CaretIndex = 4;

        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.Equal(4, editor.CaretIndex);
        Assert.True(editor.IsFocused);
    }

    [AvaloniaFact]
    public void ModeButtons_OnAnEmptyNote_DisablePreviewAndKeepTypingReady()
    {
        var window = OpenWith(string.Empty);
        var preview = window.FindControl<ToggleButton>("PreviewModeButton")!;

        Assert.False(preview.IsEnabled);
        Assert.True(Editor(window).IsFocused);
        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.False(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void ModeButtons_AtMinimumSize_LeaveTheBodyAndTitleDragStripUsable()
    {
        var window = OpenWith("milk");
        window.Width = 180;
        window.Height = 140;
        Dispatcher.UIThread.RunJobs();
        var header = window.FindControl<Grid>("Header")!;
        var bar = window.FindControl<Grid>("ModeBar")!;
        var preview = window.FindControl<ScrollViewer>("FormattedScroll")!;
        var headerBottom = header.TranslatePoint(new Point(0, header.Bounds.Height), window)!.Value.Y;
        var barTop = bar.TranslatePoint(new Point(0, 0), window)!.Value.Y;

        Assert.True(barTop >= headerBottom);
        Assert.True(bar.Bounds.Width < window.ClientSize.Width);
        Assert.True(preview.Viewport.Height > 0);
        Assert.True(header.Bounds.Width > bar.Bounds.Width);
    }

    [AvaloniaFact]
    public void ModeButtons_AfterPreview_EnterStillOpensTheEditor()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Assert.False(NoteOf(window).IsEditing, "Preview did not end editing");
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.True(Editor(window).IsFocused);
        Assert.Equal("milk".Length, Editor(window).CaretIndex);
    }

    [AvaloniaFact]
    public void ModeButtons_AfterPreview_CheckboxStillTicksInPlace()
    {
        var window = OpenWith("- [ ] milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Press(window, Line(window, 0), 0);

        Assert.Equal("- [x] milk", NoteOf(window).Content);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void ModeButtons_AfterPreview_LinkStillOpensInPlace()
    {
        var window = OpenWith("see [the docs](https://example.com/docs)");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Press(window, Line(window, 0), "see th".Length);

        Assert.Equal([new Uri("https://example.com/docs")], _links.Opened);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void ModeButtons_AfterPreview_ClickingTextKeepsPreviewUntilEditIsChosen()
    {
        const string content = "buy **cold** milk";
        var window = OpenWith(content);
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Press(window, Line(window, 0), "buy co".Length);

        Assert.True(ShowsFormatted(window));
        Assert.False(NoteOf(window).IsEditing);

        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.True(Editor(window).IsFocused);
        Assert.True(NoteOf(window).IsEditing);
    }

    [AvaloniaFact]
    public void ModeButtons_AfterEdit_EscapeStillReturnsToPreview()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void ModeButtons_AfterEdit_ClickingOutsideTheEditorStillShowsPreview()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        window.FindControl<ToggleButton>("PinButton")!.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void Toolbar_BoldButton_UsesTheSameMarkdownAsCtrlB()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Assert.True(window.FindControl<Button>("BoldButton")!.IsEffectivelyVisible);
        var editor = Editor(window);
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;

        Click(window, window.FindControl<Button>("BoldButton")!);

        Assert.Equal("buy **milk**", NoteOf(window).Content);
        Assert.Equal((6, 10), (editor.SelectionStart, editor.SelectionEnd));
        Assert.True(editor.IsFocused);
    }

    [AvaloniaFact]
    public void NativeUndoRedo_AfterTyping_RestoresBothVersions()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.CaretIndex = editor.Text!.Length;
        window.KeyTextInput("!");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk!", NoteOf(window).Content);

        editor.Undo();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("buy milk", NoteOf(window).Content);
        editor.Redo();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("buy milk!", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void NativeUndoRedo_AfterTypingAndSelectedTextReplacement_UsesOneHistory()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.CaretIndex = editor.Text!.Length;
        window.KeyTextInput("!");
        Dispatcher.UIThread.RunJobs();
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;

        editor.SelectedText = "**milk**";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy **milk**!", NoteOf(window).Content);

        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk!", NoteOf(window).Content);

        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk", NoteOf(window).Content);

        editor.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk!", NoteOf(window).Content);

        editor.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy **milk**!", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_BoldAfterTyping_UndoesAndRedoesInOrder()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.CaretIndex = editor.Text!.Length;
        window.KeyTextInput("!");
        Dispatcher.UIThread.RunJobs();
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;
        Click(window, window.FindControl<Button>("BoldButton")!);
        Assert.Equal("buy **milk**!", NoteOf(window).Content);

        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk!", NoteOf(window).Content);
        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk", NoteOf(window).Content);
        editor.Redo();
        Dispatcher.UIThread.RunJobs();
        editor.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy **milk**!", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_UndoRedoButtons_AfterBold_KeepTheEditorReady()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;
        Click(window, window.FindControl<Button>("BoldButton")!);

        var undo = window.FindControl<Button>("UndoButton")!;
        var redo = window.FindControl<Button>("RedoButton")!;
        Assert.True(undo.IsEnabled);
        Assert.False(redo.IsEnabled);
        Click(window, undo);
        Assert.Equal("buy milk", NoteOf(window).Content);
        Assert.True(editor.IsFocused);
        Assert.True(redo.IsEnabled);
        Click(window, redo);
        Assert.Equal("buy **milk**", NoteOf(window).Content);
        Assert.True(editor.IsFocused);
    }

    [AvaloniaFact]
    public void Toolbar_AtDefaultWidth_KeepsLinkAndModeButtonsOnTheSameRow()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var link = window.FindControl<Button>("LinkButton")!;
        var mode = window.FindControl<ToggleButton>("EditModeButton")!;
        var linkRight = link.TranslatePoint(new Point(link.Bounds.Width, 0), window)!.Value.X;
        var modeLeft = mode.TranslatePoint(new Point(0, 0), window)!.Value.X;

        Assert.True(linkRight <= modeLeft, $"Link ends at {linkRight}, while Edit starts at {modeLeft}");
    }

    [AvaloniaFact]
    public void Toolbar_HeadingAcrossTwoLines_UndoesAsOneStep()
    {
        var window = OpenWith("alpha\nbeta");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.SelectionStart = 1;
        editor.SelectionEnd = 8;
        var choice = window.FindControl<ComboBox>("HeadingChoice")!;
        Click(window, choice);
        Click(window, (ComboBoxItem)choice.Items[2]!);
        Assert.Equal("## alpha\n## beta", NoteOf(window).Content);

        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("alpha\nbeta", NoteOf(window).Content);
        editor.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("## alpha\n## beta", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_LinkWithSelectedText_UndoesAsOneStep()
    {
        var window = OpenWith("read docs");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.SelectionStart = 5;
        editor.SelectionEnd = 9;
        Click(window, window.FindControl<Button>("LinkButton")!);
        Assert.Equal("read [docs]()", NoteOf(window).Content);

        editor.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("read docs", NoteOf(window).Content);
        editor.Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("read [docs]()", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_BoldThenCtrlZAndCtrlY_UsesTheSameHistory()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;
        Click(window, window.FindControl<Button>("BoldButton")!);
        Assert.Equal("buy **milk**", NoteOf(window).Content);

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy milk", NoteOf(window).Content);
        window.KeyPress(Key.Y, RawInputModifiers.Control, PhysicalKey.Y, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("buy **milk**", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_EditingExistingLink_UndoesAndRedoesTheAddressChange()
    {
        var window = OpenWith("[docs](https://old.test)");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).CaretIndex = 2;
        Click(window, window.FindControl<Button>("LinkButton")!);
        window.LinkEditor.AddressInput.Text = "https://new.test";
        window.LinkEditor.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("[docs](https://new.test)", NoteOf(window).Content);

        Editor(window).Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("[docs](https://old.test)", NoteOf(window).Content);
        Editor(window).Redo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("[docs](https://new.test)", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Editor_ContinuingAList_UndoesWithTheSameButtons()
    {
        var window = OpenWith("- [ ] milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).CaretIndex = Editor(window).Text!.Length;

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("- [ ] milk\n- [ ] ", NoteOf(window).Content);

        Click(window, window.FindControl<Button>("UndoButton")!);
        Assert.Equal("- [ ] milk", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_InEdit_AppearsOppositeTheModeButtonsWithoutAnotherClick()
    {
        var window = OpenWith("milk");
        var edit = window.FindControl<ToggleButton>("EditModeButton")!;
        var previewTop = edit.TranslatePoint(new Point(0, 0), window)!.Value.Y;

        Click(window, edit);

        var bold = window.FindControl<Button>("BoldButton")!;
        Assert.True(bold.IsEffectivelyVisible);
        var editPosition = edit.TranslatePoint(new Point(0, 0), window)!.Value;
        var boldPosition = bold.TranslatePoint(new Point(0, 0), window)!.Value;
        Assert.True(editPosition.X > boldPosition.X + bold.Bounds.Width);
        Assert.InRange(Math.Abs(editPosition.Y - boldPosition.Y), 0, 3);
        Assert.InRange(Math.Abs(previewTop - editPosition.Y), 0, 1);
    }

    [AvaloniaFact]
    public void Toolbar_HeadingChoice_WithSelectionInTwoLines_StylesBoth()
    {
        var window = OpenWith("alpha\nbeta");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var body = Editor(window);
        body.SelectionStart = 0;
        body.SelectionEnd = 8;

        window.FindControl<ComboBox>("HeadingChoice")!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("## alpha\n## beta", NoteOf(window).Content);
        Assert.Equal((3, 14), (body.SelectionStart, body.SelectionEnd));
    }

    [AvaloniaFact]
    public void Toolbar_HeadingChoice_AfterMouseOpensIt_StylesAllSelectedLines()
    {
        var window = OpenWith("Valei-me!\nTestando\nEsse não");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var body = Editor(window);
        body.SelectionStart = 0;
        body.SelectionEnd = "Valei-me!\nTest".Length;

        var heading = window.FindControl<ComboBox>("HeadingChoice")!;
        Click(window, heading);
        Assert.True(heading.IsDropDownOpen);
        Click(window, (ComboBoxItem)heading.Items[4]!);

        Assert.Equal("#### Valei-me!\n#### Testando\nEsse não", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_LinkButton_WithSelection_WaitsForAddressInTheEditor()
    {
        var window = OpenWith("read docs");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var body = Editor(window);
        body.SelectionStart = 5;
        body.SelectionEnd = 9;

        Click(window, window.FindControl<Button>("LinkButton")!);

        Assert.Equal("read [docs]()", NoteOf(window).Content);
        Assert.Equal(12, body.CaretIndex);
        Assert.True(body.IsFocused);
        Assert.False(window.LinkEditor.IsOpen);
    }

    [AvaloniaFact]
    public void Toolbar_LinkButton_AfterStartingAddress_AcceptsTypingInPlace()
    {
        var window = OpenWith("read docs");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).SelectionStart = 5;
        Editor(window).SelectionEnd = 9;
        Click(window, window.FindControl<Button>("LinkButton")!);

        window.KeyTextInput("https://example.com");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("read [docs](https://example.com)", NoteOf(window).Content);
        Assert.True(Editor(window).IsFocused);
    }

    [AvaloniaFact]
    public void Toolbar_WithImageLineBeforeSelectedText_StylesTheSelectedLine()
    {
        const string image = "![imagem](file:///tmp/image.png)\n";
        var window = OpenWith(image + "Maravilhoso!");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).SelectionStart = image.Length;
        Editor(window).SelectionEnd = image.Length + "Maravilhoso!".Length;
        Assert.True(window.FindControl<ComboBox>("HeadingChoice")!.IsEnabled);
        Assert.True(window.FindControl<Button>("LinkButton")!.IsEnabled);

        window.FindControl<ComboBox>("HeadingChoice")!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(image + "## Maravilhoso!", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void Toolbar_WithSelectedText_KeepsStyleAndLinkAvailable()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;


        Assert.True(window.FindControl<ComboBox>("HeadingChoice")!.IsEnabled);
        Assert.True(window.FindControl<Button>("LinkButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void Toolbar_ItalicButton_UsesTheSameMarkdownAsCtrlI()
    {
        var window = OpenWith("buy milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.SelectionStart = 4;
        editor.SelectionEnd = 8;

        Click(window, window.FindControl<Button>("ItalicButton")!);

        Assert.Equal("buy *milk*", NoteOf(window).Content);
        Assert.Equal((5, 9), (editor.SelectionStart, editor.SelectionEnd));
    }

    [AvaloniaFact]
    public void Toolbar_HeadingChoice_ChangesOnlyTheCurrentBlock()
    {
        var window = OpenWith("milk\r\nbread");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        editor.CaretIndex = 2;

        window.FindControl<ComboBox>("HeadingChoice")!.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("## milk\r\nbread", NoteOf(window).Content);
        Assert.Equal(5, editor.CaretIndex);
    }

    [AvaloniaFact]
    public void Toolbar_WhenSwitchingModes_OffersPreviewFormattingWithoutChangingContent()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Assert.True(window.FindControl<Button>("BoldButton")!.IsEffectivelyVisible);

        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);
        Assert.True(window.FindControl<Button>("BoldButton")!.IsEffectivelyVisible);
        Assert.False(window.FindControl<Button>("BoldButton")!.IsEnabled);

        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.True(window.FindControl<Button>("BoldButton")!.IsEffectivelyVisible);
        Assert.Equal("milk", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public async Task PreviewToolbar_AfterDraggingAndClickingBold_RedrawsTheSelectedTextInOneSave()
    {
        var window = OpenWith("read docs");
        var before = _counting.Updates;
        Drag(window, Line(window, 0), 5, Line(window, 0), 8);
        var bold = window.FindControl<Button>("BoldButton")!;
        Assert.True(bold.IsEnabled);

        Click(window, bold);
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();

        Assert.Equal("read **docs**", NoteOf(window).Content);
        Assert.Equal("docs", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Assert.Equal("docs", string.Concat(Line(window, 0).Inlines!.OfType<Run>()
            .Where(run => run.Background is not null).Select(run => run.Text)));
        Assert.True(ShowsFormatted(window));
        Assert.False(NoteOf(window).IsEditing);
        Assert.Equal(before + 1, _counting.Updates);
    }

    [AvaloniaFact]
    public void PreviewToolbar_WhenDragIncludesSpaces_KeepsBoldEnabledAndTheWholeSelection()
    {
        var window = OpenWith("read docs now");
        Drag(window, Line(window, 0), 4, Line(window, 0), 9);
        var bold = window.FindControl<Button>("BoldButton")!;

        Assert.Equal(" docs ", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Assert.True(bold.IsEnabled);

        Click(window, bold);

        Assert.Equal("read **docs** now", NoteOf(window).Content);
        Assert.Equal(" docs ", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void PreviewToolbar_OnAWrappedParagraphAfterFormattedBlocks_EnablesAllApplicableCommands()
    {
        var paragraph = string.Join(' ', Enumerable.Repeat("teste", 80));
        var window = OpenWith("# Title\n\n**Intro**\n\n" + paragraph);
        window.Width = 540;
        Dispatcher.UIThread.RunJobs();
        var line = Line(window, NoteOf(window).Lines.Count - 1);

        Drag(window, line, 77, line, 107);

        Assert.NotNull(NoteOf(window).PreviewTextSelection);
        Assert.True(window.FindControl<Button>("BoldButton")!.IsEnabled);
        Assert.True(window.FindControl<Button>("ItalicButton")!.IsEnabled);
        Assert.True(window.FindControl<ComboBox>("HeadingChoice")!.IsEnabled);
        Assert.True(window.FindControl<Button>("LinkButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void PreviewToolbar_ItalicOnBoldText_RendersBothStylesAndKeepsTheHighlight()
    {
        var window = OpenWith("**milk**");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);

        Click(window, window.FindControl<Button>("ItalicButton")!);

        Assert.Equal("***milk***", NoteOf(window).Content);
        var selected = Assert.Single(Line(window, 0).Inlines!.OfType<Run>(),
            run => run.Background is not null);
        Assert.Equal("milk", selected.Text);
        Assert.Equal(FontWeight.Bold, selected.FontWeight);
        Assert.Equal(FontStyle.Italic, selected.FontStyle);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void PreviewToolbar_HeadingAcrossTwoLines_StylesBothAndKeepsTheirHighlight()
    {
        var window = OpenWith("one\ntwo");
        Drag(window, Line(window, 0), 0, Line(window, 1), 2);
        var choice = window.FindControl<ComboBox>("HeadingChoice")!;
        Assert.True(choice.IsEnabled);

        choice.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("## one\n## two", NoteOf(window).Content);
        Assert.Equal("one\ntwo", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Assert.Equal("one", string.Concat(Line(window, 0).Inlines!.OfType<Run>()
            .Where(run => run.Background is not null).Select(run => run.Text)));
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void PreviewToolbar_NormalStyle_RemovesAnExistingHeadingPrefix()
    {
        var window = OpenWith("## milk");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);

        window.FindControl<ComboBox>("HeadingChoice")!.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("milk", NoteOf(window).Content);
        Assert.Equal("milk", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void PreviewToolbar_LinkOnSelectedText_OpensTheAddressPromptAndKeepsTheLinkClickable()
    {
        var window = OpenWith("read docs");
        Drag(window, Line(window, 0), 5, Line(window, 0), 8);
        var link = window.FindControl<Button>("LinkButton")!;
        Assert.True(link.IsEnabled);

        Click(window, link);
        Assert.True(window.LinkEditor.IsOpen);
        Assert.Equal("docs", window.LinkEditor.TextInput.Text);
        window.LinkEditor.AddressInput.Text = "https://example.com";
        window.LinkEditor.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("read [docs](https://example.com)", NoteOf(window).Content);
        Assert.Equal("docs", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Assert.True(ShowsFormatted(window));
        Press(window, Line(window, 0), 6);
        Assert.Equal([new Uri("https://example.com")], _links.Opened);
    }

    [AvaloniaFact]
    public void PreviewToolbar_CancellingLink_KeepsContentAndHighlight()
    {
        var window = OpenWith("read docs");
        Drag(window, Line(window, 0), 5, Line(window, 0), 8);
        var selected = NoteOf(window).PreviewTextSelection;

        Click(window, window.FindControl<Button>("LinkButton")!);
        window.LinkEditor.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("read docs", NoteOf(window).Content);
        Assert.Same(selected, NoteOf(window).PreviewTextSelection);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void PreviewToolbar_OnExistingLink_EditsItsAddressWithoutDuplicatingTheLabel()
    {
        var window = OpenWith("[docs](https://old.test)");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);

        Click(window, window.FindControl<Button>("LinkButton")!);
        Assert.Equal("docs", window.LinkEditor.TextInput.Text);
        Assert.Equal("https://old.test", window.LinkEditor.AddressInput.Text);
        window.LinkEditor.AddressInput.Text = "https://new.test";
        window.LinkEditor.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("[docs](https://new.test)", NoteOf(window).Content);
        Assert.Equal("docs", NoteOf(window).PreviewTextSelection!.VisibleText(NoteOf(window).Content));
        Press(window, Line(window, 0), 1);
        Assert.Equal([new Uri("https://new.test")], _links.Opened);
    }

    [AvaloniaFact]
    public void PreviewToolbar_OnTaskAndCode_LeavesTheirExistingActionsIntact()
    {
        var window = OpenWith("- [ ] milk\nuse `code`");
        Drag(window, Line(window, 0), 2, Line(window, 0), 5);
        Assert.False(window.FindControl<ComboBox>("HeadingChoice")!.IsEnabled);
        Assert.True(window.FindControl<Button>("BoldButton")!.IsEnabled);
        Press(window, Line(window, 0), 0);
        Assert.Equal("- [x] milk\nuse `code`", NoteOf(window).Content);

        Drag(window, Line(window, 1), 4, Line(window, 1), 7);

        Assert.False(window.FindControl<Button>("BoldButton")!.IsEnabled);
        Assert.False(window.FindControl<Button>("LinkButton")!.IsEnabled);
        Assert.Null(NoteOf(window).PreviewTextSelection);
    }

    [AvaloniaFact]
    public void Toolbar_LinkButton_InsertsLinkAndRestoresTheEditorSelection()
    {
        var window = OpenWith("read ");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var body = Editor(window);
        body.CaretIndex = 5;

        Click(window, window.FindControl<Button>("LinkButton")!);
        Assert.True(window.LinkEditor.IsOpen);
        Assert.True(NoteOf(window).IsEditing);
        window.LinkEditor.TextInput.Text = "docs";
        window.LinkEditor.AddressInput.Text = "https://example.com";
        window.LinkEditor.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("read [docs](https://example.com)", NoteOf(window).Content);
        Assert.Equal((6, 10), (body.SelectionStart, body.SelectionEnd));
        Assert.True(body.IsFocused);
    }

    [AvaloniaFact]
    public void Toolbar_LinkButton_CancelKeepsTheMarkdownAndFocus()
    {
        var window = OpenWith("read docs");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var body = Editor(window);
        body.CaretIndex = 5;

        Click(window, window.FindControl<Button>("LinkButton")!);
        window.LinkEditor.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("read docs", NoteOf(window).Content);
        Assert.Equal((5, 5), (body.SelectionStart, body.SelectionEnd));
        Assert.True(body.IsFocused);
    }

    [AvaloniaFact]
    public void Toolbar_LinkButton_InvalidAddressKeepsTheMarkdown()
    {
        var window = OpenWith("read docs");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).CaretIndex = 5;

        Click(window, window.FindControl<Button>("LinkButton")!);
        window.LinkEditor.TextInput.Text = "docs";
        window.LinkEditor.AddressInput.Text = "file:///tmp/a";
        window.LinkEditor.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("read docs", NoteOf(window).Content);
        Assert.True(Editor(window).IsFocused);
    }

    [AvaloniaFact]
    public void Toolbar_LinkButton_ExistingLinkStartsWithItsTextAndAddress()
    {
        var window = OpenWith("[docs](https://old.test)");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).CaretIndex = 2;

        Click(window, window.FindControl<Button>("LinkButton")!);

        Assert.Equal("docs", window.LinkEditor.TextInput.Text);
        Assert.Equal("https://old.test", window.LinkEditor.AddressInput.Text);
    }

    [AvaloniaFact]
    public void Toolbar_AtMinimumSize_LeavesRoomForEditor()
    {
        var window = OpenWith("milk");
        window.Width = 180;
        window.Height = 140;
        Dispatcher.UIThread.RunJobs();
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.True(window.FindControl<Button>("BoldButton")!.IsEffectivelyVisible);
        Assert.True(window.FindControl<Button>("LinkButton")!.IsEffectivelyVisible);
        Assert.True(Editor(window).Bounds.Height > 0);
        Assert.True(window.FindControl<Grid>("Header")!.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public void Toolbar_OnANoteWithLinks_LeavesTheExistingLinkBarAvailable()
    {
        var window = OpenWith("https://example.com");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.True(window.FindControl<ItemsControl>("LinkBar")!.IsEffectivelyVisible);
        Assert.True(Editor(window).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Toolbar_AfterReturningToPreview_TasksStillTickInPlace()
    {
        var window = OpenWith("- [ ] milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Press(window, Line(window, 0), 0);

        Assert.Equal("- [x] milk", NoteOf(window).Content);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void Toolbar_OnATask_DisablesHeadingButAllowsLinkOnItsText()
    {
        var window = OpenWith("- [ ] milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Editor(window).CaretIndex = 7;
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.FindControl<ComboBox>("HeadingChoice")!.IsEnabled);
        Assert.True(window.FindControl<Button>("LinkButton")!.IsEnabled);
    }

    [AvaloniaFact]
    public void Toolbar_WhenEditorsPasteMenuOpens_KeepsTheEditorVisible()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);
        var inEditor = editor.TranslatePoint(new Point(10, 10), window)!.Value;

        window.MouseDown(inEditor, MouseButton.Right);
        window.MouseUp(inEditor, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.ContextFlyout is PopupFlyoutBase { IsOpen: true });
        Assert.True(editor.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void NoteWindow_ForANoteWithText_ShowsItFormattedRatherThanAsMarkdown()
    {
        var window = OpenWith("# Today\n- [ ] milk");

        Assert.True(ShowsFormatted(window));
        Assert.False(Editor(window).IsEffectivelyVisible);
        Assert.Equal("Today", DrawnText(Line(window, 0)));
        Assert.Equal("☐ milk", DrawnText(Line(window, 1)));
    }

    [AvaloniaFact]
    public void NoteWindow_ForANewNote_OpensReadyToType()
    {
        var window = OpenWith(string.Empty);

        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.False(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void FormattedNote_BoldText_IsDrawnBold()
    {
        var window = OpenWith("buy **cold** milk");

        var cold = Line(window, 0).Inlines!.OfType<Run>().Single(run => run.Text == "cold");

        Assert.Equal(FontWeight.Bold, cold.FontWeight);
    }

    [AvaloniaFact]
    public void FormattedNote_AHeading_IsDrawnLargerThanText()
    {
        var window = OpenWith("# Today\nmilk");

        Assert.True(Line(window, 0).FontSize > Line(window, 1).FontSize);
    }

    [AvaloniaFact]
    public void PressingAWord_ClearsTheSelectionAndKeepsPreview()
    {
        const string content = "# Today\n- buy **cold** milk";
        var window = OpenWith(content);
        Drag(window, Line(window, 1), "• buy ".Length, Line(window, 1), "• buy cold".Length - 1);
        Assert.NotNull(NoteOf(window).PreviewTextSelection);

        Press(window, Line(window, 1), "• buy co".Length);

        Assert.True(ShowsFormatted(window));
        Assert.False(Editor(window).IsEffectivelyVisible);
        Assert.Null(NoteOf(window).PreviewTextSelection);
        Assert.Equal(content, NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void PressingBlankPaper_ClearsTheSelectionAndKeepsPreview()
    {
        var window = OpenWith("milk bread");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);
        Assert.NotNull(NoteOf(window).PreviewTextSelection);

        Click(window, window.FindControl<ScrollViewer>("FormattedScroll")!);

        Assert.True(ShowsFormatted(window));
        Assert.Null(NoteOf(window).PreviewTextSelection);
    }

    [AvaloniaFact]
    public void PressingACheckBox_TicksItAndStaysFormatted()
    {
        var window = OpenWith("- [ ] milk");

        Press(window, Line(window, 0), 0);

        Assert.Equal("- [x] milk", NoteOf(window).Content);
        Assert.True(ShowsFormatted(window));
        Assert.Equal("☑ milk", DrawnText(Line(window, 0)));
    }

    [AvaloniaFact]
    public void PressingALink_OpensItAndStaysFormatted()
    {
        var window = OpenWith("see [the docs](https://example.com/docs)");

        Press(window, Line(window, 0), "see th".Length);

        Assert.Equal([new Uri("https://example.com/docs")], _links.Opened);
        Assert.True(ShowsFormatted(window));
    }

    [AvaloniaFact]
    public void DraggingPreviewText_HighlightsItWithoutOpeningTheEditor()
    {
        var window = OpenWith("milk bread");

        Drag(window, Line(window, 0), 0, Line(window, 0), 3);

        var selection = NoteOf(window).PreviewTextSelection;
        Assert.NotNull(selection);
        Assert.Equal((0, 4), (selection.Start, selection.End));
        Assert.False(NoteOf(window).ShowsEditor);
        Assert.Equal("milk bread", NoteOf(window).Content);
        Assert.Same(selection, Formatted(window).Selection);
        Assert.Equal("milk", string.Concat(Line(window, 0).Inlines!.OfType<Run>()
            .Where(run => run.Background is not null).Select(run => run.Text)));
    }

    [AvaloniaFact]
    public void DraggingPreviewTextAcrossLines_KeepsAReverseSelection()
    {
        var window = OpenWith("alpha\nbeta");

        Drag(window, Line(window, 1), 2, Line(window, 0), 1);

        var selection = NoteOf(window).PreviewTextSelection;
        Assert.NotNull(selection);
        Assert.Equal((8, 2), (selection.Anchor, selection.Focus));
        Assert.Equal([0, 1], selection.Blocks);
        Assert.False(NoteOf(window).ShowsEditor);
    }

    [AvaloniaFact]
    public void DraggingFromALink_SelectsWithoutOpeningItsAddress()
    {
        var window = OpenWith("see [docs](https://example.com)");

        Drag(window, Line(window, 0), 4, Line(window, 0), 7);

        Assert.Empty(_links.Opened);
        Assert.NotNull(NoteOf(window).PreviewTextSelection);
    }

    [AvaloniaFact]
    public void DraggingFromACheckbox_DoesNotToggleIt()
    {
        var window = OpenWith("- [ ] milk");

        Drag(window, Line(window, 0), 0, Line(window, 0), 5);

        Assert.Equal("- [ ] milk", NoteOf(window).Content);
        Assert.Null(NoteOf(window).PreviewTextSelection);
    }

    [AvaloniaFact]
    public void DraggingAcrossAWrappedVisualLine_SelectsOneSourceBlock()
    {
        var window = OpenWith("alpha beta gamma delta epsilon zeta eta theta iota");
        window.Width = 180;
        Dispatcher.UIThread.RunJobs();
        var line = Line(window, 0);
        Assert.True(line.TextLayout.HitTestTextPosition(0).Y < line.TextLayout.HitTestTextPosition(25).Y);

        Drag(window, line, 2, line, 25);

        var selection = NoteOf(window).PreviewTextSelection;
        Assert.NotNull(selection);
        Assert.Equal([0], selection.Blocks);
        Assert.Equal((2, 26), (selection.Start, selection.End));
    }

    [AvaloniaFact]
    public void DraggingAfterScrolling_MapsTheVisibleLinesToTheirSource()
    {
        var content = string.Join('\n', Enumerable.Range(0, 30).Select(i => $"line {i:00}"));
        var window = OpenWith(content);
        window.Height = 140;
        Dispatcher.UIThread.RunJobs();
        var scroll = window.FindControl<ScrollViewer>("FormattedScroll")!;
        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        var before = scroll.Offset.Y;

        Drag(window, Line(window, 28), 0, Line(window, 29), 6);

        var selection = NoteOf(window).PreviewTextSelection;
        Assert.NotNull(selection);
        Assert.Equal([28, 29], selection.Blocks);
        Assert.Equal(content.IndexOf("line 28", StringComparison.Ordinal), selection.Start);
        Assert.InRange(Math.Abs(before - scroll.Offset.Y), 0, 1);
    }

    [AvaloniaFact]
    public void PreviewSelection_AfterContentReplacement_DisappearsBeforeItCanBeUsed()
    {
        var window = OpenWith("milk bread");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);
        Assert.NotNull(NoteOf(window).PreviewTextSelection);

        NoteOf(window).Content = "changed";
        Dispatcher.UIThread.RunJobs();

        Assert.Null(NoteOf(window).PreviewTextSelection);
        Assert.Null(Formatted(window).Selection);
        Assert.DoesNotContain(Line(window, 0).Inlines!.OfType<Run>(), run => run.Background is not null);
    }

    [AvaloniaFact]
    public void PreviewSelection_WhenContentChangesDuringDrag_DoesNotReappearOnRelease()
    {
        var window = OpenWith("milk bread");
        var line = Line(window, 0);
        var from = InCharacter(window, line, 0, trailing: false);
        var to = InCharacter(window, line, 3, trailing: true);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        Assert.NotNull(NoteOf(window).PreviewTextSelection);

        NoteOf(window).Content = "changed";
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(to, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(NoteOf(window).PreviewTextSelection);
        Assert.Equal("changed", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void PreviewSelection_AfterClickingEdit_Disappears()
    {
        var window = OpenWith("milk bread");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);
        Assert.NotNull(NoteOf(window).PreviewTextSelection);

        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        Assert.Null(NoteOf(window).PreviewTextSelection);
        Assert.True(Editor(window).IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void PreviewSelection_AfterClickingAnUnrelatedControl_StillPointsAtTheSameText()
    {
        var window = OpenWith("milk bread");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);
        var original = NoteOf(window).PreviewTextSelection;

        Click(window, window.FindControl<Button>("ColorButton")!);

        Assert.Same(original, NoteOf(window).PreviewTextSelection);
        Assert.Equal("milk bread", NoteOf(window).Content);
    }

    [AvaloniaFact]
    public void PreviewSelection_AfterClickingTheActivePreviewControl_StillPointsAtTheSameText()
    {
        var window = OpenWith("milk bread");
        Drag(window, Line(window, 0), 0, Line(window, 0), 3);
        var original = NoteOf(window).PreviewTextSelection;

        Click(window, window.FindControl<ToggleButton>("PreviewModeButton")!);

        Assert.Same(original, NoteOf(window).PreviewTextSelection);
        Assert.False(NoteOf(window).ShowsEditor);
    }

    [AvaloniaFact]
    public void PressingBelowTheText_KeepsPreview()
    {
        var window = OpenWith("milk");
        var paper = window.FindControl<ScrollViewer>("FormattedScroll")!;
        var textEnds = Line(window, 0).TranslatePoint(new Point(0, Line(window, 0).Bounds.Height), paper)!.Value.Y;
        Assert.True(paper.Bounds.Height > textEnds + 20, "nothing below the text to press");
        var below = paper.TranslatePoint(new Point(40, paper.Bounds.Height - 10), window)!.Value;

        window.MouseDown(below, MouseButton.Left);
        window.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(ShowsFormatted(window));
        Assert.False(Editor(window).IsEffectivelyVisible);
    }

    /// <summary>
    /// A short note is not a scrolling note. Sized to its space plus its padding,
    /// the formatted text overflowed by the padding and every note scrolled.
    /// </summary>
    [AvaloniaFact]
    public void FormattedNote_ThatFits_DoesNotScroll()
    {
        var window = OpenWith("milk");
        var paper = window.FindControl<ScrollViewer>("FormattedScroll")!;

        Assert.True(paper.Extent.Height <= paper.Viewport.Height, $"extent {paper.Extent} is larger than viewport {paper.Viewport}");
    }

    [AvaloniaFact]
    public void Editor_LosingTheKeyboard_ShowsTheNoteFormattedAgain()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        Assert.True(Editor(window).IsEffectivelyVisible);

        window.FindControl<ToggleButton>("PinButton")!.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(ShowsFormatted(window));
    }

    /// <summary>
    /// Right-clicking to paste moves the keyboard into the editor's own menu. Taking
    /// that as "done editing" would swap the editor out from under the menu, and the
    /// paste would land nowhere.
    /// </summary>
    [AvaloniaFact]
    public void Editor_OpeningItsOwnMenu_KeepsEditing()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);
        var editor = Editor(window);

        // A real right-click, not ShowAt: only the click moves the keyboard into the
        // menu, and a test that opened the menu directly passed without the guard.
        var inEditor = editor.TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseDown(inEditor, MouseButton.Right);
        window.MouseUp(inEditor, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.ContextFlyout is PopupFlyoutBase { IsOpen: true }, "the right-click opened no menu");
        Assert.True(editor.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Escape_WhileEditing_ShowsTheNoteFormattedRatherThanClosingIt()
    {
        var window = OpenWith("milk");
        Click(window, window.FindControl<ToggleButton>("EditModeButton")!);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsVisible);
        Assert.True(ShowsFormatted(window));
    }

    /// <summary>The keyboard's way in, since pressing needs a pointer.</summary>
    [AvaloniaFact]
    public void Enter_OnAFormattedNote_EditsAtTheEnd()
    {
        var window = OpenWith("milk");
        window.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.Equal("milk".Length, Editor(window).CaretIndex);
    }

    /// <summary>
    /// The editor's own Cut/Copy/Paste menu used to be the only thing a right-click
    /// on a note's text could open, which hid the colours. Formatted text is not an
    /// editor, so the note's own menu is back.
    /// </summary>
    [AvaloniaFact]
    public void RightClickingFormattedText_OpensTheNotesOwnMenu()
    {
        var window = OpenWith("milk");
        var line = Line(window, 0);
        var onText = line.TranslatePoint(new Point(4, line.Bounds.Height / 2), window)!.Value;

        window.MouseDown(onText, MouseButton.Right);
        window.MouseUp(onText, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.FindControl<Border>("NoteChrome")!.ContextMenu!.IsOpen);
        Assert.True(ShowsFormatted(window));
    }

    private static TextBox EditingWithSelected(NoteWindow window, int start, int end)
    {
        NoteOf(window).BeginEditingAtEnd();
        Dispatcher.UIThread.RunJobs();

        var editor = Editor(window);
        Assert.True(editor.IsFocused, "the editor never took the keyboard, so no shortcut could reach it");
        editor.SelectionStart = start;
        editor.SelectionEnd = end;
        return editor;
    }

    [AvaloniaFact]
    public void CtrlB_WithAWordSelected_BoldsItAndKeepsTheWordSelected()
    {
        var window = OpenWith("buy milk now");
        var editor = EditingWithSelected(window, 4, 8);

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("buy **milk** now", editor.Text);
        Assert.Equal((6, 10), (editor.SelectionStart, editor.SelectionEnd));
    }

    [AvaloniaFact]
    public void CtrlI_WithAWordSelected_MakesItItalic()
    {
        var window = OpenWith("buy milk now");
        var editor = EditingWithSelected(window, 4, 8);

        window.KeyPress(Key.I, RawInputModifiers.Control, PhysicalKey.I, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("buy *milk* now", editor.Text);
    }

    [AvaloniaFact]
    public void CtrlB_PressedTwice_LeavesTheTextAsItWas()
    {
        var window = OpenWith("buy milk now");
        var editor = EditingWithSelected(window, 4, 8);

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("buy milk now", editor.Text);
        Assert.Equal((4, 8), (editor.SelectionStart, editor.SelectionEnd));
    }

    [AvaloniaTheory]
    [InlineData("@2026-09-16", DueState.Overdue)]
    [InlineData("@2026-09-17", DueState.Today)]
    [InlineData("@2026-09-18", DueState.Later)]
    public void FormattedNote_ADueDate_IsDrawnInTheColourOfHowSoonItIs(string date, DueState state)
    {
        var window = OpenWith($"- [ ] rent {date}");

        var drawn = Line(window, 0).Inlines!.OfType<Run>().Single(run => run.Text == date);

        Assert.Equal(DueBrushes.For(state), drawn.Foreground);
    }

    [AvaloniaFact]
    public void NewNote_TypingSeveralLetters_KeepsThemAllInTheEditor()
    {
        var window = OpenWith(string.Empty);
        var editor = Editor(window);
        Assert.True(editor.IsFocused, "a new note did not open with the keyboard in it");

        foreach (var letter in "Shop")
        {
            window.KeyTextInput(letter.ToString());
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsFocused, $"after typing '{letter}' the editor had lost the keyboard");
        }

        Assert.Equal("Shop", editor.Text);
        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.Equal(4, editor.CaretIndex);
    }
}
