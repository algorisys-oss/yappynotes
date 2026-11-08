using YappyNotes.Core;

namespace YappyNotes.ViewModels;

/// <summary>Requests link text and address without making the view-model depend on Avalonia.</summary>
public interface ILinkEditor
{
    Task<MarkdownLinks.Draft?> EditAsync(MarkdownLinks.Draft initial);
}
