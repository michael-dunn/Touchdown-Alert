using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Sounds;

namespace TouchdownAlert.Core.Tests;

public class SoundFileResolverTests
{
    [Fact]
    public void SoundsDirectory_FindsRepoSoundsDirectoryFromTestBinDirectory()
    {
        var resolver = new SoundFileResolver(Options.Create(new SoundOptions()));

        Assert.True(Directory.Exists(resolver.SoundsDirectory), $"Expected sounds directory to exist at {resolver.SoundsDirectory}");
        Assert.EndsWith("sounds", resolver.SoundsDirectory);
    }

    [Fact]
    public void Resolve_ExistingFile_ReturnsAbsolutePath()
    {
        var resolver = new SoundFileResolver(Options.Create(new SoundOptions()));

        var resolved = resolver.Resolve("horn.mp3");

        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void Resolve_MissingFile_ReturnsNull()
    {
        var resolver = new SoundFileResolver(Options.Create(new SoundOptions()));

        var resolved = resolver.Resolve("this-file-does-not-exist.mp3");

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_BlankFileName_ReturnsNull()
    {
        var resolver = new SoundFileResolver(Options.Create(new SoundOptions()));

        Assert.Null(resolver.Resolve(""));
        Assert.Null(resolver.Resolve("   "));
    }
}
