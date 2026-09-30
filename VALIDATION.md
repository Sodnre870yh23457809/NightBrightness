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

## Secondary taskbar theme refresh — 2026-09-30

- Windows theme registry values were already dark when the right-monitor issue
  was reported. Explorer had one primary and one secondary taskbar.
- Added explicit setting/theme notifications to both taskbars and their child
  controls, with delayed refreshes and retries when delivery fails. Recreated
  taskbar handles are detected every 30 seconds while theme scheduling is active.
- Release build/publish succeeded without warnings; existing self-tests passed.
- Live `--theme-refresh` exited successfully and logged delivery to both taskbars.
- Installed the published build, retained the previous binaries in a timestamped
  local backup, verified matching DLL hashes, and restarted the background app.
- Saved settings remained unchanged; the running app reapplied dark mode and
  50% brightness to both monitors and logged successful taskbar notification delivery.
- The primary taskbar was visibly dark; a later screenshot confirmed the
  secondary taskbar remained light despite successful notification delivery.
- Applying a matching dark theme through Windows' private theme manager did
  not fix the secondary taskbar. Removed that unsuccessful code experiment.
- Restarted Explorer once to recreate both taskbars. The user confirmed that
  the right taskbar was then dark. The running app detected the recreated
  taskbars and successfully delivered its refresh notifications.
- Display reconnect and the next scheduled light/dark transition remain untested;
  notification delivery alone does not prove the visual theme is correct.

## Restart Explorer on theme changes — 2026-09-30

- At the user's request, actual light/dark transitions now restart the current
  session's desktop Explorer process after the theme registry values are written.
  The scheduler waits asynchronously for the replacement desktop shell, then
  refreshes both taskbars. Windows' automatic shell restart takes precedence;
  the app launches Explorer only when the shell has not returned on its own.
- Matching-theme startup, saving settings, and detecting recreated taskbars do
  not restart Explorer. Failed restarts retry; successful restarts are not repeated
  merely because a subsequent taskbar notification fails.
- Release build and existing self-tests, including restart decision cases, passed.
- A temporary integration harness exercised the real WindowsTheme.Update path:
  matching startup retained PID 3712, dark-to-light restarted to PID 30384,
  same-theme invalidation/save retained that PID, and light-to-dark restarted to
  PID 36728. The original dark theme was restored in a finally block.
- Published and installed the update with matching DLL hashes, preserved settings
  and backed up the prior installed binaries, then relaunched the background app.

## Per-monitor brightness — 2026-09-30

- Added optional independent day/night profiles keyed by NVIDIA device identifiers,
  while retaining the shared schedule, fade times, and shared-value fallback.
- Windows display names are mapped to NVIDIA display IDs; the real displays were
  identified as Left (Display 1, main, device 1019085556) and Right (Display 2,
  device 2062529264). Sorting does not assign saved profiles by enumeration order.
- Added Schedule controls, individual timeline curves, per-monitor live status,
  a refresh button, and All/Left/Right choices in the quick tray panel.
- Release build and expanded self-tests passed: distinct levels and fade midpoints,
  profile JSON persistence, shared fallback, and invalid individual settings.
- A temporary harness drove the real WPF controls and Save handler. Saved 45%
  Left / 55% Right, then independently overrode Left to 40% and Right to 60%.
  NVIDIA registry readback confirmed the other monitor was unaffected.
- Return to schedule restored 45% / 55%. An unsaved 35% / 60% night preview returned
  to the saved values after ten seconds. Disabling individual mode restored the
  original shared 50% night brightness on both monitors.
- Inspected rendered settings and quick-panel images. Verified the dark monitor
  selector renders; installed the final build with matching DLL hashes and retained
  the previous app in a timestamped backup.
- Restored original settings after live testing. Individual mode is initially off;
  users choose their desired levels and save. Startup registrations point to the
  installed app, and the background scheduler continues at 50% on both displays.
- Reconnect behavior is covered by identifier-based lookup and a separate
  30-second monitor scan; a physical disconnect/reconnect was not performed.
