using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal static class SpeedFanControl
{
    internal static readonly string[] FanNames = { "Sys", "CPU", "Aux", "Aux1", "Aux2" };
    internal static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "SpeedFan");
    private static readonly string SpeedFanPath = Path.Combine(InstallDirectory, "speedfan.exe");

    internal static int[] Read()
    {
        IntPtr main;
        return ReadValues(WaitForReadings(false, out main));
    }

    internal static void Apply(int percent)
    {
        IntPtr main;
        var controls = WaitForReadings(true, out main);
        var previous = ReadValues(controls);
        WriteAndVerify(controls, Enumerable.Repeat(percent, FanNames.Length).ToArray(), previous);
        MinimizeToTray(main);
    }

    internal static void Restore(int[] values)
    {
        IntPtr main;
        var controls = WaitForReadings(false, out main);
        WriteAndVerify(controls, values, ReadValues(controls));
    }

    private static void WriteAndVerify(IntPtr[] controls, int[] values, int[] previous)
    {
        var attempted = new List<int>();
        try
        {
            for (int i = 0; i < controls.Length; i++)
            {
                if (previous[i] == values[i]) continue;
                attempted.Add(i);
                SetValue(controls[i], values[i]);
            }

            // Allow SpeedFan's own refresh loop to process the edits before reporting success.
            Thread.Sleep(2200);
            if (!ReadValues(controls).SequenceEqual(values))
                throw new InvalidOperationException("SpeedFan changed one or more values. Check that Automatic fan speed is off.");
        }
        catch (Exception error)
        {
            bool restored = true;
            foreach (int index in attempted)
            {
                try { SetValue(controls[index], previous[index]); }
                catch { restored = false; }
            }
            try { restored &= ReadValues(controls).SequenceEqual(previous); }
            catch { restored = false; }

            throw new InvalidOperationException(error.Message + (restored
                ? " Previous values restored."
                : " Check the five values in SpeedFan now."), error);
        }
    }

    private static void MinimizeToTray(IntPtr main)
    {
        if (!IsWindowVisible(main)) return;

        uint processId;
        GetWindowThreadProcessId(main, out processId);
        IntPtr application = IntPtr.Zero;
        EnumWindows((window, ignored) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && ClassName(window) == "TApplication") application = window;
            return true;
        }, IntPtr.Zero);
        if (application == IntPtr.Zero)
            throw new InvalidOperationException("Fan speeds were set, but SpeedFan's application window was unavailable.");

        // SpeedFan's Minimize action dispatches SC_MINIMIZE to its Delphi application window.
        // Its tray component handles that command and hides the Readings window.
        IntPtr result;
        if (SendCommand(application, 0x0112, (IntPtr)0xF020, IntPtr.Zero, 0x22, 1500, out result) == IntPtr.Zero)
            throw new InvalidOperationException("Fan speeds were set, but SpeedFan did not respond to Minimize.");

        // The old launcher could leave the form visible after the application was already
        // minimized to its tray icon. Repair that leftover native window visibility.
        if (IsIconic(application) && IsWindowVisible(main)) ShowWindow(main, 0);

        var clock = Stopwatch.StartNew();
        while (IsWindowVisible(main) && clock.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(50);
        if (IsWindowVisible(main))
            throw new InvalidOperationException("Fan speeds were set, but SpeedFan did not hide in the notification area.");
    }

    private static IntPtr[] WaitForReadings(bool startIfNeeded, out IntPtr mainWindow)
    {
        mainWindow = IntPtr.Zero;
        VerifyConfiguration();
        var clock = Stopwatch.StartNew();
        bool startRequested = false;
        string issue;
        do
        {
            var windows = MainWindows();
            if (windows.Count > 1)
                throw new InvalidOperationException("More than one SpeedFan window is open. Exit the extra copy and try again.");

            if (windows.Count == 1)
            {
                var main = windows[0];
                try
                {
                    var editors = FindReadings(main);
                    if (editors != null)
                    {
                        ReadValues(editors);
                        mainWindow = main;
                        return editors;
                    }
                    issue = "SpeedFan's five Readings fields did not become available. Close any SpeedFan dialog and try again.";
                }
                catch (InvalidOperationException error)
                {
                    if (!startIfNeeded) throw;
                    issue = error.Message;
                }
            }
            else
            {
                var openProcess = Process.GetProcessesByName("speedfan");
                try
                {
                    if (openProcess.Length == 0 && startIfNeeded && !startRequested)
                    {
                        using (Process.Start(new ProcessStartInfo(SpeedFanPath) {
                            WorkingDirectory = InstallDirectory, UseShellExecute = false
                        })) { }
                        startRequested = true;
                    }
                }
                finally { foreach (var process in openProcess) process.Dispose(); }
                issue = startIfNeeded ? "SpeedFan did not open its Readings window." :
                    "Choose a preset to start SpeedFan.";
            }
            if (!startIfNeeded) break;
            Thread.Sleep(250);
        } while (clock.Elapsed < TimeSpan.FromSeconds(35));

        throw new InvalidOperationException(issue);
    }

    private static IntPtr[] FindReadings(IntPtr main)
    {
        if (!IsWindowEnabled(main)) return null;
        var pageControl = Children(main).SingleOrDefault(w => ClassName(w) == "TPageControl");
        var readings = Children(pageControl).SingleOrDefault(w =>
            ClassName(w) == "TTabSheet" && TextOf(w) == "Readings");
        if (readings != IntPtr.Zero && !HasVisibleStyle(readings))
            throw new InvalidOperationException("Select the Readings tab in SpeedFan, then try again.");

        var editors = Children(readings).Where(w => ClassName(w) == "TRxSpinEdit" && HasVisibleStyle(w)).ToArray();
        return editors.Length == FanNames.Length ? OrderEditors(editors) : null;
    }

    private static void VerifyConfiguration()
    {
        if (!File.Exists(SpeedFanPath) || FileVersionInfo.GetVersionInfo(SpeedFanPath).FileVersion != "4.52.0.462")
            throw new InvalidOperationException("This tool expects the installed SpeedFan 4.52 build in Program Files (x86).");

        var sensorConfig = Path.Combine(InstallDirectory, "speedfansens.cfg");
        var pwmSections = Regex.Matches(ReadConfiguration(sensorConfig),
            @"(?m)^xxx Pwm (\d+) from [^\r\n]+\r?\n([\s\S]*?)^xxx end\s*$");
        var enabledNames = new List<string>();
        foreach (Match pwm in pwmSections)
        {
            var settings = pwm.Groups[2].Value;
            if (!Regex.IsMatch(settings, @"(?im)^active=true\r?$")) continue;
            if (pwm.Groups[1].Value != (enabledNames.Count + 1).ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("The SpeedFan PWM order changed. No preset was applied.");
            enabledNames.Add(Regex.Match(settings, @"(?im)^name=([^\r\n]+)").Groups[1].Value);
        }
        if (!enabledNames.SequenceEqual(FanNames, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The SpeedFan configuration does not match Sys, CPU, Aux, Aux1 and Aux2.");

        var parameters = Path.Combine(InstallDirectory, "speedfanparams.cfg");
        if (Regex.IsMatch(ReadConfiguration(parameters), @"(?im)^AutoFanSpeed=true\r?$"))
            throw new InvalidOperationException("Turn off Automatic fan speed on SpeedFan's Readings tab and try again.");
    }

    private static string ReadConfiguration(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException error)
            {
                int code = error.HResult & 0xffff;
                if (attempt == 24 || (code != 32 && code != 33)) throw;
                Thread.Sleep(125);
            }
        }
    }

    private static List<IntPtr> MainWindows()
    {
        var matches = new List<IntPtr>();
        EnumWindows((window, ignored) => {
            if (ClassName(window) != "TForm1") return true;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            IntPtr process = OpenProcess(0x1000, false, pid); // Query limited information.
            if (process == IntPtr.Zero) return true;
            try
            {
                var path = new StringBuilder(1024);
                int size = path.Capacity;
                if (QueryFullProcessImageName(process, 0, path, ref size) &&
                    String.Equals(path.ToString(), SpeedFanPath, StringComparison.OrdinalIgnoreCase))
                    matches.Add(window);
            }
            finally { CloseHandle(process); }
            return true;
        }, IntPtr.Zero);
        return matches;
    }

    private static IntPtr[] OrderEditors(IntPtr[] editors)
    {
        var positions = editors.Select(window => {
            Rect bounds;
            if (!GetWindowRect(window, out bounds)) throw new Win32Exception();
            return new { Window = window, Bounds = bounds };
        }).OrderBy(item => item.Bounds.Left).ToArray();

        // In the 4.52 Readings form, PWM 1–3 occupy the left column and PWM 4–6 the right.
        var left = positions.Take(3).OrderBy(item => item.Bounds.Top).ToArray();
        var right = positions.Skip(3).OrderBy(item => item.Bounds.Top).ToArray();
        if (left[2].Bounds.Left - left[0].Bounds.Left > 2 || right[1].Bounds.Left - right[0].Bounds.Left > 2 ||
            right[0].Bounds.Left <= left[0].Bounds.Right ||
            Math.Abs(left[0].Bounds.Top - right[0].Bounds.Top) > 2 ||
            Math.Abs(left[1].Bounds.Top - right[1].Bounds.Top) > 2 ||
            left[0].Bounds.Bottom > left[1].Bounds.Top || left[1].Bounds.Bottom > left[2].Bounds.Top)
            throw new InvalidOperationException("The Readings layout did not match the five configured fan controls.");

        return left.Concat(right).Select(item => item.Window).ToArray();
    }

    private static int[] ReadValues(IntPtr[] controls)
    {
        return controls.Select(control => {
            int value;
            if (!Int32.TryParse(TextOf(control), NumberStyles.None, CultureInfo.InvariantCulture, out value) ||
                value < 0 || value > 100)
                throw new InvalidOperationException("A SpeedFan field could not be read as a percentage.");
            return value;
        }).ToArray();
    }

    private static void SetValue(IntPtr control, int value)
    {
        IntPtr result;
        string percent = value.ToString(CultureInfo.InvariantCulture);
        if (WriteMessage(control, 0x000C, IntPtr.Zero, percent, 0x22, 1500, out result) == IntPtr.Zero || result == IntPtr.Zero)
            throw new InvalidOperationException("SpeedFan did not accept a fan setting.");
    }

    private static string TextOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        IntPtr result;
        if (ReadMessage(window, 0x000D, (IntPtr)text.Capacity, text, 0x22, 1000, out result) == IntPtr.Zero)
            throw new InvalidOperationException("SpeedFan did not respond while reading its controls.");
        return text.ToString();
    }

    private static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static IEnumerable<IntPtr> Children(IntPtr parent)
    {
        if (parent == IntPtr.Zero) yield break;
        for (var child = GetWindow(parent, 5); child != IntPtr.Zero; child = GetWindow(child, 2))
            yield return child;
    }

    private static bool HasVisibleStyle(IntPtr window)
    {
        return (GetWindowLong(window, -16) & 0x10000000) != 0;
    }

    private delegate bool WindowEnumerator(IntPtr window, IntPtr context);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowEnumerator callback, IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr ReadMessage(IntPtr window, uint message, IntPtr wParam, StringBuilder text,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr WriteMessage(IntPtr window, uint message, IntPtr wParam, string text,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendCommand(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
}
