using System.Net.Http;
using System.Text.Json;

namespace NightBrightness;

internal sealed record CityResult(string Name, string Region, string Country, double Latitude, double Longitude)
{
    public string Label => string.IsNullOrWhiteSpace(Region)
        ? $"{Name}, {Country}"
        : $"{Name}, {Region}, {Country}";
}

internal static class CityLookup
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<IReadOnlyList<CityResult>> SearchAsync(string query)
    {
        query = query.Trim();
        if (query.Length < 2) throw new ArgumentException("Enter at least two letters for a city.");
        if (query.Length > 100) throw new ArgumentException("Keep the city name under 100 characters.");
        var url = "https://geocoding-api.open-meteo.com/v1/search?name=" +
            Uri.EscapeDataString(query) + "&count=5&language=en";
        using var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (!data.RootElement.TryGetProperty("results", out var results)) return [];
        var places = new List<CityResult>();
        foreach (var item in results.EnumerateArray())
        {
            if (!item.TryGetProperty("latitude", out var latitude) ||
                !item.TryGetProperty("longitude", out var longitude)) continue;
            static string Get(JsonElement item, string key) =>
                item.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
            places.Add(new CityResult(Get(item, "name"), Get(item, "admin1"),
                Get(item, "country"), latitude.GetDouble(), longitude.GetDouble()));
        }
        return places;
    }
}
