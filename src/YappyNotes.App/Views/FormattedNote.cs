using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using YappyNotes.App;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Views;

/// <summary>
/// A note's Markdown, drawn formatted: one wrapping line of text per line of the
/// note. Dragging selects text; clicking it stays in Preview.
/// </summary>
/// <remarks>
/// <para>
/// Checkboxes and links are characters in the text rather than controls placed in
/// it. That keeps each line one piece of text that wraps as one, and it means a
/// press is answered by asking the line's layout which character is under the
/// pointer - the view-model decides what pressing that character does. Nothing
/// here knows what a checkbox or a link does.
/// </para>
/// <para>Dragging selects visible source text; a press and release still click.</para>
/// </remarks>
public sealed class FormattedNote : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<NoteLine>?> LinesProperty =
        AvaloniaProperty.Register<FormattedNote, IReadOnlyList<NoteLine>?>(nameof(Lines));

    public static readonly StyledProperty<PreviewSelection?> SelectionProperty =
        AvaloniaProperty.Register<FormattedNote, PreviewSelection?>(nameof(Selection));

    /// <summary>
    /// A row drawn among the lines rather than after them all - the line a new to-do
    /// is typed on, where the item will be stored.
    /// </summary>
    public static readonly StyledProperty<Control?> AddRowProperty =
        AvaloniaProperty.Register<FormattedNote, Control?>(nameof(AddRow));

    /// <summary>Which line the <see cref="AddRow"/> is drawn before; past the end is after them all.</summary>
    public static readonly StyledProperty<int> AddRowPositionProperty =
        AvaloniaProperty.Register<FormattedNote, int>(nameof(AddRowPosition), int.MaxValue);

    private const double BaseFontSize = 14;
    private const double IndentPerLevel = 16;

    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x3E, 0x8C));
    private static readonly IBrush MarkerBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A));
    private static readonly IBrush CodeBrush = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x00, 0x00));
    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x2B, 0x6C, 0xC4));
    private static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, DejaVu Sans Mono, Menlo, monospace");
    private Press? _press;

    private sealed record Press(IPointer Pointer, IReadOnlyList<NoteLine> Lines, int Line, int Boundary,
        NoteLine ClickLine, int ClickIndex, Point Position)
    {
        public bool Dragged { get; set; }
    }

    public FormattedNote()
    {
        // A panel with no background is not hit-testable where its children draw
        // text, so presses on a line fell straight through to the paper behind and
        // opened the editor at the end. Transparent, and only as tall as the text.
        Background = Brushes.Transparent;
    }

    public IReadOnlyList<NoteLine>? Lines
    {
        get => GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public PreviewSelection? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public Control? AddRow
    {
        get => GetValue(AddRowProperty);
        set => SetValue(AddRowProperty, value);
    }

    public int AddRowPosition
    {
        get => GetValue(AddRowPositionProperty);
        set => SetValue(AddRowPositionProperty, value);
    }

    /// <summary>A character of a line was pressed.</summary>
    public event EventHandler<LinePressedEventArgs>? LinePressed;

    public event EventHandler<RenderedSelectionEventArgs>? SelectionDragged;

    public event EventHandler? SelectionCleared;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AddRowProperty && change.OldValue is Control old)
        {
            Children.Remove(old);
        }

        if (change.Property == LinesProperty || change.Property == AddRowProperty || change.Property == AddRowPositionProperty)
        {
            if (change.Property == LinesProperty)
            {
                _press?.Pointer.Capture(null);
                _press = null;
            }

            Rebuild();
        }

        if (change.Property == SelectionProperty)
        {
            Rebuild();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Hit(position, clampToNearestLine: false) is { } hit && Lines is { } lines)
        {
            _press = new Press(e.Pointer, lines, hit.LineIndex, hit.Boundary,
                lines[hit.LineIndex], hit.ClickIndex, position);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        // Not on a line: left unhandled, for whatever holds the note to answer.
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_press is not { } press || !ReferenceEquals(press.Lines, Lines))
        {
            return;
        }

        var position = e.GetPosition(this);
        var dx = position.X - press.Position.X;
        var dy = position.Y - press.Position.Y;
        if (!press.Dragged && dx * dx + dy * dy < 9)
        {
            return;
        }

        press.Dragged = true;
        UpdateDrag(position, press);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_press is not { } press)
        {
            return;
        }

        _press = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (!ReferenceEquals(press.Lines, Lines))
        {
            return;
        }

        if (press.Dragged)
        {
            UpdateDrag(e.GetPosition(this), press);
        }
        else
        {
            SelectionCleared?.Invoke(this, EventArgs.Empty);
            LinePressed?.Invoke(this, new LinePressedEventArgs(press.ClickLine, press.ClickIndex));
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _press = null;
    }

    private void UpdateDrag(Point position, Press press)
    {
        if (Hit(position, clampToNearestLine: true) is { } hit)
        {
            SelectionDragged?.Invoke(this,
                new RenderedSelectionEventArgs(press.Line, press.Boundary, hit.LineIndex, hit.Boundary));
        }
    }

    private (int LineIndex, int Boundary, int ClickIndex)? Hit(Point position, bool clampToNearestLine)
    {
        var lines = Lines;
        if (lines is null)
        {
            return null;
        }

        TextBlock? nearest = null;
        var nearestDistance = double.PositiveInfinity;
        foreach (var text in Children.OfType<TextBlock>())
        {
            if (text.Tag is not NoteLine line)
            {
                continue;
            }

            if (text.Bounds.Contains(position))
            {
                nearest = text;
                break;
            }

            var distance = position.Y < text.Bounds.Top ? text.Bounds.Top - position.Y
                : Math.Max(0, position.Y - text.Bounds.Bottom);
            if (distance < nearestDistance)
            {
                nearest = text;
                nearestDistance = distance;
            }
        }

        if (nearest is null || (!clampToNearestLine && !nearest.Bounds.Contains(position))
            || nearest.Tag is not NoteLine chosen)
        {
            return null;
        }

        var lineIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (ReferenceEquals(lines[i], chosen))
            {
                lineIndex = i;
                break;
            }
        }

        if (lineIndex < 0)
        {
            return null;
        }

        var hit = nearest.TextLayout.HitTestPoint(position - nearest.Bounds.Position);
        var length = DrawnLength(chosen);
        var clickIndex = hit.IsInside ? hit.TextPosition : length;
        var boundary = Math.Clamp(hit.TextPosition + (hit.IsTrailing ? 1 : 0), 0, length);
        return (lineIndex, boundary, clickIndex);
    }

    private static int DrawnLength(NoteLine line) => line.Marker.Length + line.Block.Runs.Sum(run => run.Text.Length);

    /// <summary>
    /// Redraws the lines around the add row without ever taking the row out. Removing
    /// it would take the keyboard off a to-do being typed every time an item is added
    /// - which is exactly when the lines change.
    /// </summary>
    private void Rebuild()
    {
        foreach (var drawn in Children.OfType<TextBlock>().Where(child => child.Tag is NoteLine).ToList())
        {
            Children.Remove(drawn);
        }

        var lines = Lines ?? [];
        var row = AddRow;

        if (row is not null && !Children.Contains(row))
        {
            Children.Add(row);
        }

        // The row is the only child left, so each line goes in before or after it.
        var before = Math.Clamp(AddRowPosition, 0, lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (row is not null && i < before)
            {
                Children.Insert(i, Draw(lines[i], Selection));
            }
            else
            {
                Children.Add(Draw(lines[i], Selection));
            }
        }
    }

    private static TextBlock Draw(NoteLine line, PreviewSelection? selection)
    {
        var block = line.Block;
        var text = new TextBlock
        {
            Tag = line,
            TextWrapping = TextWrapping.Wrap,
            FontSize = block.Kind == MarkdownBlockKind.Heading ? HeadingSize(block.Level) : BaseFontSize,
            FontWeight = block.Kind == MarkdownBlockKind.Heading ? FontWeight.SemiBold : FontWeight.Normal,
            Margin = new Thickness(block.Level * IndentPerLevel * (block.Kind == MarkdownBlockKind.Heading ? 0 : 1), 0, 0, 2),
            // An empty line still takes a line's height, or the spacing the reader
            // typed would collapse.
            MinHeight = BaseFontSize * 1.35,
        };

        if (line.Marker.Length > 0)
        {
            text.Inlines!.Add(new Run(line.Marker) { Foreground = MarkerBrush });
        }

        foreach (var run in block.Runs)
        {
            var selectedStart = Math.Max(selection?.Start ?? 0, run.SourceStart);
            var selectedEnd = Math.Min(selection?.End ?? 0, run.SourceStart + run.Text.Length);
            if (selectedStart >= selectedEnd)
            {
                text.Inlines!.Add(Draw(run, line));
                continue;
            }

            if (selectedStart > run.SourceStart)
            {
                text.Inlines!.Add(Draw(run with { Text = run.Text[..(selectedStart - run.SourceStart)] }, line));
            }

            var highlighted = Draw(run with
            {
                Text = run.Text[(selectedStart - run.SourceStart)..(selectedEnd - run.SourceStart)],
                SourceStart = selectedStart,
            }, line);
            highlighted.Background = SelectionBrush;
            text.Inlines!.Add(highlighted);

            if (selectedEnd < run.SourceStart + run.Text.Length)
            {
                text.Inlines!.Add(Draw(run with
                {
                    Text = run.Text[(selectedEnd - run.SourceStart)..],
                    SourceStart = selectedEnd,
                }, line));
            }
        }

        return text;
    }

    private static Run Draw(MarkdownRun run, NoteLine line)
    {
        var done = line.Block is { Kind: MarkdownBlockKind.Task, IsDone: true };
        var drawn = new Run(run.Text);

        if (run.Style.HasFlag(RunStyle.Bold))
        {
            drawn.FontWeight = FontWeight.Bold;
        }

        if (run.Style.HasFlag(RunStyle.Italic))
        {
            drawn.FontStyle = FontStyle.Italic;
        }

        if (run.Style.HasFlag(RunStyle.Code))
        {
            drawn.FontFamily = Monospace;
            drawn.Background = CodeBrush;
        }

        if (run.Due is not null)
        {
            // The date keeps its characters - a click on it must still land on the
            // same character of the Markdown - and says how soon it is by colour.
            var state = line.DueStateOf(run);
            drawn.Foreground = DueBrushes.For(state);
            drawn.FontWeight = state is DueState.Overdue or DueState.Today ? FontWeight.SemiBold : FontWeight.Normal;
        }
        else if (run.Link is not null)
        {
            drawn.Foreground = LinkBrush;
            drawn.TextDecorations = TextDecorations.Underline;
        }
        else if (done)
        {
            drawn.Foreground = DoneBrush;
            drawn.TextDecorations = TextDecorations.Strikethrough;
        }

        return drawn;
    }

    private static double HeadingSize(int level) => level switch
    {
        1 => 20,
        2 => 17,
        _ => 15,
    };
}

public sealed class LinePressedEventArgs(NoteLine line, int drawnIndex) : EventArgs
{
    public NoteLine Line { get; } = line;

    public int DrawnIndex { get; } = drawnIndex;

}

public sealed class RenderedSelectionEventArgs(int anchorLine, int anchorBoundary, int focusLine, int focusBoundary) : EventArgs
{
    public int AnchorLine { get; } = anchorLine;
    public int AnchorBoundary { get; } = anchorBoundary;
    public int FocusLine { get; } = focusLine;
    public int FocusBoundary { get; } = focusBoundary;
}
