using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

namespace YappyNotes.App.Tests;

/// <summary>
/// Opens real image files with the same Skia renderer used by the desktop app.
/// The default headless renderer returns a 1x1 placeholder for every format,
/// which would make a codec test pass without decoding anything.
/// </summary>
public class ImageCodecSmokeTests
{
    [AvaloniaTheory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("bmp")]
    public void Bitmap_WhenFormatIsSupported_DecodesActualDimensions(string extension)
    {
        using var file = File.OpenRead(Fixture(extension));

        using var bitmap = new Bitmap(file);

        Assert.Equal(new Avalonia.PixelSize(320, 200), bitmap.PixelSize);
    }

    [AvaloniaFact]
    public void Bitmap_WhenDecodedToSmallerWidth_PreservesAspectRatio()
    {
        using var file = File.OpenRead(Fixture("png"));

        using var bitmap = Bitmap.DecodeToWidth(file, 80);

        Assert.Equal(new Avalonia.PixelSize(80, 50), bitmap.PixelSize);
    }

    private static string Fixture(string extension)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", $"sample.{extension}");
}
