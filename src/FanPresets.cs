using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class PresetProgram
{
    [STAThread]
    private static void Main(string[] arguments)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                OpenFromDesktop();
                return;
            }
        }

        if (arguments.Length == 1 && arguments[0] == "--setup")
        {
            try
            {
                StartupTask.InstallLauncher(Application.ExecutablePath);
                StartupTask.TryOpen();
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "SpeedFan Presets setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        bool startup = arguments.Length == 1 && arguments[0] == "--startup";
        string scope = @"Local\SpeedFanPresets-" + WindowsIdentity.GetCurrent().User.Value;
        using (var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, scope + "-show"))
        {
            bool first;
            using (var instance = new Mutex(true, scope, out first))
            {
                if (!first)
                {
                    if (!startup) showRequest.Set();
                    return;
                }

                using (var form = new PresetWindow(showRequest))
                using (var lifetime = new ApplicationContext())
                {
                    form.FormClosed += (sender, args) => lifetime.ExitThread();
                    if (startup)
                    {
                        var handle = form.Handle;
                        form.BeginInvoke((Action)(() => form.ApplyPreset(PresetWindow.NormalPercent, true)));
                    }
                    else form.OpenPanel();
                    Application.Run(lifetime);
                }
            }
        }
    }

    private static void OpenFromDesktop()
    {
        try
        {
            if (StartupTask.TryOpen()) return;
            using (Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--setup") {
                UseShellExecute = true, Verb = "runas"
            })) { }
        }
        catch (Win32Exception error)
        {
            if (error.NativeErrorCode != 1223)
                MessageBox.Show(error.Message, "SpeedFan Presets", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "SpeedFan Presets", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class PresetWindow : Form
{
    internal const int NormalPercent = 0;
    private const int PerformancePercent = 88;
    private const string PreferencesKey = @"HKEY_CURRENT_USER\Software\SpeedFanPresets";
    private const string PresetValue = "SelectedPercent";
    private static readonly Color ActiveColor = Color.FromArgb(16, 82, 92);
    private static readonly Color BorderColor = Color.FromArgb(152, 178, 185);

    private readonly Button normal;
    private readonly Button performance;
    private readonly Button none;
    private readonly Label status;
    private readonly CheckBox startup;
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem trayNormal;
    private readonly ToolStripMenuItem trayPerformance;
    private readonly ToolStripMenuItem trayNone;
    private readonly ToolStripMenuItem trayExit;
    private readonly System.Windows.Forms.Timer showTimer;
    private bool busy;
    private bool exiting;
    private bool changingStartup;
    private bool presetFailed;
    private int? selectedPreset;

    internal PresetWindow(EventWaitHandle showRequest)
    {
        Text = "SpeedFan Presets";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font = new Font("Segoe UI", 10);
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(404, 206);
        BackColor = Color.FromArgb(240, 245, 247);
        ForeColor = Color.FromArgb(23, 41, 51);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        normal = CreateButton(18);
        performance = CreateButton(144);
        none = CreateButton(270);
        normal.Click += (sender, args) => ApplyPreset(NormalPercent, false);
        performance.Click += (sender, args) => ApplyPreset(PerformancePercent, false);
        none.Click += (sender, args) => ClearPreset();
        status = new Label { Location = new Point(18, 104), Size = new Size(368, 57) };
        startup = new CheckBox {
            Location = new Point(18, 168), Size = new Size(368, 25),
            Text = "Start in Normal (0%) at Windows sign-in"
        };
        try { startup.Checked = StartupTask.Enabled(); }
        catch (Exception error) { status.Text = "Startup check failed. " + error.Message; }
        startup.CheckedChanged += UpdateStartup;
        Controls.AddRange(new Control[] { normal, performance, none, status, startup });

        trayNormal = new ToolStripMenuItem("Normal (0%)", null, (sender, args) => ApplyPreset(NormalPercent, true));
        trayPerformance = new ToolStripMenuItem("Performance (88%)", null, (sender, args) => ApplyPreset(PerformancePercent, true));
        trayNone = new ToolStripMenuItem("None", null, (sender, args) => ClearPreset());
        trayExit = new ToolStripMenuItem("Exit", null, (sender, args) => { exiting = true; Close(); });
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[] {
            new ToolStripMenuItem("Open SpeedFan Presets", null, (sender, args) => OpenPanel()),
            new ToolStripSeparator(), trayNormal, trayPerformance, trayNone, new ToolStripSeparator(), trayExit
        });
        tray = new NotifyIcon { Text = "SpeedFan Presets", Icon = Icon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (sender, args) => { if (args.Button == MouseButtons.Left) OpenPanel(); };
        showTimer = new System.Windows.Forms.Timer { Interval = 400 };
        showTimer.Tick += (sender, args) => { if (showRequest.WaitOne(0)) OpenPanel(); };
        showTimer.Start();
        var savedPreset = Registry.GetValue(PreferencesKey, PresetValue, -1) as int?;
        if (savedPreset == NormalPercent || savedPreset == PerformancePercent) selectedPreset = savedPreset;
        ShowSelection(null);
        Activated += (sender, args) => RefreshReadings();
    }

    private static Button CreateButton(int x)
    {
        return new Button {
            Location = new Point(x, 18), Size = new Size(116, 70),
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 10), UseVisualStyleBackColor = false
        };
    }

    private void ShowSelection(int? percent)
    {
        trayNormal.Checked = percent == NormalPercent;
        trayPerformance.Checked = percent == PerformancePercent;
        trayNone.Checked = !percent.HasValue;
        UpdateButton(normal, "Normal\n0%", trayNormal.Checked);
        UpdateButton(performance, "Performance\n88%", trayPerformance.Checked);
        UpdateButton(none, "None\nLeave as-is", trayNone.Checked);
    }

    private void UpdateButton(Button button, string label, bool active)
    {
        button.Text = active ? label + "  ✓" : label;
        button.AccessibleName = label.Replace('\n', ' ') + (active ? ", active" : "");
        button.BackColor = active ? ActiveColor : Color.White;
        button.ForeColor = active ? Color.White : ForeColor;
        button.FlatAppearance.BorderColor = active ? ActiveColor : BorderColor;
    }

    private string RememberPreset(int? percent)
    {
        selectedPreset = percent;
        ShowSelection(percent);
        try
        {
            Registry.SetValue(PreferencesKey, PresetValue, percent ?? -1, RegistryValueKind.DWord);
            return "";
        }
        catch (Exception)
        {
            return " This selection could only be saved for the current session.";
        }
    }

    private void ClearPreset()
    {
        if (busy) return;
        presetFailed = false;
        status.Text = "Fan speeds left as set." + RememberPreset(null);
    }

    internal void OpenPanel()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        RefreshReadings();
    }

    private async void RefreshReadings()
    {
        if (busy) return;
        SetBusy(true);
        try
        {
            var values = await Task.Run(() => SpeedFanControl.Read());
            if (selectedPreset.HasValue && values.Any(value => value != selectedPreset.Value))
                RememberPreset(null);
            else ShowSelection(selectedPreset);
            if (!presetFailed)
                status.Text = String.Join("   ", SpeedFanControl.FanNames.Select((name, index) => name + " " + values[index] + "%"));
        }
        catch (Exception error)
        {
            ShowSelection(null);
            if (!presetFailed) status.Text = error.Message;
        }
        finally { SetBusy(false); }
    }

    internal async void ApplyPreset(int percent, bool notify)
    {
        if (busy) return;
        SetBusy(true);
        presetFailed = false;
        string mode = percent == NormalPercent ? "Normal" : "Performance";
        status.Text = "Applying " + mode + "…";
        try
        {
            await Task.Run(() => SpeedFanControl.Apply(percent));
            status.Text = mode + " applied. All five controls are set to " + percent + "%." + RememberPreset(percent);
            if (notify && !Visible) tray.ShowBalloonTip(2500, "SpeedFan Presets", status.Text, ToolTipIcon.Info);
        }
        catch (Exception error)
        {
            presetFailed = true;
            status.Text = error.Message;
            ShowSelection(null);
            if (!Visible) tray.ShowBalloonTip(5000, "SpeedFan Presets", status.Text, ToolTipIcon.Error);
        }
        finally
        {
            SetBusy(false);
            if (presetFailed) RefreshReadings();
        }
    }

    private async void UpdateStartup(object sender, EventArgs args)
    {
        if (changingStartup) return;
        bool enable = startup.Checked;
        SetBusy(true);
        try
        {
            await Task.Run(() => StartupTask.SetEnabled(enable));
            status.Text = enable ? "Normal will apply after you sign in." : "Preset startup is off.";
        }
        catch (Exception error)
        {
            changingStartup = true;
            startup.Checked = !enable;
            changingStartup = false;
            status.Text = "Startup setup failed. " + error.Message;
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        normal.Enabled = performance.Enabled = none.Enabled = startup.Enabled = !value;
        trayNormal.Enabled = trayPerformance.Enabled = trayNone.Enabled = trayExit.Enabled = !value;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (args.CloseReason == CloseReason.UserClosing && !exiting)
        {
            args.Cancel = true;
            Hide();
        }
        base.OnFormClosing(args);
    }

    protected override void OnFormClosed(FormClosedEventArgs args)
    {
        showTimer.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip.Dispose();
        tray.Dispose();
        base.OnFormClosed(args);
    }
}

internal static class StartupTask
{
    private static readonly string Sid = WindowsIdentity.GetCurrent().User.Value;
    internal static readonly string TaskName = "SpeedFan Presets - " + Sid;
    private static readonly string OpenTaskName = "Open SpeedFan Presets - " + Sid;
    internal static readonly string InstalledExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SpeedFan Presets", "SpeedFan Presets.exe");

    private static dynamic Connect()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
        scheduler.Connect();
        return scheduler;
    }

    private static dynamic Find(dynamic folder, string name)
    {
        try { return folder.GetTask(name); }
        catch (COMException error)
        {
            if (error.HResult != unchecked((int)0x80070002)) throw;
            return null;
        }
    }

    internal static bool Enabled()
    {
        dynamic task = Find(Connect().GetFolder("\\"), TaskName);
        return task != null && (bool)task.Enabled;
    }

    internal static bool TryOpen()
    {
        dynamic task = Find(Connect().GetFolder("\\"), OpenTaskName);
        if (task == null) return false;
        task.Run(null);
        return true;
    }

    internal static void InstallLauncher(string source)
    {
        string directory = Path.GetDirectoryName(InstalledExe);
        Directory.CreateDirectory(directory);
        if (!String.Equals(source, InstalledExe, StringComparison.OrdinalIgnoreCase) &&
            (!File.Exists(InstalledExe) || !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(InstalledExe))))
            File.Copy(source, InstalledExe, true);

        dynamic scheduler = Connect();
        dynamic folder = scheduler.GetFolder("\\");
        dynamic definition = DefineTask(scheduler, "Open the SpeedFan Presets panel on demand.", "--open");
        // A second invocation can open the window of the process already in the tray.
        definition.Settings.MultipleInstances = 0; // Run in parallel.
        string permissions = "O:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;" + Sid + ")";
        // Only administrators can edit the action. This user can read it and request a run.
        folder.RegisterTaskDefinition(OpenTaskName, definition, 0x16, Sid, null, 3, permissions);

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string link = Path.Combine(desktop, "SpeedFan Presets.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = InstalledExe;
        shortcut.WorkingDirectory = directory;
        shortcut.Description = "Open SpeedFan Presets";
        shortcut.Save();
    }

    internal static void SetEnabled(bool enable)
    {
        dynamic scheduler = Connect();
        dynamic folder = scheduler.GetFolder("\\");
        if (!enable)
        {
            dynamic task = Find(folder, TaskName);
            if (task != null) task.Enabled = false;
            return;
        }

        dynamic definition = DefineTask(scheduler, "Apply Normal and open the tray controls at sign-in.", "--startup");
        definition.Settings.MultipleInstances = 2; // Ignore new instances.
        dynamic trigger = definition.Triggers.Create(9); // Logon trigger.
        trigger.UserId = Sid;
        trigger.Delay = "PT5S";
        folder.RegisterTaskDefinition(TaskName, definition, 6, Sid, null, 3, null); // Create or update.
    }

    private static dynamic DefineTask(dynamic scheduler, string description, string arguments)
    {
        dynamic definition = scheduler.NewTask(0);
        definition.RegistrationInfo.Description = description;
        definition.Principal.UserId = Sid;
        definition.Principal.LogonType = 3; // Interactive token.
        definition.Principal.RunLevel = 1; // Highest available, like SpeedFan.
        definition.Settings.ExecutionTimeLimit = "PT0S";
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        dynamic action = definition.Actions.Create(0); // Executable action.
        action.Path = InstalledExe;
        action.WorkingDirectory = Path.GetDirectoryName(InstalledExe);
        action.Arguments = arguments;
        return definition;
    }
}
