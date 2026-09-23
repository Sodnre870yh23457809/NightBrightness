using Windows.Devices.Geolocation;
namespace NightBrightness;
internal static class LocationProbe
{
    public static async Task<(double Latitude, double Longitude)?> RequestAsync()
    {
        if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
        return await RefreshAsync();
    }
    public static async Task<(double Latitude, double Longitude)?> RefreshAsync()
    {
        var position = await new Geolocator { DesiredAccuracyInMeters = 5000 }
            .GetGeopositionAsync(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(15));
        var p = position.Coordinate.Point.Position;
        return (p.Latitude, p.Longitude);
    }
}
