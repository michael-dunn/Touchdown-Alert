using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.Core.Sounds;

/// <summary>Resolves configured sound file names to absolute paths on disk.</summary>
public sealed class SoundFileResolver : ISoundFileResolver
{
    private const string SolutionFileName = "TouchdownAlert.slnx";

    public SoundFileResolver(IOptions<SoundOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SoundsDirectory = ResolveSoundsDirectory(options.Value.Directory);
    }

    public string SoundsDirectory { get; }

    public string? Resolve(string soundFile)
    {
        if (string.IsNullOrWhiteSpace(soundFile))
        {
            return null;
        }

        if (Path.IsPathRooted(soundFile))
        {
            return File.Exists(soundFile) ? soundFile : null;
        }

        var candidate = Path.Combine(SoundsDirectory, soundFile);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string ResolveSoundsDirectory(string configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            configuredDirectory = "sounds";
        }

        if (Path.IsPathRooted(configuredDirectory))
        {
            return configuredDirectory;
        }

        var repoRoot = FindAncestorContaining(AppContext.BaseDirectory, SolutionFileName)
            ?? FindAncestorContaining(Directory.GetCurrentDirectory(), SolutionFileName);

        var baseDir = repoRoot ?? Directory.GetCurrentDirectory();
        return Path.Combine(baseDir, configuredDirectory);
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
