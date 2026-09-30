// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NightBrightness;

internal static class ExplorerShell
{
    [DllImport("user32.dll")]
    static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    static int ShellProcessId()
    {
        var window = GetShellWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out uint processId);
        return (int)processId;
    }

    public static async Task RestartAsync()
    {
        int previous = ShellProcessId();
        if (previous != 0)
        {
            using var shell = Process.GetProcessById(previous);
            using var current = Process.GetCurrentProcess();
            if (!shell.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)
                || shell.SessionId != current.SessionId)
                throw new InvalidOperationException("The desktop shell is not this session's Explorer.");
            shell.Kill();
            await shell.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        // Windows normally restarts its shell itself. Avoid launching a duplicate.
        for (int i = 0; i < 12 && ShellProcessId() == 0; i++) await Task.Delay(250);
        if (ShellProcessId() == 0)
        {
            using var launched = Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        for (int i = 0; i < 40; i++)
        {
            int active = ShellProcessId();
            if (active != 0 && active != previous)
            {
                Program.Log($"Explorer restarted after theme change: {previous} -> {active}.");
                return;
            }
            await Task.Delay(250);
        }
        throw new InvalidOperationException("Explorer did not recreate the desktop shell; retrying.");
    }
}
