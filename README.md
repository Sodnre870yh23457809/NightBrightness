# Night Brightness

A Windows application with an interactive settings window and tray controls.
Installed in
`%LOCALAPPDATA%\NightBrightness\app` and started automatically at sign-in.

## Settings window

Open **Night Brightness** on the desktop, or double-click its moon-and-sun tray
icon. Schedule lets you set day/night brightness, morning and night times, and
a 1–180 minute fade. The timeline updates as you edit. **Save changes** applies
and saves the configuration; **Reset changes** returns to saved values.
**Preview night** applies the selected brightness for 10 seconds, then returns
to the saved schedule (or the prior brightness when paused).

Preferences includes Windows theme and desktop background switching, plus
automatic sign-in startup.
Closing the window hides it to the tray. Launching the app again reopens the
same window, without starting another scheduler. Startup uses `--background`
so it does not open the window at sign-in.

The saved configuration is in `settings.json`. The original schedule is:

| Local PC time | NVIDIA brightness |
| --- | --- |
| 05:00–20:45 | 55% |
| 20:45–21:00 | Gradual linear fade from 55% to 0%, updated each second |
| 21:00–05:00 | 0% |

By default, Windows and apps switch to **dark mode at 21:00** and **light mode at 05:00**.
This also applies the correct theme when the app starts or the PC resumes.
Apps must follow the Windows theme to reflect this preference; apps with their
own fixed theme settings may not change, and some may need reopening.
The app broadcasts the theme change to Windows and running apps.

It uses NVIDIA's driver gamma correction interface, with NVIDIA Control Panel's
brightness scale. **0% is NVIDIA's minimum brightness, not a black/off screen.**
Existing per-channel contrast and gamma values are retained. All active NVIDIA
displays are adjusted, including both currently connected monitors.

The PC must be on and signed in for the app to run. After sleep or a display
reconnection, the current scheduled value is reapplied within about 30 seconds.
It follows Windows local time, including daylight-saving changes; it does not
wake the PC at 05:00. Startup after 21:00 immediately applies the night value.

### Sunrise and sunset

Turn on **Follow sunrise and sunset** in Schedule, search for a city and choose
the matching result, or click **Use Windows location**. Windows may ask for
location access. Review the displayed sunrise and sunset times, then click
**Save changes**. Day brightness starts at sunrise;
the configured fade reaches night brightness at sunset. Windows light/dark mode
uses the same sunrise/sunset times when theme switching is enabled.

You can enter latitude and longitude manually if Windows location is unavailable.
The city search contacts Open-Meteo only when you click **Search city**, sending
the place name you typed. The PC's public IP is visible to that service as with
any web request. City search results are based on GeoNames data.
Coordinates stay in `settings.json` on this PC. With Windows location, the app
refreshes its saved position about every six hours; it continues using the last
saved position if location is temporarily unavailable. Sunrise and sunset are
calculated locally using NOAA's solar equations. Near the poles, when there is
no sunrise or sunset on a given day, the fixed morning and night times apply.
The displayed times use the PC's local time zone. A city in another time zone
will therefore show its events in the PC time zone. The fixed schedule remains
available by switching off **Follow sunrise and sunset**.

### Desktop background

In Preferences, choose a **day image** and **night image**, then turn on
**Switch desktop background** and save. The chosen images appear as previews
before saving. The app applies the same image to both monitors at the morning
and night transitions, using sunrise and sunset when solar scheduling is on.
Wallpaper switching works independently of the Windows theme toggle.

Wallpaper switching is off until both images are chosen. The images remain
in their original folders, so keep those files accessible. If a file is
moved or removed, brightness and theme scheduling continue and the app
shows a background error. Pausing the schedule stops background changes;
turning the background option off leaves the current wallpaper in place.

### Quick tray control

Left-click the tray icon for a small brightness panel. Drag its slider to
adjust both monitors until the next scheduled transition. **Return to schedule**
removes the override immediately. **Open settings** opens the full window.
Right-click the icon for the existing menu, or double-click to open settings.
## Controls

Right-click the Night Brightness notification-area icon (possibly under the
hidden-icons arrow). Pause, resume, restore day brightness until the next evening, disable
startup, or restore day brightness and exit. Double-click the icon for settings.
Pause is saved across restarts; temporary previews and overrides last only for
the current app session. Resume cancels an override and enables the schedule.
Pause stops both schedules. The restore-brightness shortcut overrides brightness
only; the Windows theme continues following the clock. Exiting leaves the
current Windows theme in place. The next app start reapplies the scheduled theme.
The initial theme preferences are backed up in `original-theme.json`, and the
last theme update is recorded in `theme-status.json`.

Desktop shortcuts: **Night Brightness** and **Restore Day Brightness**. The
restore shortcut requests a temporary override even when the app is running.

Original color values are saved in `original-colors.json`. `status.json` gives
the last successful application; `activity.log` records changes and failures.
Driver errors are shown in the tray and retried. A successful API response and
registry readback confirm driver acceptance; physical display output has not
been measured with a colorimeter.

## Build and validation

Requires the installed .NET 10 Windows Desktop runtime (SDK for building).

```
dotnet build -c Release
NightBrightness.exe --self-test
NightBrightness.exe --probe
```

`--self-test` checks fixed and solar schedules, wallpaper transitions,
polar fallback, cross-midnight operation,
every second of the fade, invalid settings, settings serialization, theme
boundaries, gamma identity, and the NVIDIA zero mapping. `--probe` only reads monitors and
color settings. `--wallpaper-probe` only checks the Windows desktop wallpaper
interface. `--verify-driver` applies 55% to both monitors for a live check.

To uninstall, run `Uninstall.ps1` with PowerShell. It restores 55%, exits the app,
and removes its startup entry and shortcuts. Files/backups remain available.

## Driver interface and license

The legacy NVIDIA `nvcpl.dll` brightness commands returned error 7 on this PC.
The app instead uses `NvAPI_DISP_SetTargetGammaCorrection`, the interface used
by [nvBrightness](https://github.com/pbatard/nvBrightness), with its NVIDIA ramp
calculation and display-to-registry mapping adapted to C#. This interface is
undocumented and a future NVIDIA driver update could require an app update.

This application is GPL-3.0-or-later; see LICENSE.txt. Adapted portions:
Copyright (c) 2025 Pete Batard <pete@akeo.ie>. NVIDIA interface declarations in
the upstream nvapi.h are MIT licensed, Copyright (c) 2019–2025 NVIDIA CORPORATION
& AFFILIATES; see THIRD-PARTY-NOTICES.txt. Full application source is provided.
