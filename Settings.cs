// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace NightBrightness;
internal sealed record Settings
{
    public int DayBrightness { get; init; } = 55;
    public int NightBrightness { get; init; } = 0;
    public string Morning { get; init; } = "05:00";
    public string Night { get; init; } = "21:00";
    public int FadeMinutes { get; init; } = 15;
    public bool Enabled { get; init; } = true;
    public bool ThemeEnabled { get; init; } = true;
    public bool WallpaperEnabled { get; init; }
    public string DayWallpaper { get; init; } = "";
    public string NightWallpaper { get; init; } = "";
    public bool StartAtSignIn { get; init; } = true;
    public bool SolarEnabled { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string LocationSource { get; init; } = "";
    public DateTime? LocationUpdatedUtc { get; init; }
    public static double Minutes(string text) => TimeSpan.ParseExact(text, @"hh\:mm", CultureInfo.InvariantCulture).TotalMinutes;
    public static double Mod(double value) => (value % 1440 + 1440) % 1440;
    public void Validate()
    {
        if (!TimeSpan.TryParseExact(Morning, @"hh\:mm", CultureInfo.InvariantCulture, out var m) || m.TotalHours >= 24 ||
            !TimeSpan.TryParseExact(Night, @"hh\:mm", CultureInfo.InvariantCulture, out var n) || n.TotalHours >= 24)
            throw new ArgumentException("Enter times in 24-hour format, such as 05:00 or 21:00.");
        if (m == n) throw new ArgumentException("Morning and night must start at different times.");
        if (DayBrightness is < 0 or > 100 || NightBrightness < 0 || NightBrightness > DayBrightness)
            throw new ArgumentException("Night brightness must be between 0% and day brightness.");
        if (FadeMinutes < 1 || FadeMinutes > 180 || FadeMinutes >= Mod((n-m).TotalMinutes))
            throw new ArgumentException("Use a fade of 1–180 minutes that fits between morning and night.");
        if (Latitude.HasValue != Longitude.HasValue ||
            Latitude is < -90 or > 90 || Longitude is < -180 or > 180 ||
            Latitude.HasValue && !double.IsFinite(Latitude.Value) || Longitude.HasValue && !double.IsFinite(Longitude.Value))
            throw new ArgumentException("Enter valid latitude and longitude, or use Windows location.");
        if (SolarEnabled && !Latitude.HasValue)
            throw new ArgumentException("Search a city, use Windows location, or enter coordinates before enabling sunrise and sunset.");
        if (WallpaperEnabled && (string.IsNullOrWhiteSpace(DayWallpaper) || string.IsNullOrWhiteSpace(NightWallpaper)))
            throw new ArgumentException("Choose both day and night desktop backgrounds before enabling wallpaper switching.");
        if (WallpaperEnabled && (!Path.IsPathFullyQualified(DayWallpaper) || !Path.IsPathFullyQualified(NightWallpaper)))
            throw new ArgumentException("Choose image files using the Browse buttons.");
    }
    public (double Morning, double Night, bool Solar) EffectiveTimes(DateTime localDate)
    {
        if (SolarEnabled && Latitude.HasValue && Longitude.HasValue)
        {
            var solar = SolarTimes.ForDate(localDate, Latitude.Value, Longitude.Value);
            if (solar.HasValue)
                return (solar.Value.Sunrise.TimeOfDay.TotalMinutes, solar.Value.Sunset.TimeOfDay.TotalMinutes, true);
        }
        return (Minutes(Morning), Minutes(Night), false);
    }
    public bool IsDark(DateTime now)
    {
        var times = EffectiveTimes(now.Date);
        return Mod(now.TimeOfDay.TotalMinutes - times.Morning) >= Mod(times.Night - times.Morning);
    }
    public string WallpaperFor(DateTime now) => IsDark(now) ? NightWallpaper : DayWallpaper;
    public double Target(DateTime now)
    {
        var times = EffectiveTimes(now.Date);
        double elapsed = Mod(now.TimeOfDay.TotalMinutes - times.Morning);
        double dayLength = Mod(times.Night - times.Morning);
        if (elapsed >= dayLength) return NightBrightness;
        if (elapsed <= dayLength - FadeMinutes) return DayBrightness;
        return NightBrightness + (DayBrightness - NightBrightness) * (dayLength - elapsed) / FadeMinutes;
    }
    [JsonIgnore]
    public string FadeStart => Format(Mod(EffectiveTimes(DateTime.Today).Night - FadeMinutes));
    public static string Format(double minutes) => TimeSpan.FromMinutes(Math.Round(Mod(minutes)) % 1440).ToString(@"hh\:mm");
    public DateTime NextFade(DateTime now)
    {
        for(int d=0;d<3;d++)
        {
            DateTime date=now.Date.AddDays(d);
            var times=EffectiveTimes(date);
            var start=date.AddMinutes(Mod(times.Night-FadeMinutes));
            if(start>now) return start;
        }
        throw new InvalidOperationException("No future fade found.");
    }
    public DateTime NextTransition(DateTime now)
    {
        for(int d=0;d<3;d++)
        {
            DateTime date=now.Date.AddDays(d);
            var times=EffectiveTimes(date);
            var events=new[] { date.AddMinutes(times.Morning),date.AddMinutes(Mod(times.Night-FadeMinutes)),date.AddMinutes(times.Night) };
            var next=events.Where(t=>t>now).OrderBy(t=>t).FirstOrDefault();
            if(next!=default) return next;
        }
        throw new InvalidOperationException("No future transition found.");
    }
    public static Settings Load()
    {
        var path = Path.Combine(Program.Data, "settings.json");
        if (!File.Exists(path)) return new Settings();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new Exception("Settings file is empty.");
        settings.Validate();
        return settings;
    }
    public void Save()
    {
        Validate();
        var path = Path.Combine(Program.Data, "settings.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
