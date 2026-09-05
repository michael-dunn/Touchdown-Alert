namespace TouchdownAlert.Core.Configuration;

/// <summary>
/// Resolves paths that are configured relative to the repo root: the nearest ancestor folder (walking up
/// from <see cref="AppContext.BaseDirectory"/>, then the current working directory) containing
/// <c>TouchdownAlert.slnx</c>. Falls back to the current directory when no such ancestor is found.
/// Shared by <see cref="Sounds.SoundFileResolver"/>, <see cref="Yahoo.YahooTokenStore"/>, and the settings file.
/// </summary>
public static class RepoPaths
{
    private const string SolutionFileName = "TouchdownAlert.slnx";

    /// <summary>The resolved repo root (nearest ancestor containing TouchdownAlert.slnx), or the current
    /// working directory when none is found.</summary>
    public static string RepoRoot =>
        FindAncestorContaining(AppContext.BaseDirectory, SolutionFileName)
            ?? FindAncestorContaining(Directory.GetCurrentDirectory(), SolutionFileName)
            ?? Directory.GetCurrentDirectory();

    /// <summary>
    /// Resolves <paramref name="relative"/> against <see cref="RepoRoot"/>. An already-rooted path is
    /// returned unchanged.
    /// </summary>
    public static string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            return RepoRoot;
        }

        return Path.IsPathRooted(relative) ? relative : Path.Combine(RepoRoot, relative);
    }

    private static string? FindAncestorContaining(string startDirectory, string fileName)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, fileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
