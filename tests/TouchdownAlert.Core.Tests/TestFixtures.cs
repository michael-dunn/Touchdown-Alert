using System.Text.Json;
using TouchdownAlert.Core.Espn.Wire;

namespace TouchdownAlert.Core.Tests;

/// <summary>Loads the shared JSON fixtures copied into the test output directory under fixtures/.</summary>
internal static class TestFixtures
{
    public static string Path(string fileName) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", fileName);

    public static string ReadText(string fileName) => File.ReadAllText(Path(fileName));

    public static EspnLeagueResponse LoadResponse(string fileName)
    {
        var json = ReadText(fileName);
        return JsonSerializer.Deserialize<EspnLeagueResponse>(json, EspnLeagueResponse.JsonOptions)
            ?? throw new InvalidOperationException($"Fixture {fileName} deserialized to null.");
    }

    public static JsonDocument LoadDocument(string fileName) => JsonDocument.Parse(ReadText(fileName));
}
