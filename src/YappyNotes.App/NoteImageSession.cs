using Avalonia;
using Avalonia.Threading;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App;

/// <summary>Owns the images requested for one open note and discards stale results.</summary>
public sealed class NoteImageSession : IDisposable
{
    private readonly ILocalImageLoader _loader;
    private readonly Action<Action> _publish;
    private readonly Dictionary<Uri, Entry> _entries = [];
    private bool _disposed;

    public NoteImageSession(ILocalImageLoader loader, Action<Action>? publish = null)
    {
        _loader = loader;
        _publish = publish ?? (action => Dispatcher.UIThread.Post(action));
    }

    public event EventHandler? Changed;

    public ImageLoadResult? Get(Uri uri)
        => _entries.GetValueOrDefault(uri)?.Current;

    public void Update(IReadOnlyList<NoteLine> lines, Size viewport)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var wanted = lines.Where(line => line.Block.Kind == MarkdownBlockKind.Image)
            .Select(line => line.Block.ImageUri)
            .OfType<Uri>()
            .ToHashSet();

        foreach (var uri in _entries.Keys.Except(wanted).ToArray())
        {
            _entries[uri].Dispose();
            _entries.Remove(uri);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        foreach (var uri in wanted)
        {
            if (!_entries.TryGetValue(uri, out var entry))
            {
                entry = new Entry();
                _entries.Add(uri, entry);
            }

            if (entry.Current is { Status: not ImageLoadStatus.Ready })
            {
                continue;
            }

            if (entry.Current is { } current && HasEnoughPixels(current, viewport))
            {
                continue;
            }

            if (entry.Pending is not null
                && entry.Requested.Width >= viewport.Width
                && entry.Requested.Height >= viewport.Height)
            {
                continue;
            }

            entry.Pending?.Cancel();
            entry.Pending?.Dispose();
            entry.Pending = new CancellationTokenSource();
            entry.Requested = viewport;
            _ = LoadAndPublishAsync(uri, entry, viewport, entry.Pending.Token);
        }
    }

    private async Task LoadAndPublishAsync(Uri uri, Entry entry, Size viewport, CancellationToken token)
    {
        ImageLoadResult result;
        try
        {
            result = await _loader.LoadAsync(uri, viewport, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            result = new ImageLoadResult(ImageLoadStatus.Invalid, default);
        }

        _publish(() =>
        {
            if (_disposed || token.IsCancellationRequested
                || !_entries.TryGetValue(uri, out var latest) || latest != entry
                || entry.Pending?.Token != token)
            {
                result.Dispose();
                return;
            }

            entry.Current?.Dispose();
            entry.Current = result;
            entry.Pending.Dispose();
            entry.Pending = null;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private static bool HasEnoughPixels(ImageLoadResult result, Size viewport)
    {
        if (result.Bitmap is null)
        {
            return false;
        }

        var natural = result.NaturalSize;
        var scale = Math.Min(1d, Math.Min(
            LocalImageLoader.MaxDisplaySide / (double)Math.Max(natural.Width, natural.Height),
            Math.Min(viewport.Width / natural.Width, viewport.Height / natural.Height)));
        return result.Bitmap.PixelSize.Width >= (int)Math.Floor(natural.Width * scale)
            && result.Bitmap.PixelSize.Height >= (int)Math.Floor(natural.Height * scale);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            entry.Dispose();
        }

        _entries.Clear();
    }

    private sealed class Entry : IDisposable
    {
        public CancellationTokenSource? Pending;
        public Size Requested;
        public ImageLoadResult? Current;

        public void Dispose()
        {
            Pending?.Cancel();
            Pending?.Dispose();
            Current?.Dispose();
        }
    }
}
