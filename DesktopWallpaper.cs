// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;

namespace NightBrightness;

// IDesktopWallpaper applies one image to every monitor when monitorId is null.
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    [PreserveSig]
    int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
        [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

    [PreserveSig]
    int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, out IntPtr wallpaper);
}

internal sealed class DesktopWallpaper
{
    static readonly Guid ClassId = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    string? lastPath;
    DateTime retryAfter;
    string? failedPath;
    public string? ErrorMessage { get; private set; }

    static IDesktopWallpaper Create()
    {
        var type = Type.GetTypeFromCLSID(ClassId, throwOnError: true)!;
        return (IDesktopWallpaper)Activator.CreateInstance(type)!;
    }

    public static bool Probe()
    {
        var desktop = Create();
        IntPtr path = IntPtr.Zero;
        try
        {
            var result = desktop.GetWallpaper(null, out path);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            return true; // S_FALSE is valid when the monitors have different images.
        }
        finally
        {
            if (path != IntPtr.Zero) Marshal.FreeCoTaskMem(path);
            Marshal.FinalReleaseComObject(desktop);
        }
    }

    public void Invalidate()
    {
        lastPath = null;
        failedPath = null;
        retryAfter = DateTime.MinValue;
        ErrorMessage = null;
    }

    public void Update(Settings settings, DateTime now)
    {
        if (!settings.Enabled || !settings.WallpaperEnabled)
        {
            Invalidate();
            return;
        }

        var path = settings.WallpaperFor(now);
        if (string.Equals(path, lastPath, StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(path, failedPath, StringComparison.OrdinalIgnoreCase) && now < retryAfter) return;

        try
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The selected background image is missing.", path);
            var desktop = Create();
            try
            {
                Marshal.ThrowExceptionForHR(desktop.SetWallpaper(null, path));
            }
            finally { Marshal.FinalReleaseComObject(desktop); }
            lastPath = path;
            failedPath = null;
            retryAfter = DateTime.MinValue;
            ErrorMessage = null;
            Program.Log($"Applied {Path.GetFileName(path)} as the {(settings.IsDark(now) ? "night" : "day")} background on all monitors.");
        }
        catch (Exception error)
        {
            failedPath = path;
            retryAfter = now.AddMinutes(5);
            ErrorMessage = "Could not change the desktop background. Check that both image files are still available.";
            Program.Log("Wallpaper update: " + error);
        }
    }
}
