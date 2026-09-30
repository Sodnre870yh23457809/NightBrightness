// SPDX-License-Identifier: GPL-3.0-or-later
// Gamma interface/calculation adapted from nvBrightness, Copyright (c) 2025
// Pete Batard <pete@akeo.ie>, https://github.com/pbatard/nvBrightness
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace NightBrightness;
internal sealed class Nvidia
{
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvapi64.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr nvapi_QueryInterface(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Init();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumGpu([Out] IntPtr[] handles, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Displays(IntPtr gpu, IntPtr displays, ref uint count, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Luid(uint display, uint mode, [Out] uint[] guid);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetGamma(uint display, IntPtr ramp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DisplayId([MarshalAs(UnmanagedType.LPStr)] string name, out uint id);
    readonly EnumGpu enumGpu = Get<EnumGpu>(0xE5AC921F);
    readonly Displays displays = Get<Displays>(0x0078DBA2);
    readonly Luid luid = Get<Luid>(0xD4A859F2);
    readonly SetGamma setGamma = Get<SetGamma>(0x7082A053);
    static T Get<T>(uint id) where T : Delegate
    {
        var p = nvapi_QueryInterface(id);
        if (p == IntPtr.Zero) throw new InvalidOperationException($"NVIDIA interface 0x{id:X} unavailable.");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }
    static void Check(int result, string operation)
    {
        if (result != 0) throw new InvalidOperationException($"{operation}: NVIDIA error {result}.");
    }
    public Nvidia() { Check(Get<Init>(0x0150E828)(), "Initialize"); }
    internal record Monitor(uint Id, uint Luid, float[] Color)
    {
        public string Name { get; init; } = "NVIDIA monitor";
        public int Left { get; init; } = int.MaxValue;
        public int Top { get; init; } = int.MaxValue;
        public string Key => $@"Software\NVIDIA Corporation\Global\NVTweak\Devices\{Luid}-0\Color";
    }
    public List<Monitor> Enumerate()
    {
        var result = new List<Monitor>();
        var gpu = new IntPtr[64];
        Check(enumGpu(gpu, out var n), "List GPUs");
        for (int g = 0; g < n; g++)
        {
            uint count = 0;
            Check(displays(gpu[g], IntPtr.Zero, ref count, 0), "Count monitors");
            if (count == 0) continue;
            var buffer = Marshal.AllocHGlobal(checked((int)count * 16));
            try
            {
                for (int i = 0; i < count * 4; i++) Marshal.WriteInt32(buffer, i * 4, 0);
                for (int i = 0; i < count; i++) Marshal.WriteInt32(buffer, i * 16, 16 | (3 << 16));
                Check(displays(gpu[g], buffer, ref count, 0), "List monitors");
                for (int i = 0; i < count; i++)
                {
                    if ((Marshal.ReadInt32(buffer, i * 16 + 12) & 4) == 0) continue;
                    uint id = unchecked((uint)Marshal.ReadInt32(buffer, i * 16 + 8));
                    var guid = new uint[4];
                    Check(luid(id, 1, guid), "Identify monitor");
                    var monitor = new Monitor(id, guid[1] ^ 0xf0000000, new float[9]);
                    using var key = Registry.CurrentUser.OpenSubKey(monitor.Key);
                    for (int c = 0; c < 9; c++)
                    {
                        var value = key?.GetValue((3538946 + c).ToString());
                        monitor.Color[c] = value is int v && v > 0 && v <= 1000 ? v : 100;
                    }
                    if (result.All(m => m.Id != id)) result.Add(monitor);
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        // Match Windows display positions to NVAPI IDs, not enumeration order.
        var mapAddress = nvapi_QueryInterface(0xae457190);
        if (mapAddress != IntPtr.Zero)
        {
            var map = Marshal.GetDelegateForFunctionPointer<DisplayId>(mapAddress);
            foreach (var screen in Screen.AllScreens)
                if (map(screen.DeviceName, out uint id) == 0)
                {
                    int index = result.FindIndex(m => m.Id == id);
                    if (index >= 0) result[index] = result[index] with {
                        Name = $"Display {screen.DeviceName.Replace(@"\\.\DISPLAY", "")}" + (screen.Primary ? " · main" : ""),
                        Left = screen.Bounds.Left, Top = screen.Bounds.Top };
                }
        }
        result = result.OrderBy(m => m.Left).ThenBy(m => m.Top).ThenBy(m => m.Luid).ToList();
        if (result.Count == 2 && result[0].Left != result[1].Left)
        {
            result[0] = result[0] with { Name = "Left · " + result[0].Name };
            result[1] = result[1] with { Name = "Right · " + result[1].Name };
        }
        return result;
    }
    internal static float Gamma(int index, float brightness, float contrast, float gamma)
    {
        float c = (contrast - 100) / 100;
        float x = index / 1023f - .5f;
        c = c <= 0 ? (c + 1) * x : x / (1 - c);
        return (float)Math.Clamp(Math.Pow(Math.Clamp((brightness - 100) / 100 + c + .5f, 0, 1), 100.0 / gamma), 0, 1);
    }
    public void Apply(Monitor monitor, double percentage)
    {
        if (!double.IsFinite(percentage) || percentage < 0 || percentage > 100) throw new ArgumentOutOfRangeException(nameof(percentage));
        float brightness = (float)(80 + .4 * percentage);
        const int bytes = 4 + 3072 * 4 + 4;
        var ptr = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.WriteInt32(ptr, bytes | (1 << 16));
            var ramp = new float[3072];
            for (int i = 0; i < 1024; i++)
                for (int channel = 0; channel < 3; channel++)
                    ramp[3 * i + channel] = Gamma(i, brightness, monitor.Color[3 + channel], monitor.Color[6 + channel]);
            Marshal.Copy(ramp, 0, ptr + 4, ramp.Length);
            Marshal.WriteInt32(ptr, bytes - 4, 1);
            Check(setGamma(monitor.Id, ptr), $"Set brightness on {monitor.Id:X}");
            using var key = Registry.CurrentUser.CreateSubKey(monitor.Key);
            for (int c = 0; c < 3; c++) key.SetValue((3538946 + c).ToString(), (int)Math.Round(brightness), RegistryValueKind.DWord);
            key.SetValue("NvCplGammaSet", 1, RegistryValueKind.DWord);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
