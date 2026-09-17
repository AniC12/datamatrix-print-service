using System.IO;

namespace CodePrintManager.Desktop;

/// <summary>
/// Resolves the two distinct directories the app uses:
/// the read-only program folder (binaries, appsettings.json, Localization) and the
/// writable data folder (database, backups, logs).
///
/// These MUST be separate for installed builds. Velopack replaces the entire
/// <c>current</c> folder on every update and the entire install root on
/// reinstall/uninstall, so anything writable stored under the install directory is
/// destroyed. Since every code in the database is single-use and unrecoverable once
/// lost, installed builds keep their data outside the install tree entirely.
///
/// The app ships as an installer only, so there are exactly two cases to handle:
/// an installed build and a development run.
/// </summary>
internal static class AppPaths
{
    /// <summary>Folder containing the binaries and files shipped with the app.</summary>
    public static string ProgramDir { get; } = AppContext.BaseDirectory;

    /// <summary>
    /// True when running from a Velopack package layout
    /// (<c>&lt;root&gt;\current\app.exe</c> alongside <c>&lt;root&gt;\Update.exe</c>),
    /// i.e. any build produced by the installer. False for <c>dotnet run</c> and
    /// plain <c>dotnet publish</c> output during development.
    /// </summary>
    public static bool IsPackagedBuild { get; } = DetectPackagedBuild();

    /// <summary>Folder holding the database, its backups and the log files.</summary>
    public static string DataDir { get; } = ResolveDataDir();

    public static string DbPath => Path.Combine(DataDir, "codeprintmanager.db");

    public static string LogDir => Path.Combine(DataDir, "logs");

    private static bool DetectPackagedBuild()
    {
        // sq.version is written by Velopack into the versioned content folder.
        if (!File.Exists(Path.Combine(ProgramDir, "sq.version")))
            return false;

        var root = Directory.GetParent(ProgramDir.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;

        return root != null && File.Exists(Path.Combine(root, "Update.exe"));
    }

    private static string ResolveDataDir()
    {
        // Development only: keep everything next to the binaries so a build output
        // folder stays self-contained and disposable.
        if (!IsPackagedBuild)
            return ProgramDir;

        // Installed: store outside the install tree so updates, reinstalls and
        // uninstalls cannot delete the code database.
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodePrintManagerData");

        Directory.CreateDirectory(dir);
        return dir;
    }
}
