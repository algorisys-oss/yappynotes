using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace YappyNotes.App;

public enum ImageLoadStatus { Ready, Missing, Invalid, TooLarge, Unsupported }

/// <summary>A decoded bitmap and the source size. Its owner must dispose it.</summary>
public sealed class ImageLoadResult(ImageLoadStatus status, PixelSize naturalSize, Bitmap? bitmap = null) : IDisposable
{
    public ImageLoadStatus Status { get; } = status;
    public PixelSize NaturalSize { get; } = naturalSize;
    public Bitmap? Bitmap { get; } = bitmap;
    internal bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        Bitmap?.Dispose();
    }
}

public interface ILocalImageLoader
{
    Task<ImageLoadResult> LoadAsync(Uri uri, Size viewport, CancellationToken cancellationToken = default);
}

/// <summary>Loads a bounded local raster image at the size needed by one note.</summary>
public sealed class LocalImageLoader : ILocalImageLoader
{
    public const long MaxFileBytes = 32L * 1024 * 1024;
    public const int MaxSourceSide = 20_000;
    public const long MaxSourcePixels = 100_000_000;
    public const int MaxDisplaySide = 4_096;

    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);

    public async Task<ImageLoadResult> LoadAsync(Uri uri, Size viewport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!TryLocalPath(uri, out var path))
        {
            return new ImageLoadResult(ImageLoadStatus.Unsupported, default);
        }

        await DecodeSlots.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Decode(path, viewport, cancellationToken), cancellationToken);
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private static bool TryLocalPath(Uri uri, out string path)
    {
        path = string.Empty;
        if (!uri.IsAbsoluteUri || !uri.IsFile || uri.Host.Length != 0
            || !uri.AbsoluteUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            || uri.AbsoluteUri.StartsWith("file:////", StringComparison.OrdinalIgnoreCase)
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (!new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" }
            .Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        path = uri.LocalPath;
        return Path.IsPathFullyQualified(path);
    }

    private static ImageLoadResult Decode(string path, Size viewport, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes)
            {
                return new ImageLoadResult(ImageLoadStatus.TooLarge, default);
            }

            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg
                or SKEncodedImageFormat.Gif or SKEncodedImageFormat.Webp))
            {
                return new ImageLoadResult(ImageLoadStatus.Invalid, default);
            }

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0)
            {
                return new ImageLoadResult(ImageLoadStatus.Invalid, default);
            }

            if (!SourceDimensionsAllowed(info.Width, info.Height))
            {
                return new ImageLoadResult(ImageLoadStatus.TooLarge, default);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var natural = new PixelSize(info.Width, info.Height);
            var scale = Math.Min(1d, Math.Min(
                MaxDisplaySide / (double)Math.Max(info.Width, info.Height),
                Math.Min(viewport.Width / info.Width, viewport.Height / info.Height)));
            if (double.IsNaN(scale) || scale <= 0)
            {
                return new ImageLoadResult(ImageLoadStatus.Invalid, natural);
            }

            var width = Math.Max(1, (int)Math.Floor(info.Width * scale));
            var height = Math.Max(1, (int)Math.Floor(info.Height * scale));
            // The metadata probe has consumed the stream. Decode from a new handle
            // so the codec and bitmap never compete for one stream position.
            using var pixels = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (pixels.Length > MaxFileBytes)
            {
                return new ImageLoadResult(ImageLoadStatus.TooLarge, default);
            }

            var bitmap = width / (double)info.Width <= height / (double)info.Height
                ? Bitmap.DecodeToWidth(pixels, width)
                : Bitmap.DecodeToHeight(pixels, height);
            if (cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new ImageLoadResult(ImageLoadStatus.Ready, natural, bitmap);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileNotFoundException)
        {
            return new ImageLoadResult(ImageLoadStatus.Missing, default);
        }
        catch (DirectoryNotFoundException)
        {
            return new ImageLoadResult(ImageLoadStatus.Missing, default);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new ImageLoadResult(ImageLoadStatus.Invalid, default);
        }
    }

    internal static bool SourceDimensionsAllowed(int width, int height)
        => width is > 0 and <= MaxSourceSide
            && height is > 0 and <= MaxSourceSide
            && (long)width * height <= MaxSourcePixels;
}
