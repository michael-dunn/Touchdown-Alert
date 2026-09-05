using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;
using TouchdownAlert.Core.Yahoo;

namespace TouchdownAlert.Core.Tests;

public class YahooTokenStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tokenFilePath;

    public YahooTokenStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TouchdownAlert.Tests", Guid.NewGuid().ToString("N"));
        _tokenFilePath = Path.Combine(_tempDir, "yahoo-token.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private YahooTokenStore CreateStore() =>
        new(Options.Create(new YahooOptions { TokenFilePath = _tokenFilePath }));

    [Fact]
    public void HasToken_FalseBeforeSave_TrueAfter()
    {
        var store = CreateStore();

        Assert.False(store.HasToken);

        store.Save(new YahooTokenData("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow));

        Assert.True(store.HasToken);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var store = CreateStore();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var obtainedAt = DateTimeOffset.UtcNow;
        var token = new YahooTokenData("access-token", "refresh-token", expiresAt, obtainedAt);

        store.Save(token);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal("access-token", loaded!.AccessToken);
        Assert.Equal("refresh-token", loaded.RefreshToken);
        Assert.Equal(expiresAt, loaded.ExpiresAtUtc);
        Assert.Equal(obtainedAt, loaded.ObtainedAtUtc);
    }

    [Fact]
    public void Load_WithNoFile_ReturnsNull()
    {
        var store = CreateStore();

        Assert.Null(store.Load());
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var store = CreateStore();
        store.Save(new YahooTokenData("a", "r", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow));

        store.Delete();

        Assert.False(store.HasToken);
        Assert.Null(store.Load());
    }

    [Fact]
    public void Delete_WithNoFile_DoesNotThrow()
    {
        var store = CreateStore();

        store.Delete();
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        Assert.False(Directory.Exists(_tempDir));
        var store = CreateStore();

        store.Save(new YahooTokenData("a", "r", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow));

        Assert.True(File.Exists(_tokenFilePath));
    }
}
