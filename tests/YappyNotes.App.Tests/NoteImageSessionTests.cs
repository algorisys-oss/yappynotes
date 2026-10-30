using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using YappyNotes.App;
using YappyNotes.Core;
using YappyNotes.ViewModels;

namespace YappyNotes.App.Tests;

public class NoteImageSessionTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Images", "sample.png");

    private static Uri Local(string name) => new($"file:///tmp/{name}.png");

    private static IReadOnlyList<NoteLine> Lines(Uri uri)
        => [.. NoteMarkdown.Parse($"![image]({uri.AbsoluteUri})").Select(block => NoteLine.For(block))];

    private static ImageLoadResult Ready(int width)
    {
        using var file = File.OpenRead(Fixture);
        return new ImageLoadResult(ImageLoadStatus.Ready, new PixelSize(320, 200), Bitmap.DecodeToWidth(file, width));
    }

    [AvaloniaFact]
    public void ImageLoader_ReplacedOrCancelledRequest_DiscardsOldResult()
    {
        var loader = new ControlledLoader();
        using var session = new NoteImageSession(loader, action => action());
        var oldUri = Local("old");
        var newUri = Local("new");
        session.Update(Lines(oldUri), new Size(80, 50));
        var oldRequest = loader.Requests[0];

        session.Update(Lines(newUri), new Size(80, 50));
        var oldResult = Ready(80);
        oldRequest.Complete(oldResult);
        Dispatcher.UIThread.RunJobs();

        Assert.True(oldRequest.Token.IsCancellationRequested);
        Assert.True(oldResult.IsDisposed);
        Assert.Null(session.Get(oldUri));

        var newResult = Ready(80);
        loader.Requests[1].Complete(newResult);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(newResult, session.Get(newUri));

        session.Dispose();
        Assert.True(newResult.IsDisposed);
    }

    [AvaloniaFact]
    public void ImageLoader_WindowGrows_RequestsMorePixelsButShrinkingReusesBitmap()
    {
        var loader = new ControlledLoader();
        using var session = new NoteImageSession(loader, action => action());
        var uri = Local("resized");
        session.Update(Lines(uri), new Size(80, 50));
        var small = Ready(80);
        loader.Requests[0].Complete(small);
        Dispatcher.UIThread.RunJobs();

        session.Update(Lines(uri), new Size(60, 40));
        Assert.Single(loader.Requests);

        session.Update(Lines(uri), new Size(240, 150));
        Assert.Equal(2, loader.Requests.Count);
        var large = Ready(240);
        loader.Requests[1].Complete(large);
        Dispatcher.UIThread.RunJobs();

        Assert.True(small.IsDisposed);
        Assert.Same(large, session.Get(uri));
    }

    private sealed class ControlledLoader : ILocalImageLoader
    {
        public List<Request> Requests { get; } = [];

        public Task<ImageLoadResult> LoadAsync(Uri uri, Size viewport, CancellationToken cancellationToken = default)
        {
            var request = new Request(uri, viewport, cancellationToken);
            Requests.Add(request);
            return request.Completion.Task;
        }
    }

    private sealed class Request(Uri uri, Size viewport, CancellationToken token)
    {
        public Uri Uri { get; } = uri;
        public Size Viewport { get; } = viewport;
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<ImageLoadResult> Completion { get; } = new();
        public void Complete(ImageLoadResult result) => Completion.SetResult(result);
    }
}
