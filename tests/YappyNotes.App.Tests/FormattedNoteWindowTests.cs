using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private readonly FakeLinkLauncher _links = new();

    public FormattedNoteWindowTests()
    {
        _notes = new NoteService(_repository, _clock);
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
    public void FormattedNote_AFencedBlock_ShowsCodeWithoutFences()
    {
        var window = OpenWith("before\n```cs\n  first();\n  second();\n```\nafter");

        var code = Assert.Single(Formatted(window).GetVisualDescendants().OfType<TextBlock>(),
            text => text.Tag is NoteLine { Block.Kind: MarkdownBlockKind.CodeFence });
        var drawn = !string.IsNullOrEmpty(code.Text) ? code.Text : DrawnText(code);

        Assert.Equal("  first();\n  second();\n", drawn);
    }

    [AvaloniaFact]
    public void FormattedNote_AFenceWithLanguage_ShowsItsLabel()
    {
        var window = OpenWith("```csharp\nvar x = 1;\n```");

        Assert.Contains(Formatted(window).GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == "csharp");
    }

    [AvaloniaFact]
    public async Task Copy_OnAFencedBlock_CopiesOnlyTheLiteralCode()
    {
        const string code = "  x = 1;\r\n\treturn x;\r\n";
        var window = OpenWith("before\n```csharp\r\n" + code + "```\nafter");
        var clipboard = window.Clipboard!;
        await clipboard.SetTextAsync("previous clipboard text");
        var copy = Assert.Single(Formatted(window).GetVisualDescendants().OfType<Button>(),
            button => button.Content is "Copy" or TextBlock { Text: "Copy" });
        var centre = copy.TranslatePoint(new Point(copy.Bounds.Width / 2, copy.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(code, await clipboard.TryGetTextAsync());
    }

    [AvaloniaFact]
    public void Copy_OnAnEmptyFencedBlock_IsDisabled()
    {
        var window = OpenWith("```\n```");

        var copy = Assert.Single(Formatted(window).GetVisualDescendants().OfType<Button>(),
            button => button.Content is "Copy" or TextBlock { Text: "Copy" });

        Assert.False(copy.IsEnabled);
    }

    [AvaloniaFact]
    public void FormattedNote_ALongCodeLine_DoesNotWrap()
    {
        var window = OpenWith("```\n" + new string('W', 200) + "\n```");
        var code = Assert.Single(Formatted(window).GetVisualDescendants().OfType<TextBlock>(),
            text => text.Tag is NoteLine { Block.Kind: MarkdownBlockKind.CodeFence });
        var scroll = code.GetVisualAncestors().OfType<ScrollViewer>().First();

        Assert.Contains("Mono", code.FontFamily.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TextWrapping.NoWrap, code.TextWrapping);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width,
            $"code extent {scroll.Extent.Width} should exceed viewport {scroll.Viewport.Width}");
    }

    [AvaloniaFact]
    public void PressingCodeText_OpensTheEditorAtTheClickedCharacter()
    {
        const string content = "before\n```cs\n  hello();\n```\nafter";
        var window = OpenWith(content);
        var code = Assert.Single(Formatted(window).GetVisualDescendants().OfType<TextBlock>(),
            text => text.Tag is NoteLine { Block.Kind: MarkdownBlockKind.CodeFence });

        Press(window, code, "  he".Length);

        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.Equal(content.IndexOf("hello", StringComparison.Ordinal) + 2, Editor(window).CaretIndex);
    }

    [AvaloniaFact]
    public async Task CopyButton_DoesNotOpenTheEditorOrChangeTheNote()
    {
        const string content = "```cs\nvar answer = 42;\n```";
        var repository = new CountingNoteRepository(new InMemoryNoteRepository());
        var notes = new NoteService(repository, _clock);
        var autoSave = new AutoSaveService(notes, _clock, TimeSpan.FromMilliseconds(750));
        var note = await notes.CreateAsync();
        note.Content = content;
        await notes.SaveAsync(note);
        var window = new NoteWindow(new NoteViewModel(
            note, notes, autoSave, new FakeWindowManager(), _clock, new InlineUiDispatcher(), _links));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var updatesBeforeClick = repository.Updates;
        var copy = Assert.Single(Formatted(window).GetVisualDescendants().OfType<Button>(),
            button => button.Content is "Copy" or TextBlock { Text: "Copy" });
        var centre = copy.TranslatePoint(new Point(copy.Bounds.Width / 2, copy.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await autoSave.FlushAllAsync();

        Assert.True(ShowsFormatted(window));
        Assert.False(Editor(window).IsEffectivelyVisible);
        Assert.Equal(content, NoteOf(window).Content);
        Assert.Equal(updatesBeforeClick, repository.Updates);
    }

    [AvaloniaFact]
    public void PressingAWord_OpensTheEditorWithTheCaretOnIt()
    {
        const string content = "# Today\n- buy **cold** milk";
        var window = OpenWith(content);

        Press(window, Line(window, 1), "• buy co".Length);

        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.Equal(content.IndexOf("ld", StringComparison.Ordinal), Editor(window).CaretIndex);
        Assert.True(Editor(window).IsFocused, "the editor opened without the keyboard in it");
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
    public void PressingBelowTheText_EditsAtTheEnd()
    {
        var window = OpenWith("milk");
        var paper = window.FindControl<ScrollViewer>("FormattedScroll")!;
        var textEnds = Line(window, 0).TranslatePoint(new Point(0, Line(window, 0).Bounds.Height), paper)!.Value.Y;
        Assert.True(paper.Bounds.Height > textEnds + 20, "nothing below the text to press");
        var below = paper.TranslatePoint(new Point(40, paper.Bounds.Height - 10), window)!.Value;

        window.MouseDown(below, MouseButton.Left);
        window.MouseUp(below, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(Editor(window).IsEffectivelyVisible);
        Assert.Equal("milk".Length, Editor(window).CaretIndex);
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
        Press(window, Line(window, 0), 1);

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
        Press(window, Line(window, 0), 1);
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
        Press(window, Line(window, 0), 1);

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
