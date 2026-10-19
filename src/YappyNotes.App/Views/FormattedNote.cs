using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using YappyNotes.App;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Views;

/// <summary>
/// A note's Markdown, drawn formatted: one wrapping line of text per line of the
/// note.
/// </summary>
/// <remarks>
/// <para>
/// Checkboxes and links are characters in the text rather than controls placed in
/// it. That keeps each line one piece of text that wraps as one, and it means a
/// press is answered by asking the line's layout which character is under the
/// pointer - the view-model decides what pressing that character does. Nothing
/// here knows what a checkbox or a link does.
/// </para>
/// <para>
/// It does not select text. Selecting and clicking-to-edit want the same gesture,
/// and the editor is one press away for anyone who wants to copy.
/// </para>
/// </remarks>
public sealed class FormattedNote : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<NoteLine>?> LinesProperty =
        AvaloniaProperty.Register<FormattedNote, IReadOnlyList<NoteLine>?>(nameof(Lines));

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
    private static readonly FontFamily Monospace = new("Cascadia Mono, Consolas, DejaVu Sans Mono, Menlo, monospace");

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AddRowProperty && change.OldValue is Control old)
        {
            Children.Remove(old);
        }

        if (change.Property == LinesProperty || change.Property == AddRowProperty || change.Property == AddRowPositionProperty)
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

        foreach (var text in this.GetVisualDescendants().OfType<TextBlock>())
        {
            if (text.Tag is not NoteLine line)
            {
                continue;
            }

            var position = e.GetPosition(text);
            if (!new Rect(text.Bounds.Size).Contains(position))
            {
                continue;
            }

            var hit = text.TextLayout.HitTestPoint(position);

            // Past the end of the line's text there is no character under the
            // pointer, so no box or link either: it is a press at the line's end.
            var index = hit.IsInside ? hit.TextPosition : DrawnLength(line);
            LinePressed?.Invoke(this, new LinePressedEventArgs(line, index, hit.IsInside && hit.IsTrailing));
            e.Handled = true;
            return;
        }

        // Not on a line: left unhandled, for whatever holds the note to answer.
    }

    private static int DrawnLength(NoteLine line) => line.Marker.Length + line.Block.Runs.Sum(run => run.Text.Length);

    private async Task CopyCodeAsync(string code)
    {
        if (code.Length == 0)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(code);
        }
    }

    private Control DrawCode(NoteLine line)
    {
        var code = string.Concat(line.Block.Runs.Select(run => run.Text));
        var copy = new Button
        {
            Content = "Copy",
            IsEnabled = code.Length > 0,
            Padding = new Thickness(5, 1),
        };
        copy.Click += async (_, _) => await CopyCodeAsync(code);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        if (line.Block.Language is { } language)
        {
            header.Children.Add(new TextBlock
            {
                Text = language,
                FontFamily = Monospace,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
        }

        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);

        var codeText = new TextBlock
        {
            Tag = line,
            Text = code,
            FontFamily = Monospace,
            FontSize = BaseFontSize,
            TextWrapping = TextWrapping.NoWrap,
        };

        var scroll = new ScrollViewer
        {
            Content = codeText,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(scroll);

        var container = new Border
        {
            Tag = line,
            Child = content,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2),
            Padding = new Thickness(6, 4),
        };
        container.Bind(Border.BackgroundProperty, new Binding(nameof(NoteViewModel.Color))
        {
            Converter = NoteBrushes.CodePaper,
        });

        return container;
    }

    /// <summary>
    /// Redraws the lines around the add row without ever taking the row out. Removing
    /// it would take the keyboard off a to-do being typed every time an item is added
    /// - which is exactly when the lines change.
    /// </summary>
    private void Rebuild()
    {
        foreach (var drawn in Children.Where(child => child.Tag is NoteLine).ToList())
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
                Children.Insert(i, Draw(lines[i]));
            }
            else
            {
                Children.Add(Draw(lines[i]));
            }
        }
    }

    private Control Draw(NoteLine line)
    {
        var block = line.Block;
        if (block.Kind == MarkdownBlockKind.CodeFence)
        {
            return DrawCode(line);
        }

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
            text.Inlines!.Add(Draw(run, line));
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

public sealed class LinePressedEventArgs(NoteLine line, int drawnIndex, bool trailing) : EventArgs
{
    public NoteLine Line { get; } = line;

    public int DrawnIndex { get; } = drawnIndex;

    public bool Trailing { get; } = trailing;
}
