using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Abstractions;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.Core.Sounds;

/// <summary>Resolves configured sound file names to absolute paths on disk.</summary>
public sealed class SoundFileResolver : ISoundFileResolver
{
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

        return RepoPaths.Resolve(configuredDirectory);
    }
}
