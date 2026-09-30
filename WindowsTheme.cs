// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;

namespace NightBrightness;

internal sealed class WindowsTheme
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    bool? lastDark;
    DateTime retryAfter;
    DateTime nextTaskbarCheck;
    IntPtr[] lastTaskbars = [];
    Task<(bool refreshed, bool restarted)>? notification;
    bool restartPending;
    public void Invalidate() { lastDark = null; retryAfter = DateTime.MinValue; nextTaskbarCheck = DateTime.MinValue; }

    delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    static IntPtr[] Taskbars()
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) => {
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows.OrderBy(h => h.ToInt64()).ToArray();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        string lParam, uint flags, uint timeout, out UIntPtr result);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    static extern IntPtr SendThemeMessage(IntPtr window, uint message, UIntPtr wParam,
        IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    public static async Task<bool> RefreshShellAsync()
    {
        // Refresh taskbars first, so an unrelated hung app cannot delay Explorer.
        // Explorer's secondary taskbars and their controls can retain old theme handles.
        bool delivered = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(1000);
            var taskbars = Taskbars();
            delivered = taskbars.Length > 0;
            foreach (var taskbar in taskbars)
            {
                var windows = new List<IntPtr> { taskbar };
                EnumChildWindows(taskbar, (window, _) => { windows.Add(window); return true; }, IntPtr.Zero);
                foreach (var window in windows)
                {
                    bool setting = SendMessageTimeout(window, 0x001a, UIntPtr.Zero,
                        "ImmersiveColorSet", 0x0002, 250, out _) != IntPtr.Zero;
                    bool theme = SendThemeMessage(window, 0x031a, UIntPtr.Zero,
                        IntPtr.Zero, 0x0002, 250, out _) != IntPtr.Zero;
                    delivered &= setting && theme;
                }
            }
            if (attempt == 0)
            {
                SendMessageTimeout(new IntPtr(0xffff), 0x001a, UIntPtr.Zero,
                    "ImmersiveColorSet", 0x0002, 100, out _);
                SendMessageTimeout(new IntPtr(0xffff), 0x001a, UIntPtr.Zero,
                    "TraySettings", 0x0002, 100, out _);
            }
            if (attempt == 2)
                Program.Log($"Taskbar theme refresh: {taskbars.Length} taskbars; notifications {(delivered ? "delivered" : "incomplete; retry scheduled")}.");
        }
        return delivered;
    }

    public static bool IsDark(DateTime local) => local.TimeOfDay < TimeSpan.FromHours(5)
        || local.TimeOfDay >= TimeSpan.FromHours(21);

    internal static bool NeedsExplorerRestart(bool? previousDark, bool dark, object? appsLight, object? systemLight)
    {
        int light = dark ? 0 : 1;
        return (previousDark.HasValue && previousDark.Value != dark)
            || !Equals(appsLight, light) || !Equals(systemLight, light);
    }

    static async Task<(bool refreshed, bool restarted)> ApplyShellAsync(bool restart)
    {
        bool restarted = false;
        try
        {
            if (restart) { await ExplorerShell.RestartAsync(); restarted = true; }
            return (await RefreshShellAsync(), restarted);
        }
        catch (Exception error)
        {
            Program.Log("Theme shell update failed: " + error.Message);
            return (false, restarted);
        }
    }

    public void Update(DateTime now, bool dark)
    {
        if (notification != null)
        {
            if (!notification.IsCompleted) return;
            if (notification.IsCompletedSuccessfully && notification.Result.restarted) restartPending = false;
            if (!notification.IsCompletedSuccessfully || !notification.Result.refreshed)
            {
                lastDark = null;
                retryAfter = now.AddSeconds(30);
                Program.Log("Taskbar theme refresh incomplete; retrying in 30 seconds.");
            }
            notification = null;
        }
        if (now < retryAfter) return;
        if (lastDark == dark)
        {
            if (now < nextTaskbarCheck) return;
            nextTaskbarCheck = now.AddSeconds(30);
            var taskbars = Taskbars();
            if (lastTaskbars.SequenceEqual(taskbars)) return;
            lastTaskbars = taskbars;
            notification = Task.Run(() => ApplyShellAsync(false));
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            var backup = Path.Combine(Program.Data, "original-theme.json");
            if (!File.Exists(backup))
                File.WriteAllText(backup, JsonSerializer.Serialize(new {
                    AppsUseLightTheme = key.GetValue("AppsUseLightTheme"),
                    SystemUsesLightTheme = key.GetValue("SystemUsesLightTheme") }));
            int light = dark ? 0 : 1;
            restartPending |= NeedsExplorerRestart(lastDark, dark,
                key.GetValue("AppsUseLightTheme"), key.GetValue("SystemUsesLightTheme"));
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
            if (!Equals(key.GetValue("AppsUseLightTheme"), light) ||
                !Equals(key.GetValue("SystemUsesLightTheme"), light))
                throw new InvalidOperationException("Windows theme setting readback failed.");
            // Keep bounded notifications off the UI/brightness timer, and observe failures.
            lastTaskbars = Taskbars();
            nextTaskbarCheck = now.AddSeconds(30);
            bool restart = restartPending;
            notification = Task.Run(() => ApplyShellAsync(restart));
            File.WriteAllText(Path.Combine(Program.Data, "theme-status.json"),
                JsonSerializer.Serialize(new { time = now, mode = dark ? "dark" : "light", appsUseLightTheme = light, systemUsesLightTheme = light }));
            lastDark = dark;
            Program.Log($"Windows and app theme set to {(dark ? "dark" : "light")}.");
        }
        catch (Exception e)
        {
            retryAfter = now.AddSeconds(30);
            Program.Log("Windows theme update failed; retrying: " + e.Message);
        }
    }
}
