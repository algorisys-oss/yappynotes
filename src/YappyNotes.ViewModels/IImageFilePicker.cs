namespace YappyNotes.ViewModels;

/// <summary>Chooses one local image without exposing the UI toolkit to the view-model.</summary>
public interface IImageFilePicker
{
    /// <returns>The chosen file URI, or null when the picker is cancelled.</returns>
    Task<Uri?> PickAsync();
}
