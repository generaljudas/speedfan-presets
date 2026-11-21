# Implementation notes

## Source map

- `src/FanPresets.cs` contains the Windows Forms panel, tray menu, remembered selection, single-instance coordination and scheduled task setup.
- `src/SpeedFanControl.cs` locates the installed SpeedFan process and its native Readings controls, validates the expected setup, applies percentages and handles tray minimization.
- `src/Presets.manifest` uses `asInvoker`. The normal desktop entry requests the preconfigured elevated launcher task.
- `assets/fan.ico` is the app icon.
- `build.ps1` compiles the application against the Windows .NET Framework libraries.

## Fan control

The helper locates SpeedFan's `TForm1`, `TPageControl`, `TTabSheet` named Readings and five active `TRxSpinEdit` controls. In the validated layout, PWM 1–3 occupy the left column and PWM 4–5 the right column. The source checks the layout and corresponding names in `speedfansens.cfg` before editing.

Writes use Windows `WM_SETTEXT` messages to those existing edit controls, allowing SpeedFan's change handlers to perform hardware control. Messages have timeouts. After a short refresh interval, the helper verifies the displayed percentages. If a write or verification fails partway, it attempts to restore all attempted controls to their prior percentages and reports restoration failure explicitly when necessary. This verifies SpeedFan's displayed settings, rather than measuring physical fan RPM.

Startup waits up to 35 seconds for SpeedFan's controls. Configuration reads retry temporary file-sharing conflicts. Multiple SpeedFan instances are rejected. Tray minimization sends `SC_MINIMIZE` to SpeedFan's Delphi `TApplication` window, following its native application minimize route. A visibility repair handles the leftover small window produced by an earlier launcher that used generic Windows minimization.

## Permissions and persistence

Setup needs administrator approval to install the executable in Program Files and register elevated tasks. The on-demand task runs with the current user's interactive token and highest available privileges. Its action is fixed to the protected installed executable with `--open`. The account has read and execute access to the task, and administrators can modify its action.

The optional sign-in task calls that executable with `--startup`, with a five-second logon delay. It applies Normal only when it starts a new helper instance. Both tasks use account-specific names formed from the current user's Windows SID. The application computes that value during setup.

An account-scoped named mutex prevents duplicate helpers. A named event lets an elevated launcher reveal the existing panel. Selection is saved under `HKCU\Software\SpeedFanPresets`. Ordinary preset operations use SpeedFan's controls, with configuration files used for validation.

## Validation performed

The original configured Windows PC was used for live integration checks.

- Applied 88% and 0%, read back all five fields, and restored prior settings.
- Executed the sign-in task on demand while SpeedFan was closed and verified launch and Normal application.
- Checked button color and accessible selection state for Normal, Performance and None.
- Launched the desktop shortcut from a non-elevated process, both with the helper closed and already running.
- Reproduced the earlier bottom-left minimized window, verified recovery into the tray state, and tested fresh SpeedFan launch plus subsequent preset changes.
- Compared the installed executable and packaged executable hashes.

A full Windows reboot and compatibility with other hardware configurations have not been verified. The shipped repository excludes development probes that changed fan settings and restarted SpeedFan during testing.
