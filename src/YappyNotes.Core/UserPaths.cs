namespace YappyNotes.Core;

/// <summary>
/// Where YappyNotes keeps its database and managed images.
/// </summary>
/// <remarks>
/// The three platforms disagree about where user data goes, so nothing anywhere
/// else should build a path out of the home directory - do that and the app is
/// wrong on two of the three. Everything goes through here.
/// </remarks>
public sealed class UserPaths
{
    /// <summary>
    /// Overrides the data directory outright. `scripts/dev-start.sh --sandbox`
    /// sets it, so that testing a migration cannot touch the reader's own notes.
    /// </summary>
    public const string DirectoryOverrideVariable = "YAPPYNOTES_DATA_DIR";

    public const string DatabaseFileName = "notes.db";

    private const string AppFolderName = "YappyNotes";

    /// <summary>
    /// Folders this app has kept its notes in under previous names, newest first.
    /// </summary>
    /// <remarks>
    /// The app was SmartNotes before it was YappyNotes, and the folder is named
    /// after it. Append to this rather than editing it if it is ever renamed
    /// again - an entry that is removed is somebody's notes left behind.
    /// </remarks>
    private static readonly string[] PreviousAppFolderNames = ["SmartNotes"];

    private readonly bool _wasChosenByHand;

    public UserPaths(string dataDirectory) : this(dataDirectory, chosenByHand: false)
    {
    }

    private UserPaths(string dataDirectory, bool chosenByHand)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        DataDirectory = dataDirectory;
        DatabaseFile = Path.Combine(dataDirectory, DatabaseFileName);
        ImageDirectory = Path.Combine(dataDirectory, "images");
        _wasChosenByHand = chosenByHand;
    }

    /// <summary>The folder holding the database.</summary>
    public string DataDirectory { get; }

    /// <summary>The database containing notes and settings.</summary>
    public string DatabaseFile { get; }

    /// <summary>PNG files created from images pasted into notes.</summary>
    public string ImageDirectory { get; }

    /// <summary>
    /// The paths this machine should use: the override if it is set, the
    /// platform's convention otherwise.
    /// </summary>
    /// <param name="readEnvironmentVariable">
    /// How to read the environment. Defaults to the real one; tests pass their
    /// own rather than mutating the process they run in, which would make them
    /// order-dependent.
    /// </param>
    public static UserPaths Resolve(Func<string, string?>? readEnvironmentVariable = null)
    {
        var read = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var overridden = read(DirectoryOverrideVariable);

        return string.IsNullOrWhiteSpace(overridden)
            ? new UserPaths(DefaultDataDirectory(read), chosenByHand: false)
            : new UserPaths(overridden, chosenByHand: true);
    }

    /// <summary>Makes the data directory if it is not there yet.</summary>
    public void EnsureCreated() => Directory.CreateDirectory(DataDirectory);

    /// <summary>
    /// Moves notes kept under a previous name of this app into the current
    /// folder, if there are any and this folder does not exist yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called on every start, before anything opens the database. Renaming the
    /// app renames the folder it keeps notes in, and without this everyone's
    /// notes appear to vanish on the first start afterwards - they are simply
    /// somewhere nothing looks any more.
    /// </para>
    /// <para>
    /// It never overwrites. If the current folder already exists then the app has
    /// been started under this name and the old folder is history rather than a
    /// better copy, so both are left alone. A directory chosen by hand through
    /// the override means "use exactly this", so nothing is moved into it either.
    /// </para>
    /// </remarks>
    public void AdoptLegacyDirectory()
    {
        if (_wasChosenByHand || Directory.Exists(DataDirectory))
        {
            return;
        }

        var parent = Path.GetDirectoryName(DataDirectory);
        if (string.IsNullOrEmpty(parent))
        {
            return;
        }

        foreach (var previous in PreviousAppFolderNames)
        {
            var legacy = Path.Combine(parent, previous);
            if (!Directory.Exists(legacy))
            {
                continue;
            }

            try
            {
                Directory.Move(legacy, DataDirectory);
            }
            catch (IOException)
            {
                // Something else holds it, or it is across a filesystem boundary.
                // Starting with an empty folder is a bad day; refusing to start
                // at all is a worse one, and the old notes are still there.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return;
        }
    }

    // Written out per platform rather than taken from Environment.SpecialFolder,
    // because the README promises a specific directory on each of the three and
    // SpecialFolder does not map to all of them the way one would guess.
    private static string DefaultDataDirectory(Func<string, string?> read)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", AppFolderName);
        }

        // Linux, and anything else behaving like it.
        var xdgDataHome = read("XDG_DATA_HOME");
        var root = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(home, ".local", "share")
            : xdgDataHome;

        return Path.Combine(root, AppFolderName);
    }
}
