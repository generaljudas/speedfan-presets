# SpeedFan Presets

A small Windows tray app with one-click fan presets for an existing SpeedFan installation.

| Selection | Effect on Sys, CPU, Aux, Aux1 and Aux2 |
| --- | --- |
| Normal | Sets all five controls to 0% |
| Performance | Sets all five controls to 88% |
| None | Clears the app's selected preset and leaves the current speeds in place |

The selected button turns blue and displays a checkmark. The app remembers its selection between launches. After applying a preset, SpeedFan moves into the notification area by the clock.

## Compatibility

This version was built and verified for one configured Windows gaming PC. It requires 64-bit Windows, .NET Framework 4.x, and SpeedFan 4.52 with executable file version `4.52.0.462`, installed in `C:\Program Files (x86)\SpeedFan`.

SpeedFan must already control the hardware correctly. The five active PWM controls must be numbered 1 through 5 and named `Sys`, `CPU`, `Aux`, `Aux1`, and `Aux2`, in that order. The app validates the configuration and Readings layout before writing values. Keep SpeedFan's **Automatic fan speed** option off and its **Readings** tab selected.

The 0% setting includes the CPU control and may stop connected fans. These presets come from the original machine's manually used settings. Check cooling suitability for your hardware before using Normal. Thermal monitoring and temperature-based overrides are outside the app's functionality.

## Install

1. Download [SpeedFan Presets.exe](dist/SpeedFan%20Presets.exe) using GitHub's download button, or build it from source.
2. Open the executable normally from your Windows account. First-time setup requests administrator approval and installs it under `C:\Program Files\SpeedFan Presets`.
3. Use the new **SpeedFan Presets** desktop shortcut. Setup registers a Windows scheduled task that opens the elevated panel without repeated approval prompts.
4. Select **Start in Normal (0%) at Windows sign-in** if automatic startup is wanted. A fresh installation leaves this option off until selected.

With startup enabled, the task waits five seconds after sign-in, starts SpeedFan as needed, waits for its readings, applies 0% to all five controls, and sends SpeedFan to the tray. Loading and hardware detection add time, and SpeedFan may appear briefly. This runs after Windows sign-in. Fan behavior during firmware startup and before sign-in is controlled by the computer's existing settings.

SpeedFan is a separate dependency maintained by its author. Obtain it through the author's website. [SpeedFan website](https://www.almico.com/speedfan.php)

## Daily use

- Open the desktop shortcut or click the preset app's tray icon.
- Choose **Normal** for 0% or **Performance** for 88%.
- Choose **None** to clear the selection while keeping current speeds.
- Right-click the tray icon for the same presets and **Exit**.
- Closing the panel hides it in the tray. **Exit** closes the preset helper. SpeedFan continues running with its current settings.

The app tracks preset names itself. It reads SpeedFan's percentages to verify writes and checks them again when the panel is opened or activated. If they differ from the remembered preset, selection becomes None. A matching set of percentages alone does not select a preset. Checks happen during these interactions, so manual changes in SpeedFan may take until the next panel activation to appear.

## Build

Run this from the repository directory in PowerShell.

```powershell
.\build.ps1
```

The script uses the 64-bit .NET Framework C# compiler bundled with Windows installations that include it. Output is `build\SpeedFan Presets.exe`. The checked-in `dist` executable is the verified build installed on the original machine. Its checksum is in [SHA256SUMS.txt](dist/SHA256SUMS.txt). Compiler-generated assembly identifiers can make a fresh build's checksum differ.

## Updates and removal

For an update, use **Exit** in the preset app's tray menu, then run the new executable with `--setup` from an elevated PowerShell window. This replaces the installed helper and re-registers its desktop launcher. Run setup under the Windows account that will use the app.

```powershell
& '.\build\SpeedFan Presets.exe' --setup
```

To remove it, turn off its sign-in option, exit the helper, and use Windows Task Scheduler to delete the two tasks whose names begin with `SpeedFan Presets - ` and `Open SpeedFan Presets - ` for your account. Delete the `C:\Program Files\SpeedFan Presets` directory and the **SpeedFan Presets** desktop shortcut. The saved selection is a DWORD named `SelectedPercent` under `HKEY_CURRENT_USER\Software\SpeedFanPresets`, which can also be removed. SpeedFan itself has its own installation and settings.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| Version or configuration mismatch | Check the exact SpeedFan build, installation path, active PWM order and five names above. |
| More than one SpeedFan window | Exit the extra SpeedFan process and retry. |
| Readings unavailable | Select Readings and close any SpeedFan dialog. Allow hardware detection to finish. |
| Fan values change after applying | Check Automatic fan speed and other hardware-control software. The helper verifies the displayed values after about 2.2 seconds. |
| Windows prompts every launch | Open the SpeedFan Presets shortcut. Directly opening SpeedFan can still prompt. Re-run setup if the `Open SpeedFan Presets` task is missing. |
| Startup does not apply Normal | Check the sign-in checkbox and the matching enabled task in Windows Task Scheduler. The task runs in an interactive signed-in session. |
| Preset succeeded but tray minimization failed | Read the error and use SpeedFan's Minimize button. The fan settings may already have been applied. |

## Implementation and validation

See [implementation notes](docs/implementation.md) for the Windows integration, permission model and test coverage.

