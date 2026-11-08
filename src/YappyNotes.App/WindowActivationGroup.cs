using Avalonia.Controls;

namespace YappyNotes.App;

/// <summary>
/// Keeps desktop-specific window-switcher behavior out of the note and manager views.
/// </summary>
internal interface IWindowActivationGroup
{
    bool ShowNotesInTaskbar { get; }

    void Register(Window window);
}

internal static class WindowActivationGroup
{
    public static IWindowActivationGroup ForCurrentPlatform() =>
        OperatingSystem.IsLinux() ? new LinuxWindowActivationGroup() : new PassiveWindowActivationGroup();
}

internal sealed class PassiveWindowActivationGroup : IWindowActivationGroup
{
    public bool ShowNotesInTaskbar => false;

    public void Register(Window window) { }
}

/// <summary>
/// A note stays in the Linux switcher even when the manager is closed. Activating
/// any app window raises the other open windows, then returns focus to the one
/// selected by the user. A short-lived set ignores activation events caused by
/// those raises, including events delivered after Activate has returned.
/// </summary>
internal sealed class LinuxWindowActivationGroup : IWindowActivationGroup
{
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromMilliseconds(500);

    private readonly List<Window> _windows = [];
    private readonly HashSet<Window> _internalActivations = [];
    private readonly Action<Window> _activate;
    private readonly TimeProvider _clock;
    private long _internalActivationStarted;
    private bool _raising;

    public LinuxWindowActivationGroup(Action<Window>? activate = null, TimeProvider? clock = null)
    {
        _activate = activate ?? (window => window.Activate());
        _clock = clock ?? TimeProvider.System;
    }

    public bool ShowNotesInTaskbar => true;

    public void Register(Window window)
    {
        if (_windows.Contains(window))
        {
            return;
        }

        _windows.Add(window);
        window.Activated += OnActivated;
        window.Closed += OnClosed;
    }

    internal void BringForward(Window selected)
    {
        if (!_windows.Contains(selected) || !selected.IsVisible || _raising)
        {
            return;
        }

        if (_internalActivations.Count > 0
            && _clock.GetElapsedTime(_internalActivationStarted) > ActivationTimeout)
        {
            _internalActivations.Clear();
        }

        if (_internalActivations.Remove(selected))
        {
            return;
        }

        var others = _windows.Where(window => window != selected && window.IsVisible).ToList();
        if (others.Count == 0)
        {
            return;
        }

        _raising = true;
        _internalActivationStarted = _clock.GetTimestamp();
        try
        {
            foreach (var window in others)
            {
                _internalActivations.Add(window);
                _activate(window);
            }

            _internalActivations.Add(selected);
            _activate(selected);
        }
        finally
        {
            _raising = false;
        }
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            BringForward(window);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        _windows.Remove(window);
        _internalActivations.Remove(window);
        window.Activated -= OnActivated;
        window.Closed -= OnClosed;
    }
}
