using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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

public class NoteImageRenderingTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly InMemoryNoteRepository _repository = new();
    private readonly CountingNoteRepository _counting;
    private readonly NoteService _notes;
    private readonly AutoSaveService _autoSave;
    private readonly FakeLinkLauncher _links = new();

    public NoteImageRenderingTests()
    {
        _counting = new CountingNoteRepository(_repository);
        _notes = new NoteService(_counting, _clock);
        _autoSave = new AutoSaveService(_notes, _clock, TimeSpan.FromMilliseconds(750));
    }

    private static Uri Sample => new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "sample.png"));
    private static string Reference(Uri uri, string description = "diagram") => $"![{description}]({uri.AbsoluteUri})";

    private NoteWindow Open(string content)
    {
        var stored = _notes.CreateAsync().GetAwaiter().GetResult();
        stored.Content = content;
        var window = new NoteWindow(new NoteViewModel(
            stored, _notes, _autoSave, new FakeWindowManager(), _clock, new InlineUiDispatcher(), _links));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static FormattedNote Formatted(NoteWindow window) => window.FindControl<FormattedNote>("Formatted")!;
    private static Border ImageRow(NoteWindow window)
        => Assert.Single(Formatted(window).Children.OfType<Border>(), row => row.Tag is NoteLine { Block.Kind: MarkdownBlockKind.Image });

    private static async Task WaitForImage(NoteWindow window, Uri uri)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            if (window.ImageSession?.Get(uri)?.Status is ImageLoadStatus.Ready or ImageLoadStatus.Missing or ImageLoadStatus.Invalid)
            {
                Dispatcher.UIThread.RunJobs();
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("image result was not published");
    }

    private static void Click(NoteWindow window, Border row)
    {
        var point = row.TranslatePoint(new Point(Math.Min(10, row.Bounds.Width / 2), row.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickText(NoteWindow window, TextBlock line, int index)
    {
        var hit = line.TextLayout.HitTestTextPosition(index);
        var inWindow = line.TranslatePoint(new Point(hit.X + hit.Width / 4, hit.Y + hit.Height / 2), window)!.Value;
        window.MouseDown(inWindow, MouseButton.Left);
        window.MouseUp(inWindow, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(320, 200, 1000, 1000, 320, 200)]
    [InlineData(320, 200, 160, 100, 160, 100)]
    [InlineData(10_000, 5_000, 12_000, 12_000, 4_096, 2_048)]
    public void FormattedNote_ImageDisplaySize_RespectsNaturalViewportAndTechnicalLimit(
        int naturalWidth, int naturalHeight, int viewportWidth, int viewportHeight, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new Size(expectedWidth, expectedHeight),
            FormattedNote.ImageDisplaySize(new PixelSize(naturalWidth, naturalHeight),
                new Size(viewportWidth, viewportHeight)));
    }

    [AvaloniaFact]
    public void FormattedNote_BeforeImageLoads_ShowsDescriptionWithoutFilePath()
    {
        var formatted = new FormattedNote
        {
            Lines = [.. NoteMarkdown.Parse(Reference(Sample, "diagram")).Select(block => NoteLine.For(block))],
        };

        var row = Assert.Single(formatted.Children.OfType<Border>());
        var placeholder = Assert.IsType<TextBlock>(row.Child);
        Assert.Equal("Loading image: diagram", placeholder.Text);
        Assert.DoesNotContain(Sample.AbsoluteUri, placeholder.Text);
    }

    [AvaloniaFact]
    public async Task FormattedNote_ImageBlock_ShowsThumbnailBetweenTextLines()
    {
        var window = Open($"before\n{Reference(Sample)}\nafter");
        await WaitForImage(window, Sample);

        var rows = Formatted(window).Children.Where(child => child.Tag is NoteLine).ToList();
        Assert.Collection(rows,
            first => Assert.IsType<TextBlock>(first),
            middle => Assert.IsType<Border>(middle),
            last => Assert.IsType<TextBlock>(last));
        var row = ImageRow(window);
        var image = Assert.IsType<Image>(row.Child);
        Assert.NotNull(image.Source);
        Assert.True(row.Bounds.Height > 100);
        Assert.Equal("diagram", AutomationProperties.GetName(row));
    }

    [AvaloniaFact]
    public async Task FormattedNote_WhenWindowGrows_ImageGrowsToNaturalSize()
    {
        var window = Open(Reference(Sample));
        await WaitForImage(window, Sample);
        var small = Assert.IsType<Image>(ImageRow(window).Child);
        Assert.True(small.Bounds.Width < 320);

        window.Width = 520;
        window.Height = 420;
        Dispatcher.UIThread.RunJobs();
        await WaitForImage(window, Sample);

        var large = Assert.IsType<Image>(ImageRow(window).Child);
        Assert.Equal(320, large.Bounds.Width);
        Assert.Equal(200, large.Bounds.Height);
    }

    [AvaloniaFact]
    public async Task FormattedNote_WhenWindowShrinks_ImageFitsAgain()
    {
        var window = Open($"before\n{Reference(Sample)}\nafter");
        window.Width = 520;
        window.Height = 420;
        Dispatcher.UIThread.RunJobs();
        await WaitForImage(window, Sample);

        window.Width = 220;
        window.Height = 190;
        Dispatcher.UIThread.RunJobs();

        var image = Assert.IsType<Image>(ImageRow(window).Child);
        var paper = window.FindControl<ScrollViewer>("FormattedScroll")!;
        Assert.True(image.Bounds.Width <= paper.Viewport.Width);
        Assert.True(image.Bounds.Height <= paper.Viewport.Height);
        Assert.InRange(image.Bounds.Width / image.Bounds.Height, 1.59, 1.61);
        Assert.True(paper.Extent.Height > paper.Viewport.Height);
        paper.Offset = new Vector(0, paper.Extent.Height);
        Dispatcher.UIThread.RunJobs();
        Assert.True(paper.Offset.Y > 0);
        Assert.Equal(2, Formatted(window).Children.OfType<TextBlock>().Count(row => row.Tag is NoteLine));
    }

    [AvaloniaFact]
    public async Task FormattedNote_MissingImage_ShowsEditablePlaceholder()
    {
        var missing = new Uri(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"));
        var window = Open(Reference(missing, "lost diagram"));
        await WaitForImage(window, missing);

        var row = ImageRow(window);
        var placeholder = Assert.IsType<TextBlock>(row.Child);
        Assert.Equal("Image unavailable: lost diagram", placeholder.Text);
        Assert.DoesNotContain(missing.AbsoluteUri, placeholder.Text);
        Assert.True(row.Bounds.Height > 0);

        Click(window, row);
        Assert.True(window.FindControl<TextBox>("Body")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task ClickPlaceholder_OpensEditorAtReference()
    {
        var missing = new Uri(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"));
        var content = $"before\n{Reference(missing, "missing")}\nafter";
        var window = Open(content);
        await WaitForImage(window, missing);

        Click(window, ImageRow(window));

        Assert.Equal(content.IndexOf("![", StringComparison.Ordinal), window.FindControl<TextBox>("Body")!.CaretIndex);
        Assert.Equal(content, ((NoteViewModel)window.DataContext!).Content);
        Assert.Empty(_links.Opened);
    }

    [AvaloniaFact]
    public async Task FormattedNote_ReopeningMissingImage_RevalidatesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reopened-{Guid.NewGuid():N}.png");
        var uri = new Uri(path);
        var content = Reference(uri, "reopened");
        try
        {
            var first = Open(content);
            await WaitForImage(first, uri);
            Assert.IsType<TextBlock>(ImageRow(first).Child);
            first.Close();

            File.Copy(Sample.LocalPath, path);
            var reopened = Open(content);
            await WaitForImage(reopened, uri);
            Assert.IsType<Image>(ImageRow(reopened).Child);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task ClickImage_OpensEditorAtReferenceWithoutSaving()
    {
        var content = $"before\n{Reference(Sample)}\nafter";
        var window = Open(content);
        await WaitForImage(window, Sample);

        Click(window, ImageRow(window));
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();

        var editor = window.FindControl<TextBox>("Body")!;
        Assert.True(editor.IsEffectivelyVisible);
        Assert.Equal(content.IndexOf("![", StringComparison.Ordinal), editor.CaretIndex);
        Assert.Equal(content, ((NoteViewModel)window.DataContext!).Content);
        Assert.Empty(_links.Opened);
        Assert.Equal(0, _counting.Updates);
    }

    [AvaloniaFact]
    public async Task ChangeImageReference_ReplacesThumbnailWithoutLeavingOldControl()
    {
        var old = Sample;
        var next = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "sample.webp"));
        var window = Open(Reference(old, "old"));
        await WaitForImage(window, old);
        var previousRow = ImageRow(window);
        var previousResult = window.ImageSession!.Get(old)!;

        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await _autoSave.WhenIdleAsync();
        Assert.Equal(0, _counting.Updates);

        ((NoteViewModel)window.DataContext!).Content = Reference(next, "new");
        await WaitForImage(window, next);

        Assert.DoesNotContain(previousRow, Formatted(window).Children);
        Assert.Equal("new", AutomationProperties.GetName(ImageRow(window)));
        Assert.IsType<Image>(ImageRow(window).Child);
        Assert.True(previousResult.IsDisposed);
        Assert.Null(window.ImageSession!.Get(old));
    }

    [AvaloniaFact]
    public async Task FormattedNote_ExistingLinksAndTasks_AreUnaffectedByImage()
    {
        var content = $"- [ ] milk\n{Reference(Sample)}\nsee [the docs](https://example.com/docs)\n- [x] bread";
        var window = Open(content);
        await WaitForImage(window, Sample);
        var note = (NoteViewModel)window.DataContext!;
        var rows = Formatted(window).Children.Where(control => control.Tag is NoteLine).ToList();

        Assert.Equal([typeof(TextBlock), typeof(Border), typeof(TextBlock), typeof(TextBlock)],
            rows.Select(control => control.GetType()));
        Assert.IsType<Image>(ImageRow(window).Child);

        ClickText(window, Assert.IsType<TextBlock>(rows[0]), 0);
        Assert.Equal($"- [x] milk\n{Reference(Sample)}\nsee [the docs](https://example.com/docs)\n- [x] bread", note.Content);
        Assert.IsType<Image>(ImageRow(window).Child);

        var link = Formatted(window).Children.OfType<TextBlock>()
            .Single(control => control.Tag is NoteLine { Block.Kind: MarkdownBlockKind.Paragraph });
        ClickText(window, link, "see th".Length);

        Assert.Equal([new Uri("https://example.com/docs")], _links.Opened);
        Assert.False(note.IsEditing);
        Assert.Equal(Reference(Sample), note.Content.Split('\n')[1]);
        Assert.IsType<Image>(ImageRow(window).Child);
    }
}
