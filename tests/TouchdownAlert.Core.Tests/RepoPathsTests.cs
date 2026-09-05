using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.Core.Tests;

public class RepoPathsTests
{
    [Fact]
    public void RepoRoot_FindsAncestorContainingSlnx_FromTestBinDirectory()
    {
        // AppContext.BaseDirectory is deep under tests/TouchdownAlert.Core.Tests/bin/Release/... - RepoRoot must
        // walk all the way up to the repo root where TouchdownAlert.slnx lives.
        var root = RepoPaths.RepoRoot;

        Assert.True(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(root, "TouchdownAlert.slnx")), $"Expected TouchdownAlert.slnx under {root}");
    }

    [Fact]
    public void Resolve_RelativePath_CombinesWithRepoRoot()
    {
        var resolved = RepoPaths.Resolve("config/settings.json");

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(Path.Combine(RepoPaths.RepoRoot, "config/settings.json"), resolved);
        Assert.StartsWith(RepoPaths.RepoRoot, resolved);
    }

    [Fact]
    public void Resolve_AlreadyRootedPath_ReturnedUnchanged()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "some-file.json");

        var resolved = RepoPaths.Resolve(rooted);

        Assert.Equal(rooted, resolved);
    }

    [Fact]
    public void Resolve_BlankPath_ReturnsRepoRoot()
    {
        Assert.Equal(RepoPaths.RepoRoot, RepoPaths.Resolve(""));
        Assert.Equal(RepoPaths.RepoRoot, RepoPaths.Resolve("   "));
    }
}
