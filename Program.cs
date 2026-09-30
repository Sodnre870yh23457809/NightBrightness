// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
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
            if (args.Contains("--theme-refresh")) return Task.Run(WindowsTheme.RefreshShellAsync).GetAwaiter().GetResult() ? 0 : 1;
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
    DateTime lastMonitorScan = DateTime.MinValue;
    bool locationRefreshing;
    double lastValue = -1;
    DateTime? overrideUntil;
    double overrideValue;
    Dictionary<uint, double>? overrideValues, previewReturn;
    readonly Dictionary<uint, double> appliedValues = new();
    bool busy;
    public Settings Config { get; private set; }
    public string Status { get; private set; } = "Starting";
    public string? ErrorMessage { get; private set; }
    public double CurrentBrightness => lastValue;
    public IReadOnlyList<Nvidia.Monitor> Monitors { get; private set; } = Array.Empty<Nvidia.Monitor>();
    public double BrightnessFor(uint monitor) => appliedValues.TryGetValue(monitor, out double value) ? value : Config.Target(DateTime.Now, monitor);
    public string BrightnessSummary => appliedValues.Count == 0 ? "—" :
        appliedValues.Values.Max() - appliedValues.Values.Min() < .05 ? $"{lastValue:0.#}%" :
        $"{appliedValues.Values.Min():0.#}–{appliedValues.Values.Max():0.#}%";
    public int MonitorCount { get; private set; }
    public bool IsOverride => overrideUntil.HasValue;
    public DateTime? OverrideUntil => overrideUntil;
    public string? LocationStatus { get; private set; }
    public string? WallpaperError => wallpaper.ErrorMessage;
    public event Action? Changed;
    public void RefreshMonitors() { Monitors=nvidia.Enumerate(); MonitorCount=Monitors.Count; lastMonitorScan=DateTime.Now; }
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
        menu.Items.Add("Restore day brightness and exit", null, (_, _) => { if (ApplyTargets(m => Config.Levels(m.Luid).Day)) ExitThread(); });
        tray = new NotifyIcon { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!, Text = "Night Brightness", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowQuickPanel(); };
        timer.Tick += (_, _) => Tick();
        timer.Start(); Tick();
        try { StartupRegistration.Apply(Config.StartAtSignIn); }
        catch (Exception e) { Program.Log("Sign-in startup: " + e); }
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
        bool oldStartup = Config.StartAtSignIn;
        try
        {
            StartupRegistration.Apply(settings.StartAtSignIn);
            settings.Save();
        }
        catch
        {
            try { StartupRegistration.Apply(oldStartup); }
            catch (Exception e) { Program.Log("Startup rollback: " + e); }
            throw;
        }
        Config = settings; overrideUntil = null; overrideValues = null; previewReturn = null; lastApplied = DateTime.MinValue; retryAfter = DateTime.MinValue;
        theme.Invalidate();
        wallpaper.Invalidate();
        Tick(); Changed?.Invoke();
    }
    public void Pause() => Save(Config with { Enabled = false });
    public void Resume() => Save(Config with { Enabled = true });
    public void ReturnToSchedule()
    {
        if (!Config.Enabled) { Resume(); return; }
        overrideUntil = null; overrideValues = null; previewReturn = null; lastApplied = DateTime.MinValue;
        Tick(); Changed?.Invoke();
    }
    public void OverrideBrightness(double value, uint? monitor = null)
    {
        if (previewReturn != null) overrideValues = null;
        previewReturn = null;
        overrideValue = Math.Clamp(Math.Round(value), 0, 100);
        if (monitor.HasValue)
        {
            overrideValues ??= new();
            overrideValues[monitor.Value] = overrideValue;
        }
        else overrideValues = null;
        overrideUntil = Config.NextTransition(DateTime.Now);
        ApplyCurrent(DateTime.Now); Changed?.Invoke();
    }
    public void Restore()
    {
        previewReturn = null; overrideValue = Config.DayBrightness; overrideUntil = Config.NextFade(DateTime.Now);
        overrideValues = Monitors.ToDictionary(m => m.Luid, m => (double)Config.Levels(m.Luid).Day);
        ApplyCurrent(DateTime.Now); Changed?.Invoke();
    }
    public void Preview(Settings draft)
    {
        previewReturn ??= Monitors.ToDictionary(m => m.Luid, m => BrightnessFor(m.Luid));
        overrideValue = draft.NightBrightness;
        overrideValues = Monitors.ToDictionary(m => m.Luid, m => (double)draft.Levels(m.Luid).Night);
        overrideUntil = DateTime.Now.AddSeconds(10);
        ApplyCurrent(DateTime.Now); Changed?.Invoke();
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
            if (Request("exit")) { if (ApplyTargets(m => Config.Levels(m.Luid).Day)) ExitThread(); return; }
            if (Request("show")) ShowWindow();
            if (Request("quick")) ShowQuickPanel();
            if (Request("restore")) Restore();
            var now = DateTime.Now;
            wallpaper.Update(Config, now);
            if (overrideUntil <= now)
            {
                overrideUntil = null; overrideValues = null; lastApplied = DateTime.MinValue;
                if (!Config.Enabled && previewReturn != null)
                    ApplyTargets(m => previewReturn.TryGetValue(m.Luid, out double prior) ? prior : Config.Target(now, m.Luid));
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
            ApplyCurrent(now);
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
    bool ApplyCurrent(DateTime now) => ApplyTargets(m => overrideUntil.HasValue
        ? overrideValues == null ? overrideValue : overrideValues.TryGetValue(m.Luid, out double value) ? value : Config.Enabled ? Config.Target(now, m.Luid) : BrightnessFor(m.Luid)
        : Config.Target(now, m.Luid));
    bool ApplyTargets(Func<Nvidia.Monitor, double> target)
    {
        try
        {
            if (Monitors.Count == 0 || (DateTime.Now-lastMonitorScan).TotalSeconds>=30) RefreshMonitors();
            var monitors = Monitors.ToList();
            if (monitors.Count == 0) throw new InvalidOperationException("No active NVIDIA monitors. Reconnect a display; the app will retry.");
            var values = monitors.ToDictionary(m => m.Luid, target);
            if (values.Count == appliedValues.Count && values.All(p => appliedValues.TryGetValue(p.Key, out double previous) && Math.Abs(previous-p.Value)<.05)
                && (DateTime.Now-lastApplied).TotalSeconds<30)
            { Changed?.Invoke(); return true; }
            var errors = new List<string>();
            foreach (var m in monitors)
                try { nvidia.Apply(m, values[m.Luid]); } catch (Exception e) { errors.Add(e.Message); }
            if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
            if (values.Any(p => !appliedValues.TryGetValue(p.Key, out double previous) || Math.Abs(previous-p.Value)>=.05))
                Program.Log("Applied " + string.Join("; ", monitors.Select(m => $"{m.Name}: {values[m.Luid]:F2}%")) + ".");
            appliedValues.Clear(); foreach(var pair in values) appliedValues.Add(pair.Key, pair.Value);
            lastValue = values.Values.Average(); lastApplied = DateTime.Now; ErrorMessage = null;
            Status = overrideUntil.HasValue ? $"Preview / override until {overrideUntil:HH:mm:ss}" : Config.IsDark(DateTime.Now) ? "Night schedule active" : "Day schedule active";
            trayStatus.Text = $"{BrightnessSummary} · {monitors.Count} monitors";
            tray.Text = $"Night Brightness: {BrightnessSummary} / {monitors.Count} monitors";
            File.WriteAllText(Path.Combine(Program.Data, "status.json"), JsonSerializer.Serialize(new { time = DateTime.Now, brightness = lastValue,
                monitors = monitors.Select(m => new { m.Id, m.Luid, m.Name, brightness = values[m.Luid] }), overrideUntil }));
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
