// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NightBrightness;

internal sealed class WindowsTheme
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    bool? lastDark;
    DateTime retryAfter;
    public void Invalidate() { lastDark = null; retryAfter = DateTime.MinValue; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam,
        string lParam, uint flags, uint timeout, out UIntPtr result);

    public static bool IsDark(DateTime local) => local.TimeOfDay < TimeSpan.FromHours(5)
        || local.TimeOfDay >= TimeSpan.FromHours(21);

    public void Update(DateTime now, bool dark)
    {
        if (lastDark == dark || now < retryAfter) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            var backup = Path.Combine(Program.Data, "original-theme.json");
            if (!File.Exists(backup))
                File.WriteAllText(backup, JsonSerializer.Serialize(new {
                    AppsUseLightTheme = key.GetValue("AppsUseLightTheme"),
                    SystemUsesLightTheme = key.GetValue("SystemUsesLightTheme") }));
            int light = dark ? 0 : 1;
            key.SetValue("AppsUseLightTheme", light, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", light, RegistryValueKind.DWord);
            if (!Equals(key.GetValue("AppsUseLightTheme"), light) ||
                !Equals(key.GetValue("SystemUsesLightTheme"), light))
                throw new InvalidOperationException("Windows theme setting readback failed.");
            // Notify Windows and apps without blocking the brightness timer on hung windows.
            _ = Task.Run(() => {
                SendMessageTimeout(new IntPtr(0xffff), 0x001a, UIntPtr.Zero,
                    "ImmersiveColorSet", 0x0002, 100, out _);
                SendMessageTimeout(new IntPtr(0xffff), 0x001a, UIntPtr.Zero,
                    "TraySettings", 0x0002, 100, out _);
            });
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
