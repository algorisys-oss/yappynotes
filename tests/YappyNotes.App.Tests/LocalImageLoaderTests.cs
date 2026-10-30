using Avalonia;
using Avalonia.Headless.XUnit;
using YappyNotes.App;

namespace YappyNotes.App.Tests;

public class LocalImageLoaderTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "sample.png");

    [AvaloniaFact]
    public async Task ImageLoader_ValidLocalFile_ReturnsBitmapSizedForViewport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"yappynotes-image-{Guid.NewGuid():N}.png");
        File.Copy(Fixture, path);
        try
        {
            var loader = new LocalImageLoader();
            var uri = new Uri(path);

            using var small = await loader.LoadAsync(uri, new Size(80, 50));
            using var large = await loader.LoadAsync(uri, new Size(240, 150));
            using var natural = await loader.LoadAsync(uri, new Size(900, 900));

            Assert.Equal(ImageLoadStatus.Ready, small.Status);
            Assert.Equal(new PixelSize(320, 200), small.NaturalSize);
            Assert.Equal(new PixelSize(80, 50), small.Bitmap!.PixelSize);
            Assert.Equal(new PixelSize(240, 150), large.Bitmap!.PixelSize);
            Assert.Equal(new PixelSize(320, 200), natural.Bitmap!.PixelSize);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task ImageLoader_MissingFile_ReturnsUnavailable()
    {
        var uri = new Uri(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.png"));

        using var result = await new LocalImageLoader().LoadAsync(uri, new Size(80, 50));

        Assert.Equal(ImageLoadStatus.Missing, result.Status);
        Assert.Null(result.Bitmap);
    }

    [AvaloniaFact]
    public async Task ImageLoader_InvalidOrOversizedFile_ReturnsUnavailable()
    {
        var badPath = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}.png");
        var hugePath = Path.Combine(Path.GetTempPath(), $"huge-{Guid.NewGuid():N}.png");
        File.WriteAllText(badPath, "not an image");
        using (var huge = File.Create(hugePath))
        {
            huge.SetLength(LocalImageLoader.MaxFileBytes + 1);
        }

        try
        {
            var loader = new LocalImageLoader();
            using var bad = await loader.LoadAsync(new Uri(badPath), new Size(80, 50));
            using var huge = await loader.LoadAsync(new Uri(hugePath), new Size(80, 50));

            Assert.Equal(ImageLoadStatus.Invalid, bad.Status);
            Assert.Equal(ImageLoadStatus.TooLarge, huge.Status);
            Assert.Null(bad.Bitmap);
            Assert.Null(huge.Bitmap);
        }
        finally
        {
            File.Delete(badPath);
            File.Delete(hugePath);
        }
    }

    [AvaloniaTheory]
    [InlineData("https://example.com/a.png")]
    [InlineData("file://server/share/a.png")]
    [InlineData("file:///tmp/a.bmp")]
    public async Task ImageLoader_RemoteOrNetworkUri_DoesNotReadOrFetch(string address)
    {
        using var result = await new LocalImageLoader().LoadAsync(new Uri(address), new Size(80, 50));

        Assert.Equal(ImageLoadStatus.Unsupported, result.Status);
        Assert.Null(result.Bitmap);
    }

    [Theory]
    [InlineData(20_000, 5_000, true)]
    [InlineData(20_001, 1, false)]
    [InlineData(10_001, 10_000, false)]
    public void ImageLoader_SourceDimensions_EnforcesBothLimits(int width, int height, bool allowed)
    {
        Assert.Equal(allowed, LocalImageLoader.SourceDimensionsAllowed(width, height));
    }
}
