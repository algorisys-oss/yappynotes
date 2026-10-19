using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using YappyNotes.App.Views;
using YappyNotes.Core;
using YappyNotes.TestKit;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Tests;

/// <summary>
/// Recolouring a note from a button you can see.
/// </summary>
/// <remarks>
/// The colours used to be reachable only from the note's context menu, and
/// right-clicking the body - most of the note - opens the text box's own
/// Cut/Copy/Paste menu instead. Reported from real use as "not very obvious".
/// </remarks>
public class NoteColourWindowTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 9, 17, 12, 0, 0, TimeSpan.Zero));
    private readonly CountingNoteRepository _counting = new(new InMemoryNoteRepository());
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;

    public NoteColourWindowTests()
    {
        _notes = new NoteService(_counting, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, Debounce);
    }

    private NoteWindow OpenWindow(NoteColor color)
    {
        var note = _notes.CreateAsync().GetAwaiter().GetResult();
        note.Color = color;

        var window = new NoteWindow(new NoteViewModel(note, _notes, _autoSave, new FakeWindowManager()));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// Opens the colour flyout and hands back its swatches. A flyout builds its
    /// content when it is shown, so nothing inside it exists before this.
    /// </summary>
    private static ListBox OpenSwatches(NoteWindow window)
    {
        var button = window.FindControl<Button>("ColorButton")
            ?? throw new InvalidOperationException("the note has no ColorButton");
        var flyout = button.Flyout
            ?? throw new InvalidOperationException("the ColorButton has no flyout");
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();

        var content = ((Flyout)flyout).Content as Control
            ?? throw new InvalidOperationException("the colour flyout has no content");

        return content.GetSelfAndLogicalDescendants().OfType<ListBox>().Single(c => c.Name == "ColorSwatches");
    }

    [AvaloniaFact]
    public void TitleStrip_OnAnyNote_HasAColourButtonWithAFlyout()
    {
        var window = OpenWindow(NoteColor.Yellow);

        var button = window.FindControl<Button>("ColorButton");

        Assert.NotNull(button);
        Assert.True(button.IsVisible);
        Assert.NotNull(button.Flyout);
        Assert.Contains(button, window.FindControl<Grid>("Header")!.GetVisualDescendants());
    }

    [AvaloniaFact]
    public void ColourFlyout_WhenOpened_OffersEveryColour()
    {
        var window = OpenWindow(NoteColor.Yellow);

        var swatches = OpenSwatches(window);

        Assert.Equal(Enum.GetValues<NoteColor>(), swatches.Items.Cast<NoteColor>());
    }

    [AvaloniaFact]
    public void ColourFlyout_WhenOpened_MarksTheColourTheNoteIs()
    {
        var window = OpenWindow(NoteColor.Pink);

        var swatches = OpenSwatches(window);

        Assert.Equal(NoteColor.Pink, swatches.SelectedItem);
    }

    [AvaloniaFact]
    public void ColourFlyout_PickingAColour_RepaintsTheNote()
    {
        var window = OpenWindow(NoteColor.Yellow);
        var swatches = OpenSwatches(window);

        swatches.SelectedItem = NoteColor.Blue;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(NoteColor.Blue, ((NoteViewModel)window.DataContext!).Color);
        Assert.Equal(
            ((SolidColorBrush)NoteBrushes.Paper.Convert(NoteColor.Blue, typeof(IBrush), null, null!)).Color,
            ((SolidColorBrush)window.Background!).Color);
    }

    /// <summary>
    /// The swatches are bound two-way to the note's colour, so the binding writing
    /// its own value back as the flyout opens is exactly the kind of thing that
    /// would rewrite a note nobody changed.
    /// </summary>
    [AvaloniaFact]
    public async Task ColourFlyout_OpenedAndClosedWithoutAPick_WritesNothing()
    {
        var window = OpenWindow(NoteColor.Green);
        var writesBefore = _counting.Updates;

        OpenSwatches(window);
        window.FindControl<Button>("ColorButton")!.Flyout!.Hide();
        Dispatcher.UIThread.RunJobs();
        await _autoSave.FlushAllAsync();

        Assert.Equal(writesBefore, _counting.Updates);
    }

    [AvaloniaFact]
    public void CodePaper_ForEveryNoteColor_IsFifteenPercentDarkerThanPaper()
    {
        var expectedPaper = new Dictionary<NoteColor, Color>
        {
            [NoteColor.Yellow] = Color.FromRgb(0xFF, 0xF3, 0x9A),
            [NoteColor.Green] = Color.FromRgb(0xC9, 0xEF, 0xB2),
            [NoteColor.Blue] = Color.FromRgb(0xB6, 0xDC, 0xF7),
            [NoteColor.Pink] = Color.FromRgb(0xFB, 0xC6, 0xDB),
            [NoteColor.Purple] = Color.FromRgb(0xDA, 0xC3, 0xF0),
            [NoteColor.Grey] = Color.FromRgb(0xE3, 0xE3, 0xE3),
        };

        Assert.Equal(Enum.GetValues<NoteColor>(), expectedPaper.Keys);
        foreach (var (noteColor, paperColor) in expectedPaper)
        {
            var paper = (SolidColorBrush)NoteBrushes.Paper.Convert(noteColor, typeof(IBrush), null, null!);
            var edge = (SolidColorBrush)NoteBrushes.Edge.Convert(noteColor, typeof(IBrush), null, null!);
            var code = (SolidColorBrush)NoteBrushes.CodePaper.Convert(noteColor, typeof(IBrush), null, null!);

            Assert.Equal(paperColor, paper.Color);
            Assert.Equal(Color.FromRgb(
                (byte)(paperColor.R * 0.88),
                (byte)(paperColor.G * 0.88),
                (byte)(paperColor.B * 0.88)), edge.Color);
            Assert.Equal((byte)(paperColor.R * 0.85), code.Color.R);
            Assert.Equal((byte)(paperColor.G * 0.85), code.Color.G);
            Assert.Equal((byte)(paperColor.B * 0.85), code.Color.B);
        }
    }

    [AvaloniaFact]
    public void CodeBlock_WhenNoteColorChanges_RepaintsFromCurrentPaper()
    {
        const string content = "```cs\nvar answer = 42;\n```";
        var note = _notes.CreateAsync().GetAwaiter().GetResult();
        note.Content = content;
        note.Color = NoteColor.Yellow;
        var viewModel = new NoteViewModel(note, _notes, _autoSave, new FakeWindowManager());
        var window = new NoteWindow(viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var code = Assert.Single(window.FindControl<FormattedNote>("Formatted")!
            .GetVisualDescendants().OfType<Border>(),
            border => border.Tag is NoteLine { Block.Kind: MarkdownBlockKind.CodeFence });
        var yellowPaper = ((SolidColorBrush)window.Background!).Color;
        var yellowCode = ((SolidColorBrush)code.Background!).Color;

        viewModel.SetColor(NoteColor.Blue);
        Dispatcher.UIThread.RunJobs();

        var bluePaper = ((SolidColorBrush)window.Background!).Color;
        var blueCode = ((SolidColorBrush)code.Background!).Color;
        Assert.Equal(Color.FromRgb(
            (byte)(yellowPaper.R * 0.85),
            (byte)(yellowPaper.G * 0.85),
            (byte)(yellowPaper.B * 0.85)), yellowCode);
        Assert.Equal(Color.FromRgb(
            (byte)(bluePaper.R * 0.85),
            (byte)(bluePaper.G * 0.85),
            (byte)(bluePaper.B * 0.85)), blueCode);
        Assert.NotEqual(yellowCode, blueCode);
        Assert.Equal(content, viewModel.Content);
    }

    [AvaloniaFact]
    public void CodeBlock_WhenAppThemeIsDark_StillFollowsLightNotePaper()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        try
        {
            var note = _notes.CreateAsync().GetAwaiter().GetResult();
            note.Content = "```\ncode\n```";
            note.Color = NoteColor.Purple;
            var window = new NoteWindow(new NoteViewModel(note, _notes, _autoSave, new FakeWindowManager()));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var code = Assert.Single(window.FindControl<FormattedNote>("Formatted")!
                .GetVisualDescendants().OfType<Border>(),
                border => border.Tag is NoteLine { Block.Kind: MarkdownBlockKind.CodeFence });
            var paper = ((SolidColorBrush)window.Background!).Color;
            var codePaper = ((SolidColorBrush)code.Background!).Color;

            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            Assert.Equal(Color.FromRgb(0xDA, 0xC3, 0xF0), paper);
            Assert.Equal(Color.FromRgb(
                (byte)(paper.R * 0.85),
                (byte)(paper.G * 0.85),
                (byte)(paper.B * 0.85)), codePaper);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
