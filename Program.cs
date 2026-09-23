// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Microsoft.Win32;
namespace NightBrightness;
internal static class Program
{
    public static readonly string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightBrightness");
    public static void Log(string message)
    {
        Directory.CreateDirectory(Data);
        var path = Path.Combine(Data, "activity.log");
        if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Move(path, path + ".previous", true);
        File.AppendAllText(path, $"{DateTime.Now:O} {message}\n");
    }
    [STAThread]
    static int Main(string[] args)
    {
        Directory.CreateDirectory(Data);
        try
        {
            if (args.Contains("--self-test")) { Tests.Run(); return 0; }
            if (args.Contains("--wallpaper-probe")) return DesktopWallpaper.Probe() ? 0 : 1;
            if (args.Contains("--probe"))
            {
                var monitors = new Nvidia().Enumerate();
                File.WriteAllText(Path.Combine(Data, "probe.json"), JsonSerializer.Serialize(monitors, new JsonSerializerOptions { WriteIndented = true }));
                return monitors.Count == 2 ? 0 : 2;
            }
            if (args.Contains("--verify-driver"))
            {
                var nv = new Nvidia();
                foreach (var m in nv.Enumerate()) nv.Apply(m, 55);
                return 0;
            }
            using var mutex = new Mutex(true, @"Local\Sondre.NightBrightness", out bool owned);
            if (!owned)
            {
                if (!args.Contains("--background"))
                    File.WriteAllText(Path.Combine(Data, args.Contains("--restore") ? "restore.request" : args.Contains("--quick-panel") ? "quick.request" : "show.request"), "1");
                return 0;
            }
            ApplicationConfiguration.Initialize();
            using var app = new Scheduler();
            if (args.Contains("--restore")) app.Restore();
            else if (args.Contains("--quick-panel")) app.ShowQuickPanel();
            else if (!args.Contains("--background")) app.ShowWindow();
            Application.Run(app);
            return 0;
        }
        catch (Exception e)
        {
            Log(e.ToString());
            if (!args.Any(a => a.StartsWith("--self") || a == "--probe" || a == "--wallpaper-probe")) MessageBox.Show(e.Message, "Night Brightness", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
internal sealed class Scheduler : ApplicationContext
{
    readonly Nvidia nvidia = new();
    readonly WindowsTheme theme = new();
    readonly DesktopWallpaper wallpaper = new();
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    readonly ToolStripMenuItem trayStatus = new("Starting...") { Enabled = false };
    MainWindow? window;
    QuickPanel? quickPanel;
    DateTime lastApplied = DateTime.MinValue, retryAfter = DateTime.MinValue, lastError = DateTime.MinValue;
    DateTime lastLocationAttempt = DateTime.MinValue;
    bool locationRefreshing;
    double lastValue = -1;
    DateTime? overrideUntil;
    double overrideValue;
    double? previewReturn;
    bool busy;
    public Settings Config { get; private set; }
    public string Status { get; private set; } = "Starting";
    public string? ErrorMessage { get; private set; }
    public double CurrentBrightness => lastValue;
    public int MonitorCount { get; private set; }
    public bool IsOverride => overrideUntil.HasValue;
    public DateTime? OverrideUntil => overrideUntil;
    public string? LocationStatus { get; private set; }
    public string? WallpaperError => wallpaper.ErrorMessage;
    public event Action? Changed;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public Scheduler()
    {
        Config = Settings.Load();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Night Brightness", null, (_, _) => ShowWindow());
        menu.Items.Add(trayStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restore day brightness", null, (_, _) => Restore());
        menu.Items.Add("Resume schedule", null, (_, _) => Resume());
        menu.Items.Add("Pause schedule", null, (_, _) => Pause());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Restore day brightness and exit", null, (_, _) => { if (ApplyAll(Config.DayBrightness)) ExitThread(); });
        tray = new NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!, Text = "Night Brightness", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowQuickPanel(); };
        timer.Tick += (_, _) => Tick();
        timer.Start(); Tick();
    }
    public void ShowWindow()
    {
        quickPanel?.Hide();
        if (window == null)
        {
            window = new MainWindow(this);
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window);
        }
        window.Show(); window.WindowState = System.Windows.WindowState.Normal; window.Activate();
    }
    public void ShowQuickPanel()
    {
        if (quickPanel == null)
        {
            quickPanel = new QuickPanel(this);
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(quickPanel);
        }
        quickPanel.ShowNearTray();
    }
    public void Save(Settings settings)
    {
        settings.Validate();
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var oldStartup = key.GetValue("NightBrightness");
        try
        {
            if (settings.StartAtSignIn) key.SetValue("NightBrightness", "\"" + Environment.ProcessPath + "\" --background");
            else key.DeleteValue("NightBrightness", false);
            settings.Save();
        }
        catch
        {
            if (oldStartup != null) key.SetValue("NightBrightness", oldStartup);
            else key.DeleteValue("NightBrightness", false);
            throw;
        }
        Config = settings; overrideUntil = null; previewReturn = null; lastApplied = DateTime.MinValue; retryAfter = DateTime.MinValue;
        theme.Invalidate();
        wallpaper.Invalidate();
        Tick(); Changed?.Invoke();
    }
    public void Pause() => Save(Config with { Enabled = false });
    public void Resume() => Save(Config with { Enabled = true });
    public void ReturnToSchedule()
    {
        if (!Config.Enabled) { Resume(); return; }
        overrideUntil = null; previewReturn = null; lastApplied = DateTime.MinValue;
        Tick(); Changed?.Invoke();
    }
    public void OverrideBrightness(double value)
    {
        previewReturn = null;
        overrideValue = Math.Clamp(Math.Round(value), 0, 100);
        overrideUntil = Config.NextTransition(DateTime.Now);
        ApplyAll(overrideValue); Changed?.Invoke();
    }
    public void Restore()
    {
        previewReturn = null; overrideValue = Config.DayBrightness; overrideUntil = Config.NextFade(DateTime.Now);
        ApplyAll(overrideValue); Changed?.Invoke();
    }
    public void Preview(double value)
    {
        previewReturn ??= lastValue >= 0 ? lastValue : Config.Target(DateTime.Now);
        overrideValue = Math.Clamp(value, 0, 100); overrideUntil = DateTime.Now.AddSeconds(10);
        ApplyAll(overrideValue); Changed?.Invoke();
    }
    bool Request(string name)
    {
        var path = Path.Combine(Program.Data, name + ".request");
        if (!File.Exists(path)) return false;
        File.Delete(path); return true;
    }
    void Tick()
    {
        if (busy) return;
        busy = true;
        try
        {
            if (Request("exit")) { if (ApplyAll(Config.DayBrightness)) ExitThread(); return; }
            if (Request("show")) ShowWindow();
            if (Request("quick")) ShowQuickPanel();
            if (Request("restore")) Restore();
            var now = DateTime.Now;
            wallpaper.Update(Config, now);
            if (overrideUntil <= now)
            {
                overrideUntil = null; lastApplied = DateTime.MinValue;
                if (!Config.Enabled && previewReturn.HasValue) ApplyAll(previewReturn.Value);
                previewReturn = null;
            }
            if (!Config.Enabled && !overrideUntil.HasValue)
            {
                Status = "Schedule paused"; trayStatus.Text = Status; tray.Text = "Night Brightness — paused";
                Changed?.Invoke(); return;
            }
            if (Config.SolarEnabled && Config.LocationSource == "Windows" && !locationRefreshing &&
                DateTime.UtcNow - lastLocationAttempt > TimeSpan.FromHours(6))
                _ = RefreshLocationAsync();
            if (Config.Enabled && Config.ThemeEnabled) theme.Update(now, Config.IsDark(now));
            if (now < retryAfter) return;
            double value = overrideUntil.HasValue ? overrideValue : Config.Target(now);
            if (Math.Abs(value - lastValue) < .05 && (now - lastApplied).TotalSeconds < 30)
            { Changed?.Invoke(); return; }
            ApplyAll(value);
        }
        catch (Exception e) { Error(e); }
        finally { busy = false; }
    }
    async Task RefreshLocationAsync()
    {
        locationRefreshing = true; lastLocationAttempt = DateTime.UtcNow;
        try
        {
            var position = await LocationProbe.RefreshAsync();
            if (position.HasValue && Config.SolarEnabled && Config.LocationSource == "Windows")
            {
                Config = Config with { Latitude = position.Value.Latitude, Longitude = position.Value.Longitude, LocationUpdatedUtc = DateTime.UtcNow };
                Config.Save(); lastApplied = DateTime.MinValue; theme.Invalidate();
                LocationStatus = "Windows location updated."; Changed?.Invoke();
            }
        }
        catch (Exception e)
        {
            LocationStatus = "Using the last saved location. Windows location is unavailable.";
            Program.Log("Location refresh: " + e.Message); Changed?.Invoke();
        }
        finally { locationRefreshing = false; }
    }
    bool ApplyAll(double value)
    {
        try
        {
            var monitors = nvidia.Enumerate(); MonitorCount = monitors.Count;
            if (monitors.Count == 0) throw new InvalidOperationException("No active NVIDIA monitors. Reconnect a display; the app will retry.");
            var errors = new List<string>();
            foreach (var m in monitors)
                try { nvidia.Apply(m, value); } catch (Exception e) { errors.Add(e.Message); }
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            if (Math.Abs(value-lastValue) >= .05) Program.Log($"Applied {value:F2}% to {monitors.Count} monitors.");
            lastValue = value; lastApplied = DateTime.Now; ErrorMessage = null;
            Status = overrideUntil.HasValue ? $"Preview / override until {overrideUntil:HH:mm:ss}" : Config.IsDark(DateTime.Now) ? "Night schedule active" : "Day schedule active";
            trayStatus.Text = $"{value:F1}% · {monitors.Count} monitors";
            tray.Text = $"Night Brightness: {value:F1}% / {monitors.Count} monitors";
            File.WriteAllText(Path.Combine(Program.Data, "status.json"), JsonSerializer.Serialize(new { time = DateTime.Now, brightness = value, monitors = monitors.Select(m => new { m.Id, m.Luid }), overrideUntil }));
            Changed?.Invoke(); return true;
        }
        catch (Exception e) { Error(e); return false; }
    }
    void Error(Exception e)
    {
        retryAfter = DateTime.Now.AddSeconds(10); ErrorMessage = e.Message; Status = "Waiting for display";
        trayStatus.Text = "Adjustment failed — retrying";
        if ((DateTime.Now-lastError).TotalSeconds > 60) { Program.Log(e.ToString()); lastError = DateTime.Now; }
        Changed?.Invoke();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop(); timer.Dispose();
            quickPanel?.Shutdown();
            if (window != null) { window.AllowClose = true; window.Close(); }
            tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
