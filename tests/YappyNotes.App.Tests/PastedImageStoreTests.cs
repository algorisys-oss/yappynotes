using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using YappyNotes.App;
using YappyNotes.Core;

namespace YappyNotes.App.Tests;

public sealed class PastedImageStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"yappynotes-paste-{Guid.NewGuid():N}");

    private UserPaths Paths => new(_directory);
    private static WriteableBitmap Bitmap() => new(new PixelSize(8, 6), new Vector(96, 96));

    [AvaloniaFact]
    public async Task PastedImageStore_ValidBitmap_WritesPngInsideImageDirectory()
    {
        using var image = Bitmap();
        var uri = await new PastedImageStore(Paths).SaveAsync(image);

        Assert.True(uri.IsFile);
        Assert.Equal(Path.GetFullPath(Paths.ImageDirectory), Path.GetDirectoryName(uri.LocalPath));
        Assert.Equal(".png", Path.GetExtension(uri.LocalPath));
        Assert.True(File.Exists(uri.LocalPath));
        Assert.Equal([uri.LocalPath], Directory.GetFiles(Paths.ImageDirectory));
        using var decoded = await new LocalImageLoader().LoadAsync(uri, new Size(8, 6));
        Assert.Equal(ImageLoadStatus.Ready, decoded.Status);
        Assert.Equal(new PixelSize(8, 6), decoded.NaturalSize);
    }

    [AvaloniaFact]
    public async Task PastedImageStore_TwoPastes_CreateDifferentFiles()
    {
        using var image = Bitmap();
        var store = new PastedImageStore(Paths);

        var first = await store.SaveAsync(image);
        var second = await store.SaveAsync(image);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first.LocalPath));
        Assert.True(File.Exists(second.LocalPath));
        Assert.Equal(2, Directory.GetFiles(Paths.ImageDirectory).Length);
    }

    [AvaloniaFact]
    public async Task PastedImageStore_WriteFailure_LeavesNoFinalFileOrReference()
    {
        using var image = Bitmap();
        var store = new PastedImageStore(Paths, path =>
        {
            File.WriteAllText(path, "partial");
            throw new IOException("disk failure");
        });

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(image));

        Assert.Empty(Directory.GetFiles(Paths.ImageDirectory));
    }

    [AvaloniaFact]
    public async Task PastedImageStore_ExcessiveDimensions_WritesNothing()
    {
        using var image = new WriteableBitmap(new PixelSize(LocalImageLoader.MaxSourceSide + 1, 1), new Vector(96, 96));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new PastedImageStore(Paths).SaveAsync(image));

        Assert.False(Directory.Exists(Paths.ImageDirectory));
    }

    [AvaloniaFact]
    public async Task PastedImageStore_EncodedFileOverLimit_IsNotPublished()
    {
        using var image = Bitmap();
        var store = new PastedImageStore(Paths, path =>
        {
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.SetLength(LocalImageLoader.MaxFileBytes + 1);
            return file;
        });

        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(image));

        Assert.Empty(Directory.GetFiles(Paths.ImageDirectory));
    }

    [AvaloniaFact]
    public async Task PastedImageStore_WithOverriddenDataDirectory_StaysInSandbox()
    {
        var data = Path.Combine(_directory, "sandbox with spaces");
        var paths = UserPaths.Resolve(name => name == UserPaths.DirectoryOverrideVariable ? data : null);
        using var image = Bitmap();

        var uri = await new PastedImageStore(paths).SaveAsync(image);

        Assert.Equal(Path.Combine(data, "images"), Path.GetDirectoryName(uri.LocalPath));
        Assert.Contains("sandbox%20with%20spaces", uri.AbsoluteUri);
        Assert.False(Directory.Exists(Path.Combine(_directory, "images")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
