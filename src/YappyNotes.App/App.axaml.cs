using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Threading;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App;

public partial class App : Application
{
    private AppServices? _services;
    private WindowManager? _windows;
    private bool _shuttingDown;

    /// <summary>
    /// This process's claim on the notes folder, which a second start wakes. Null
    /// under the test harness and the designer, which claim nothing.
    /// </summary>
    internal SingleInstance? RunningCopy { get; init; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The headless test harness brings the Application up without a desktop
        // lifetime, so this has to stay conditional rather than assume a window.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The app lives in the tray and runs all day, so closing the manager
            // - or every note - must not end it. Quitting is the tray's Quit, which
            // goes through TryShutdown so that ShutdownRequested still flushes.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += (_, _) => _shuttingDown = true;
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException +=
                (_, e) => e.Handled = ShutdownNoise.IsHarmless(e.Exception, _shuttingDown);

            _ = StartAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _services = await AppServices.StartAsync(UserPaths.Resolve());
        }
        catch
        {
            // With an explicit shutdown mode nothing else would ever end the
            // process: no window has opened and no tray icon exists yet, so a
            // failed start would leave YappyNotes running with nothing to click.
            desktop.Shutdown(1);
            throw;
        }

        var loginItem = LoginItems.ForThisProcess();

        _windows = new WindowManager(
            _services.Notes,
            _services.AutoSave,
            () => new SettingsViewModel(_services!.Settings, new ThemeApplier(), loginItem),
            _services.Paths);

        var settings = await _services.Settings.LoadAsync();
        new ThemeApplier().Apply(settings.Theme);

        // Every start, so the first start of an installed copy registers it and a
        // moved app has its entry rewritten. It never stops the notes coming back.
        await LoginStartup.ApplyStoredAsync(_services.Settings, loginItem);

        var lifetime = new DesktopAppLifetime(desktop);
        var tray = new TrayViewModel(_services.Notes, _windows, lifetime);
        var updates = new UpdatesViewModel(new VelopackUpdater(), lifetime);
        TrayIcon.SetIcons(this, [TrayMenu.Create(tray, updates)]);

        _windows.ShowManager();

        // Not awaited: the notes come back whether or not GitHub answers.
        _ = updates.CheckOnStartAsync(settings.CheckForUpdates);

        // Restore note windows: every note that was on the desktop comes back
        // where it was left.
        foreach (var note in await _services.Notes.GetActiveAsync())
        {
            await _windows.ShowNoteAsync(note.Id);
        }

        // Only now. A wake shows every note, and one arriving while the restore
        // above was still awaiting a note would open that note's window twice:
        // ShowNoteAsync checks for an open window before its await, not after.
        RunningCopy?.Listen(() => Dispatcher.UIThread.Post(() => tray.BringForwardCommand.Execute(null)));
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // First, before the early return: the tray is torn down after this
        // whether or not the services ever started. See ShutdownNoise.
        _shuttingDown = true;

        if (_services is null)
        {
            return;
        }

        // Everything still sitting in a debounce gets written before the process
        // goes. Blocking here is the price of shutdown being synchronous, and it
        // is a handful of small UPDATEs.
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _services = null;
    }
}
