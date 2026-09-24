# UI release verification — 2026-09-22

- .NET Release build and publish succeeded without warnings.
- Default and custom schedules, cross-midnight operation, 901 fade samples,
  theme boundaries, invalid configuration, serialization, next-fade calculation,
  and NVIDIA gamma checks passed.
- Inspected actual Schedule and Preferences windows using Windows accessibility
  and screenshots. Polished caption color, scrollbar, and full-resolution icon.
- Edited fade from 15 to 30 minutes through the UI; timeline changed to 20:30.
  Saved and checked settings.json. Entering 0 was rejected and Save disabled.
  Restored and saved the requested 15-minute fade.
- Restarted the application and verified the saved schedule loaded.
- Changed the draft night slider to 10%, clicked Preview, and confirmed both
  NVIDIA displays applied 10%. After ten seconds the app returned to 0% with
  no override. Reset the draft; saved night brightness remains 0%.
- Verified --background starts one process without a window; launching normally
  opens that same process's settings window.
- Closed the window with its title-bar button; background process remained.
  Reopened and checked 55% day, 0% night, 05:00 morning, 21:00 night, 15-minute fade.
- Startup entry uses --background. Desktop shortcuts have the custom icon.
- Final state: settings window open; one running scheduler; both monitors at 0%.

Previous binaries are retained in the local application's backup-ui directory.

## Wallpaper update — 2026-09-23

- Release build and publish succeeded without warnings.
- Self-test passed fixed and sunrise/sunset wallpaper transition checks and
  rejected incomplete or relative wallpaper paths.
- Read-only `--wallpaper-probe` successfully reached Windows'
  `IDesktopWallpaper` interface. No wallpaper image was applied during validation.
- Installed DLL matches the staged build. The application restarted, the
  current wallpaper and saved Trondheim schedule stayed unchanged, and the
  live status still reports both NVIDIA monitors at 65%.
- The Windows UI capture helper failed to start during this update, so the
  new Preferences card was checked by XAML compilation and live window
  startup, without a captured visual inspection.

## Fast sign-in startup — 2026-09-24

- Release build and built-in self-test passed.
- Installed the new binary and registered a per-user Windows logon task with no
  trigger delay, priority 4, interactive desktop access, and battery operation.
- Kept the existing Run entry as a fallback; the single-instance guard prevents
  duplicate schedulers.
- Launched the installed app through Task Scheduler. One process stayed running,
  the saved settings remained intact, and both NVIDIA monitors were at 65%.
- An actual sign-out/sign-in has not been performed during this validation.
