using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TouchdownAlert.Core.Configuration;

namespace TouchdownAlert.Core.Yahoo;

/// <summary>The persisted OAuth2 token state for one Yahoo login.</summary>
public sealed record YahooTokenData(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("expiresAtUtc")] DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("obtainedAtUtc")] DateTimeOffset ObtainedAtUtc);

/// <summary>Loads/saves the Yahoo OAuth token to/from disk.</summary>
public interface IYahooTokenStore
{
    /// <summary>True if a token file exists on disk (does not check whether it has expired).</summary>
    bool HasToken { get; }

    YahooTokenData? Load();

    void Save(YahooTokenData token);

    /// <summary>Deletes the token file, if any. Used by "log out".</summary>
    void Delete();
}

/// <summary>
/// File-backed <see cref="IYahooTokenStore"/>. A single process-wide lock keeps concurrent reads/writes
/// (e.g. a poll racing a token refresh) from corrupting the file.
/// </summary>
public sealed class YahooTokenStore : IYahooTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly string _filePath;

    public YahooTokenStore(IOptions<YahooOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _filePath = ResolvePath(options.Value.TokenFilePath);
    }

    public bool HasToken
    {
        get { lock (_lock) { return File.Exists(_filePath); } }
    }

    public YahooTokenData? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = File.ReadAllText(_filePath);
            try
            {
                return JsonSerializer.Deserialize<YahooTokenData>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public void Save(YahooTokenData token)
    {
        ArgumentNullException.ThrowIfNull(token);
        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(token, JsonOptions));
        }
    }

    public void Delete()
    {
        lock (_lock)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    /// <summary>Resolves a configured token file path the same way <c>SoundFileResolver</c> resolves the sounds directory.</summary>
    internal static string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = "config/yahoo-token.json";
        }

        return RepoPaths.Resolve(configuredPath);
    }
}
