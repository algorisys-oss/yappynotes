using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Views;

/// <summary>A small link form attached to the note's formatting bar.</summary>
public sealed class NoteLinkEditor : ILinkEditor
{
    private readonly Button _anchor;
    private readonly Flyout _flyout;
    private readonly TextBox _text = new() { PlaceholderText = "Text", MinWidth = 170 };
    private readonly TextBox _address = new() { PlaceholderText = "https://…", MinWidth = 170 };
    private readonly Button _save;
    private readonly Button _cancel;
    private TaskCompletionSource<MarkdownLinks.Draft?>? _pending;

    public NoteLinkEditor(Button anchor)
    {
        _anchor = anchor;
        _save = new Button { Content = "Apply", MinWidth = 60 };
        _cancel = new Button { Content = "Cancel", MinWidth = 60 };
        _save.Click += (_, _) => Complete(new MarkdownLinks.Draft(_text.Text ?? string.Empty, _address.Text ?? string.Empty));
        _cancel.Click += (_, _) => Complete(null);

        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(_save);
        buttons.Children.Add(_cancel);
        var form = new StackPanel { Spacing = 5 };
        form.Children.Add(new TextBlock { Text = "Link text" });
        form.Children.Add(_text);
        form.Children.Add(new TextBlock { Text = "Address (http, https or mailto)" });
        form.Children.Add(_address);
        form.Children.Add(buttons);

        _flyout = new Flyout { Content = form };
        _flyout.Closed += (_, _) => Complete(null);
    }

    internal TextBox TextInput => _text;
    internal TextBox AddressInput => _address;
    internal Button ApplyButton => _save;
    internal Button CancelButton => _cancel;
    internal bool IsOpen => _flyout.IsOpen;

    public Task<MarkdownLinks.Draft?> EditAsync(MarkdownLinks.Draft initial)
    {
        if (_pending is not null)
        {
            return Task.FromResult<MarkdownLinks.Draft?>(null);
        }

        _text.Text = initial.Text;
        _address.Text = initial.Address;
        _pending = new TaskCompletionSource<MarkdownLinks.Draft?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _flyout.ShowAt(_anchor);
        _text.Focus();
        return _pending.Task;
    }

    private void Complete(MarkdownLinks.Draft? result)
    {
        var pending = _pending;
        _pending = null;
        if (_flyout.IsOpen)
        {
            _flyout.Hide();
        }

        pending?.TrySetResult(result);
    }
}
