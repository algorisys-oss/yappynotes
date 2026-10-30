using Avalonia.Media.Imaging;
using YappyNotes.Core;

namespace YappyNotes.App;

/// <summary>Saves a clipboard bitmap as a managed PNG before its URI enters a note.</summary>
public sealed class PastedImageStore
{
    private readonly UserPaths _paths;
    private readonly Func<string, Stream> _openTemporary;

    public PastedImageStore(UserPaths paths, Func<string, Stream>? openTemporary = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _openTemporary = openTemporary ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));
    }

    public Task<Uri> SaveAsync(Bitmap bitmap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (!LocalImageLoader.SourceDimensionsAllowed(bitmap.PixelSize.Width, bitmap.PixelSize.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(bitmap), "The image exceeds the source dimension limit.");
        }

        return Task.Run(() => Save(bitmap, cancellationToken), cancellationToken);
    }

    private Uri Save(Bitmap bitmap, CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(_paths.ImageDirectory);
        Directory.CreateDirectory(directory);
        var name = Guid.CreateVersion7().ToString("N");
        var temporary = Path.Combine(directory, $".{name}.tmp");
        var final = Path.Combine(directory, $"{name}.png");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var output = _openTemporary(temporary))
            {
                bitmap.Save(output, PngBitmapEncoderOptions.Default);
                output.Flush();
                if (output.Length > LocalImageLoader.MaxFileBytes)
                {
                    throw new IOException("The encoded image exceeds the file size limit.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, final);
            return new Uri(final);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // A failed write must never publish a final file or reference.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
