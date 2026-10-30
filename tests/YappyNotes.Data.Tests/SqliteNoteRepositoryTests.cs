using Microsoft.Data.Sqlite;
using YappyNotes.Core;
using YappyNotes.Data;
using YappyNotes.TestKit;

namespace YappyNotes.Data.Tests;

/// <summary>
/// SQLite, against the same contract the in-memory fake answers in Core.Tests.
/// </summary>
/// <remarks>
/// A real file rather than :memory:, because the app ships against a file and the
/// differences that matter - locking, the journal mode surviving a reconnect, a
/// migration still being there on the next connection - only show up with one.
/// </remarks>
public sealed class SqliteNoteRepositoryTests : NoteRepositoryContract, IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"yappynotes-tests-{Guid.CreateVersion7()}");

    protected override async Task<INoteRepository> NewRepositoryAsync()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, $"{Guid.CreateVersion7()}.db");

        var database = new NoteDatabase(file);
        await new Migrator().MigrateAsync(database, TestContext.Current.CancellationToken);

        return new SqliteNoteRepository(database);
    }

    [Fact]
    public async Task SqliteNoteRepository_ImageReference_RoundTripsAsText()
    {
        Directory.CreateDirectory(_directory);
        var database = new NoteDatabase(Path.Combine(_directory, $"{Guid.CreateVersion7()}.db"));
        var token = TestContext.Current.CancellationToken;
        await new Migrator().MigrateAsync(database, token);
        var repository = new SqliteNoteRepository(database);
        const string reference = "![café \\] plano](file:///tmp/caf%C3%A9%20%5Bplano%5D.webp)";
        var note = Note.Create(TimeProvider.System);
        note.Content = $"before\n{reference}\nafter";

        await repository.InsertAsync(note, token);
        var restored = await repository.GetByIdAsync(note.Id, token);

        Assert.NotNull(restored);
        Assert.Equal(note.Content, restored.Content);
        Assert.Equal(reference, restored.Content.Split('\n')[1]);
        Assert.Equal(MarkdownBlockKind.Image, NoteMarkdown.Parse(restored.Content)[1].Kind);

        await using var connection = await database.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "select typeof(Content), Content from notes where Id = @id;";
        command.Parameters.AddWithValue("@id", note.Id.ToString());
        await using var reader = await command.ExecuteReaderAsync(token);
        Assert.True(await reader.ReadAsync(token));
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal(note.Content, reader.GetString(1));
    }

    public void Dispose()
    {
        // The pool keeps the file open, and on Windows that makes the delete fail.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
