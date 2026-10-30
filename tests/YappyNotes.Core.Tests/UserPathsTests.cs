using YappyNotes.Core;

namespace YappyNotes.Core.Tests;

public class UserPathsTests
{
    /// <summary>An environment with nothing in it.</summary>
    private static string? Nothing(string name) => null;

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables)
        => name => variables.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void Resolve_WithTheOverrideSet_UsesIt()
    {
        // scripts/dev-start.sh --sandbox sets this. If it stops being honoured,
        // a sandbox run silently opens the reader's real notes.
        var paths = UserPaths.Resolve(
            Environment((UserPaths.DirectoryOverrideVariable, "/tmp/somewhere-else")));

        Assert.Equal("/tmp/somewhere-else", paths.DataDirectory);
    }

    [Fact]
    public void Resolve_WithABlankOverride_FallsBackToThePlatformDefault()
    {
        var paths = UserPaths.Resolve(
            Environment((UserPaths.DirectoryOverrideVariable, "   ")));

        Assert.Equal(UserPaths.Resolve(Nothing).DataDirectory, paths.DataDirectory);
    }

    [Fact]
    public void Resolve_WithNoOverride_PutsTheDataSomewhereAbsoluteNamedForTheApp()
    {
        var paths = UserPaths.Resolve(Nothing);

        Assert.True(Path.IsPathRooted(paths.DataDirectory), paths.DataDirectory);
        Assert.Equal("YappyNotes", Path.GetFileName(paths.DataDirectory));
    }

    [Fact]
    public void DatabaseFile_IsAlwaysNotesDbInsideTheDataDirectory()
    {
        var paths = new UserPaths(Path.Combine("/tmp", "sandbox"));

        Assert.Equal(Path.Combine("/tmp", "sandbox", "notes.db"), paths.DatabaseFile);
    }

    [Fact]
    public void UserPaths_ImageDirectory_IsInsideTheSelectedDataDirectory()
    {
        var normal = UserPaths.Resolve(Nothing);
        var overridden = UserPaths.Resolve(Environment((UserPaths.DirectoryOverrideVariable,
            Path.Combine(Path.GetTempPath(), "my notes"))));
        var sandbox = new UserPaths(Path.Combine(Path.GetTempPath(), "sandbox notes"));

        foreach (var paths in new[] { normal, overridden, sandbox })
        {
            Assert.Equal(Path.Combine(paths.DataDirectory, "images"), paths.ImageDirectory);
            Assert.Equal(Path.GetFullPath(paths.ImageDirectory), paths.ImageDirectory);
        }
    }

    [Fact]
    public void UserPaths_GivenABlankDirectory_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => new UserPaths("  "));
    }

    [Fact]
    public void EnsureCreated_WhenTheDirectoryIsNotThere_MakesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yappynotes-{Guid.CreateVersion7()}");
        try
        {
            var paths = new UserPaths(directory);
            Assert.False(Directory.Exists(directory));

            paths.EnsureCreated();

            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EnsureCreated_CalledTwice_DoesNotThrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"yappynotes-{Guid.CreateVersion7()}");
        try
        {
            var paths = new UserPaths(directory);
            paths.EnsureCreated();
            paths.EnsureCreated();

            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_OnLinux_HonoursXdgDataHome()
    {
        if (!OperatingSystem.IsLinux()) return;

        var paths = UserPaths.Resolve(Environment(("XDG_DATA_HOME", "/tmp/xdg")));

        Assert.Equal(Path.Combine("/tmp", "xdg", "YappyNotes"), paths.DataDirectory);
    }

    /// <summary>
    /// The three platforms disagree about where a file like this goes, and the
    /// README promises one answer each. Only the running platform's promise can
    /// be checked here, which is why CI runs on more than one.
    /// </summary>
    [Fact]
    public void Resolve_WithNoOverride_FollowsThisPlatformsConvention()
    {
        var directory = UserPaths.Resolve(Nothing).DataDirectory;
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(
                Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "YappyNotes"),
                directory);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(Path.Combine(home, "Library", "Application Support", "YappyNotes"), directory);
        }
        else
        {
            Assert.Equal(Path.Combine(home, ".local", "share", "YappyNotes"), directory);
        }
    }

    /// <summary>
    /// The app was called SmartNotes until it was called YappyNotes, and the
    /// folder is named after it. Without this, everyone's notes appear to vanish
    /// on the first start after the rename - they are simply in a folder nothing
    /// looks at any more.
    /// </summary>
    [Fact]
    public void AdoptLegacyDirectory_WhenOnlyTheOldFolderExists_MovesItAcross()
    {
        var root = NewRoot();
        var old = Path.Combine(root, "SmartNotes");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "notes.db"), "the notes");

        var paths = new UserPaths(Path.Combine(root, "YappyNotes"));
        paths.AdoptLegacyDirectory();

        Assert.False(Directory.Exists(old));
        Assert.Equal("the notes", File.ReadAllText(paths.DatabaseFile));
    }

    /// <summary>
    /// Never clobber. Somebody who has already started the renamed app has notes
    /// in the new folder, and the old one is history rather than a better copy.
    /// </summary>
    [Fact]
    public void AdoptLegacyDirectory_WhenBothExist_LeavesThemBothAlone()
    {
        var root = NewRoot();
        var old = Path.Combine(root, "SmartNotes");
        var current = Path.Combine(root, "YappyNotes");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(old, "notes.db"), "the old notes");
        File.WriteAllText(Path.Combine(current, "notes.db"), "the notes in use");

        var paths = new UserPaths(current);
        paths.AdoptLegacyDirectory();

        Assert.True(Directory.Exists(old));
        Assert.Equal("the notes in use", File.ReadAllText(paths.DatabaseFile));
    }

    [Fact]
    public void AdoptLegacyDirectory_WithNothingToAdopt_DoesNothing()
    {
        var root = NewRoot();
        var paths = new UserPaths(Path.Combine(root, "YappyNotes"));

        paths.AdoptLegacyDirectory();

        Assert.False(Directory.Exists(paths.DataDirectory));
    }

    [Fact]
    public void AdoptLegacyDirectory_RunTwice_IsHarmless()
    {
        // It runs on every start, not once.
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "SmartNotes"));
        File.WriteAllText(Path.Combine(root, "SmartNotes", "notes.db"), "the notes");
        var paths = new UserPaths(Path.Combine(root, "YappyNotes"));

        paths.AdoptLegacyDirectory();
        paths.AdoptLegacyDirectory();

        Assert.Equal("the notes", File.ReadAllText(paths.DatabaseFile));
    }

    [Fact]
    public void AdoptLegacyDirectory_ForADirectoryChosenByHand_DoesNothing()
    {
        // YAPPYNOTES_DATA_DIR means "use exactly this". Moving a folder into it
        // because of a name it happens to sit beside would be a surprise.
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, "SmartNotes"));
        File.WriteAllText(Path.Combine(root, "SmartNotes", "notes.db"), "the notes");

        var chosen = UserPaths.Resolve(name =>
            name == UserPaths.DirectoryOverrideVariable ? Path.Combine(root, "somewhere-else") : null);
        chosen.AdoptLegacyDirectory();

        Assert.True(Directory.Exists(Path.Combine(root, "SmartNotes")));
        Assert.False(Directory.Exists(chosen.DataDirectory));
    }

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"yappynotes-paths-{Guid.CreateVersion7()}");
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    private readonly List<string> _roots = [];
}
