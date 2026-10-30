using Avalonia.Controls;
using Avalonia.Platform.Storage;
using YappyNotes.ViewModels;

namespace YappyNotes.App;

/// <summary>Chooses a local image for the note that owns the picker.</summary>
public sealed class AvaloniaImageFilePicker(Func<Window?> owner) : IImageFilePicker
{
    private static readonly FilePickerFileType SupportedImages = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"],
        MimeTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"],
        AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "com.compuserve.gif", "org.webmproject.webp"],
    };

    public async Task<Uri?> PickAsync()
    {
        var window = owner();
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add image",
            AllowMultiple = false,
            FileTypeFilter = [SupportedImages],
        });

        return files.FirstOrDefault()?.Path;
    }
}
