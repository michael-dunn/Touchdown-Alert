using System.Text.Json;

namespace TouchdownAlert.Overlay.Contracts;

/// <summary>
/// Shared System.Text.Json options for talking to the App: camelCase on the wire, case-insensitive on the
/// way in (tolerant of the App's exact casing choices), and tolerant of fields the overlay doesn't model.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
