// SPDX-License-Identifier: GPL-3.0-or-later
// Solar equations: NOAA Global Monitoring Division, General Solar Position Calculations.
namespace NightBrightness;
internal static class SolarTimes
{
    public static (DateTime Sunrise, DateTime Sunset)? ForDate(DateTime localDate, double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
            return null;
        int days = DateTime.IsLeapYear(localDate.Year) ? 366 : 365;
        double fractionalYear = 2 * Math.PI / days * (localDate.DayOfYear - 1);
        double equation = 229.18 * (0.000075 + 0.001868 * Math.Cos(fractionalYear) - 0.032077 * Math.Sin(fractionalYear)
            - 0.014615 * Math.Cos(2 * fractionalYear) - 0.040849 * Math.Sin(2 * fractionalYear));
        double declination = 0.006918 - 0.399912 * Math.Cos(fractionalYear) + 0.070257 * Math.Sin(fractionalYear)
            - 0.006758 * Math.Cos(2 * fractionalYear) + 0.000907 * Math.Sin(2 * fractionalYear)
            - 0.002697 * Math.Cos(3 * fractionalYear) + 0.00148 * Math.Sin(3 * fractionalYear);
        double latRad = latitude * Math.PI / 180;
        double cosine = (Math.Cos(90.833 * Math.PI / 180) / (Math.Cos(latRad) * Math.Cos(declination)))
            - Math.Tan(latRad) * Math.Tan(declination);
        if (!double.IsFinite(cosine) || cosine < -1 || cosine > 1) return null; // Polar day or night.
        double angle = Math.Acos(cosine) * 180 / Math.PI;
        var utcBase = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Utc);
        DateTime sunrise = TimeZoneInfo.ConvertTimeFromUtc(utcBase.AddMinutes(720 - 4 * (longitude + angle) - equation), TimeZoneInfo.Local);
        DateTime sunset = TimeZoneInfo.ConvertTimeFromUtc(utcBase.AddMinutes(720 - 4 * (longitude - angle) - equation), TimeZoneInfo.Local);
        if (sunrise.Date != localDate.Date || sunset.Date != localDate.Date || sunset <= sunrise) return null;
        return (sunrise, sunset);
    }
}
