using System.Text.Json;
namespace NightBrightness;
internal static class Tests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        var s = new Settings(); s.Validate();
        Check(!WindowsTheme.NeedsExplorerRestart(null, true, 0, 0), "No Explorer restart on startup with matching dark theme");
        Check(!WindowsTheme.NeedsExplorerRestart(true, true, 0, 0), "No Explorer restart on same-theme settings save");
        Check(WindowsTheme.NeedsExplorerRestart(false, true, 0, 0), "Restart on light-to-dark transition even if registry already updated");
        Check(WindowsTheme.NeedsExplorerRestart(true, false, 1, 1), "Restart on dark-to-light transition");
        Check(WindowsTheme.NeedsExplorerRestart(null, true, 1, 1), "Restart when startup changes Windows theme");
        Check(WindowsTheme.NeedsExplorerRestart(null, true, 1, 0), "Restart when app theme changes independently");
        var cases = new (string time, double brightness, bool dark)[] {
            ("00:00",0,true),("04:59:59",0,true),("05:00",55,false),
            ("20:45",55,false),("20:52:30",27.5,false),("21:00",0,true),("23:59:59",0,true) };
        foreach (var c in cases)
        {
            var t = DateTime.Parse("2026-09-22 " + c.time);
            Check(Math.Abs(s.Target(t)-c.brightness)<.0001 && s.IsDark(t)==c.dark, "Default schedule " + c.time);
        }
        double previous = 55;
        for (int i=0;i<=900;i++)
        {
            var b=s.Target(new DateTime(2026,9,22,20,45,0).AddSeconds(i));
            Check(b<=previous && b>=0 && b<=55,"Fade monotonicity"); previous=b;
        }
        var custom=s with { Morning="20:00",Night="06:00",FadeMinutes=60,DayBrightness=70,NightBrightness=10 };
        custom.Validate();
        Check(custom.Target(new DateTime(2026,9,22,5,30,0))==40,"Cross-midnight fade");
        Check(custom.IsDark(new DateTime(2026,9,22,12,0,0)),"Cross-midnight night");
        Check(custom.Target(new DateTime(2026,9,22,23,0,0))==70,"Cross-midnight day");
        Check(s.NextFade(new DateTime(2026,9,22,22,0,0))==new DateTime(2026,9,23,20,45,0),"Next fade");
        var wallpapers=s with { WallpaperEnabled=true,DayWallpaper=@"C:\day.jpg",NightWallpaper=@"C:\night.jpg" };
        wallpapers.Validate();
        Check(wallpapers.WallpaperFor(new DateTime(2026,9,22,4,59,0))==wallpapers.NightWallpaper,"Night wallpaper before morning");
        Check(wallpapers.WallpaperFor(new DateTime(2026,9,22,5,0,0))==wallpapers.DayWallpaper,"Day wallpaper at morning");
        Check(wallpapers.WallpaperFor(new DateTime(2026,9,22,21,0,0))==wallpapers.NightWallpaper,"Night wallpaper at sunset");
        foreach(var badWallpaper in new[] {
            s with { WallpaperEnabled=true },
            s with { WallpaperEnabled=true,DayWallpaper=@"C:\day.jpg" },
            s with { WallpaperEnabled=true,DayWallpaper="day.jpg",NightWallpaper="night.jpg" } })
        {
            bool rejected=false; try { badWallpaper.Validate(); } catch(ArgumentException) { rejected=true; }
            Check(rejected,"Invalid wallpaper settings accepted");
        }
        foreach(var bad in new[] { s with { Morning="25:00" }, s with { Night="05:00" }, s with { FadeMinutes=0 }, s with { NightBrightness=80 },s with { Morning="20:55" } })
        {
            bool rejected=false; try { bad.Validate(); } catch(ArgumentException) { rejected=true; }
            Check(rejected,"Invalid settings accepted");
        }
        var budapestSummer=SolarTimes.ForDate(new DateTime(2026,6,21),47.4979,19.0402);
        var budapestWinter=SolarTimes.ForDate(new DateTime(2026,12,21),47.4979,19.0402);
        Check(budapestSummer.HasValue && budapestWinter.HasValue,"Budapest solar events");
        Check(budapestSummer.GetValueOrDefault().Sunset-budapestSummer.GetValueOrDefault().Sunrise > TimeSpan.FromHours(15),"Summer day length");
        Check(budapestWinter.GetValueOrDefault().Sunset-budapestWinter.GetValueOrDefault().Sunrise < TimeSpan.FromHours(10),"Winter day length");
        var solarSettings=s with { SolarEnabled=true,Latitude=47.4979,Longitude=19.0402,DayBrightness=65 };
        solarSettings.Validate();
        var solarDay=solarSettings.EffectiveTimes(new DateTime(2026,9,23));
        Check(solarDay.Solar && solarDay.Morning < solarDay.Night,"Solar mode resolves sunrise and sunset");
        Check(solarSettings.IsDark(new DateTime(2026,9,23,1,0,0)),"Solar night before sunrise");
        Check(!solarSettings.IsDark(new DateTime(2026,9,23,12,0,0)),"Solar day at noon");
        var next=solarSettings.NextTransition(new DateTime(2026,9,23,12,0,0));
        Check(next.Date==new DateTime(2026,9,23) && next.TimeOfDay.TotalMinutes<solarDay.Night,"Next solar fade transition");
        var solarWallpapers=solarSettings with { WallpaperEnabled=true,DayWallpaper=@"C:\day.jpg",NightWallpaper=@"C:\night.jpg" };
        var solarTimes=SolarTimes.ForDate(new DateTime(2026,9,23),47.4979,19.0402)!.Value;
        Check(solarWallpapers.WallpaperFor(solarTimes.Sunrise.AddMinutes(1))==solarWallpapers.DayWallpaper,"Solar sunrise wallpaper");
        Check(solarWallpapers.WallpaperFor(solarTimes.Sunset.AddMinutes(1))==solarWallpapers.NightWallpaper,"Solar sunset wallpaper");
        var polar=s with { SolarEnabled=true,Latitude=69.6492,Longitude=18.9553 };
        Check(!polar.EffectiveTimes(new DateTime(2026,12,21)).Solar,"Polar fallback to fixed times");
        Check(JsonSerializer.Serialize(JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(custom)))==JsonSerializer.Serialize(custom),"Settings persistence roundtrip");
        var individual=s with { PerMonitorBrightness=true,MonitorBrightness=new() { [10]=new(70,20),[20]=new(45,5) } };
        individual.Validate();
        Check(individual.Target(new DateTime(2026,9,22,12,0,0),10)==70 && individual.Target(new DateTime(2026,9,22,12,0,0),20)==45,"Distinct monitor day levels");
        Check(individual.Target(new DateTime(2026,9,22,22,0,0),10)==20 && individual.Target(new DateTime(2026,9,22,22,0,0),20)==5,"Distinct monitor night levels");
        Check(individual.Target(new DateTime(2026,9,22,20,52,30),10)==45 && individual.Target(new DateTime(2026,9,22,20,52,30),20)==25,"Per-monitor fade interpolation");
        Check(individual.Target(new DateTime(2026,9,22,12,0,0),99)==55,"Unconfigured monitor uses shared fallback");
        Check((individual with { PerMonitorBrightness=false }).Target(new DateTime(2026,9,22,12,0,0),10)==55,"Shared mode ignores retained individual profiles");
        var loadedIndividual=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(individual))!;
        Check(loadedIndividual.Target(new DateTime(2026,9,22,22,0,0),20)==5,"Per-monitor settings persistence");
        bool invalidMonitor=false;
        try { (s with { MonitorBrightness=new() { [10]=new(30,50) } }).Validate(); } catch(ArgumentException) { invalidMonitor=true; }
        Check(invalidMonitor,"Reject invalid per-monitor levels");
        for(int i=0;i<1024;i++) Check(Math.Abs(Nvidia.Gamma(i,100,100,100)-i/1023f)<.000001,"Gamma identity");
        Check(Math.Abs(Nvidia.Gamma(1023,80,100,100)-.8)<.00001,"NVIDIA zero mapping");
        File.WriteAllText(Path.Combine(Program.Data,"self-test.txt"),"PASS: fixed and solar schedules, per-monitor day/night levels and fades, shared fallback, wallpaper transitions, seasons, polar fallback, cross-midnight, 901 fade samples, theme boundaries and Explorer restart decisions, invalid settings, JSON roundtrip, next transition and gamma mapping.");
    }
}
