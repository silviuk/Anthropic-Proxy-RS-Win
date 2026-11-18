// Native Windows Forms controller. Built with the Windows .NET Framework compiler.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("Anthropic-Proxy-RS-Win")]
[assembly: System.Reflection.AssemblyVersion("1.5.3.0")]

namespace AnthropicProxyDesktop
{
    sealed class Settings
    {
        public Settings() {}
        public string Upstream = "https://api.openai.com";
        public string Secret = "";
        public string Bind = "127.0.0.1";
        public int Port = 3000;
        public string Completion = "";
        public string Reasoning = "";
        public string ModelMap = "";
        public string IgnoreTerms = "";
        public bool Passthrough;
        public bool CaptureBodies;
        public bool Debug;
        public bool Background = true;
        public bool NoEnvFile = true;
        public string ConfigFile = "";
        public string PidFile = "";
        public string LogFile = "";

        public string GetKey()
        {
            if (String.IsNullOrEmpty(Secret)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(Secret), null, DataProtectionScope.CurrentUser));
        }
        public void SetKey(string key)
        {
            Secret = String.IsNullOrEmpty(key) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        }
        public void Validate()
        {
            ConfigFile = ConfigFile ?? ""; PidFile = PidFile ?? ""; LogFile = LogFile ?? "";
            if (ConfigFile.Length > 0 && !File.Exists(ConfigFile)) throw new Exception("The selected configuration file does not exist.");
            foreach (string path in new[] { PidFile, LogFile })
                if (path.Length > 0 && !Path.IsPathRooted(path)) throw new Exception("Use a full path for custom PID and log files.");
            ModelMap = ModelMap ?? ""; IgnoreTerms = IgnoreTerms ?? "";
            Completion = Completion ?? ""; Reasoning = Reasoning ?? "";
            if (String.IsNullOrWhiteSpace(Upstream)) throw new Exception("Enter an upstream URL.");
            foreach (string item in Upstream.Split(';'))
            {
                Uri uri;
                if (!Uri.TryCreate(item.Trim(), UriKind.Absolute, out uri) ||
                    (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.Query) ||
                    !String.IsNullOrEmpty(uri.Fragment) || !String.IsNullOrEmpty(uri.UserInfo))
                    throw new Exception("Enter an HTTP or HTTPS upstream URL without credentials, query parameters or fragments.");
                string last = uri.AbsolutePath.TrimEnd('/').Split('/').Last();
                if (last == "chat" || (last == "completions" && !uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions")))
                    throw new Exception("Use a service URL, versioned base URL or full /chat/completions endpoint.");
            }
            System.Net.IPAddress address;
            string host = (Bind ?? "").Trim().Trim('[', ']');
            if (System.Net.IPAddress.TryParse(host, out address))
                Bind = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString();
            else if (Uri.CheckHostName(host) == UriHostNameType.Dns) Bind = host;
            else throw new Exception("Enter a listening address, such as localhost, 127.0.0.1 or [::1].");
            if (Port < 1 || Port > 65535) throw new Exception("The listening port must be between 1 and 65535.");
            if (Passthrough && GetKey().Length != 0) throw new Exception("Clear the saved API key before enabling client API key passthrough.");
            foreach (string entry in ModelMap.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('=');
                if (separator < 1 || String.IsNullOrWhiteSpace(entry.Substring(0, separator)) || String.IsNullOrWhiteSpace(entry.Substring(separator + 1)))
                    throw new Exception("Model mapping must use source=target entries separated by semicolons.");
            }
        }
    }

    sealed class Controller
    {
        internal readonly string DirectoryPath, SettingsPath, Binary;
        string RuntimePath { get { return Path.Combine(DirectoryPath, "runtime-paths.json"); } }
        internal string PidPath { get { return RuntimePaths()[0]; } }
        internal string LogPath { get { return RuntimePaths()[1]; } }
        Process foreground;
        internal bool HasForeground { get { return foreground != null && !foreground.HasExited; } }
        string[] RuntimePaths()
        {
            return File.Exists(RuntimePath) ? new JavaScriptSerializer().Deserialize<string[]>(File.ReadAllText(RuntimePath)) :
                new[] { Path.Combine(DirectoryPath, "proxy.pid"), Path.Combine(DirectoryPath, "requests.log") };
        }
        internal readonly string RunKey;
        internal readonly ProfileStore Profiles;
        internal const string RunValue = "AnthropicProxy";
        internal Controller(string directory, string binary, string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run")
        {
            DirectoryPath = directory;
            Directory.CreateDirectory(directory);
            SettingsPath = Path.Combine(directory, "settings.json");
            Binary = Path.GetFullPath(binary);
            RunKey = runKey;
            Profiles = new ProfileStore(directory, Load);
        }
        internal Settings Load()
        {
            return File.Exists(SettingsPath) ? new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(SettingsPath)) : new Settings();
        }
        internal void Save(Settings settings)
        {
            settings.Validate();
            string temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
        }
        internal bool Autostart
        {
            get
            {
                if (PackagedStartup.IsPackaged) return PackagedStartup.Enabled;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && String.Equals(key.GetValue(RunValue) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
            }
        }
        internal string StartupCommand { get { return Quote(Application.ExecutablePath) + " --autostart"; } }
        internal void SetAutostart(bool enabled)
        {
            if (PackagedStartup.IsPackaged) { PackagedStartup.SetEnabled(enabled); return; }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(RunValue, StartupCommand, RegistryValueKind.String);
                else key.DeleteValue(RunValue, false);
            }
        }
        internal static string Quote(string value)
        {
            // Windows command line quoting, including trailing backslashes.
            StringBuilder result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
                else { result.Append('\\', slashes); result.Append(c); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }
        internal bool IsRunning()
        {
            int pid;
            try {
                using (var file = new FileStream(PidPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(file))
                    if (!Int32.TryParse(reader.ReadToEnd().Trim(), out pid) || pid <= 0) return false;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    return !process.HasExited && String.Equals(process.MainModule.FileName, Binary, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            // Permission failures are shown to the user rather than treated as a stopped process.
        }
        internal void AutoStart()
        {
            if (Autostart) {
                Settings settings = Load(); settings.Background = true; Start(settings);
            }
        }
        internal void Start(Settings settings)
        {
            settings.Validate();
            if (IsRunning()) return;
            string pid = String.IsNullOrWhiteSpace(settings.PidFile) ? Path.Combine(DirectoryPath, "proxy.pid") : settings.PidFile;
            string log = String.IsNullOrWhiteSpace(settings.LogFile) ? Path.Combine(DirectoryPath, "requests.log") : settings.LogFile;
            File.WriteAllText(RuntimePath, new JavaScriptSerializer().Serialize(new[] { pid, log }));
            Execute(StartArguments(settings, pid, log), settings);
            if (!IsRunning()) throw new Exception("The proxy did not start. Open Logs for details.");
        }
        internal void Stop()
        {
            if (!IsRunning()) return;
            try { Execute("stop --pid-file " + Quote(PidPath), null); }
            catch (ProxyCommandException) {
                // Another stop or process exit may finish between the initial
                // status check and the command. The desired state is reached.
                if (IsRunning()) throw;
            }
        }
        internal static string StartArguments(Settings settings, string pid, string log)
        {
            string args = (settings.Background ? "--daemon" : "--daemon-child") + " --log-file " + Quote(log) + " --pid-file " + Quote(pid);
            if (settings.NoEnvFile) args += " --no-env-file";
            if (!String.IsNullOrWhiteSpace(settings.ConfigFile) && !settings.NoEnvFile) args += " --config " + Quote(settings.ConfigFile);
            if (settings.Debug) args += " --debug";
            if (settings.CaptureBodies) args += " --verbose";
            return args;
        }
        internal string Execute(string arguments, Settings settings)
        {
            if (!File.Exists(Binary)) throw new Exception("Keep anthropic-proxy.exe in the same folder as this app.");
            ProcessStartInfo info = new ProcessStartInfo(Binary, arguments);
            info.WorkingDirectory = DirectoryPath;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            // Management commands never spawn a detached child, so their actual
            // stderr can be captured safely. Keep daemon startup free of pipes.
            info.RedirectStandardError = settings == null;
            string previousLog = settings == null ? "" : ReadLog();
            if (settings != null)
            {
                string[] variables = { "UPSTREAM_BASE_URL", "ANTHROPIC_PROXY_BASE_URL", "UPSTREAM_API_KEY", "OPENROUTER_API_KEY",
                    "UPSTREAM_API_KEY_PASSTHROUGH", "ANTHROPIC_PROXY_BIND", "PORT", "COMPLETION_MODEL", "REASONING_MODEL",
                    "ANTHROPIC_PROXY_MODEL_MAP", "ANTHROPIC_PROXY_SYSTEM_PROMPT_IGNORE_TERMS", "DEBUG", "VERBOSE", "RUST_LOG" };
                foreach (string name in variables) info.EnvironmentVariables.Remove(name);
                info.EnvironmentVariables["UPSTREAM_BASE_URL"] = settings.Upstream.Trim();
                info.EnvironmentVariables["UPSTREAM_API_KEY"] = settings.GetKey();
                info.EnvironmentVariables["UPSTREAM_API_KEY_PASSTHROUGH"] = settings.Passthrough ? "true" : "false";
                info.EnvironmentVariables["ANTHROPIC_PROXY_BIND"] = settings.Bind.Trim();
                info.EnvironmentVariables["PORT"] = settings.Port.ToString();
                if (settings.Completion.Trim().Length > 0) info.EnvironmentVariables["COMPLETION_MODEL"] = settings.Completion.Trim();
                if (settings.Reasoning.Trim().Length > 0) info.EnvironmentVariables["REASONING_MODEL"] = settings.Reasoning.Trim();
                info.EnvironmentVariables["ANTHROPIC_PROXY_MODEL_MAP"] = settings.ModelMap.Trim();
                info.EnvironmentVariables["ANTHROPIC_PROXY_SYSTEM_PROMPT_IGNORE_TERMS"] = settings.IgnoreTerms.Trim();
                info.EnvironmentVariables["DEBUG"] = "false";
                info.EnvironmentVariables["VERBOSE"] = "false";
            }
            if (settings != null && !settings.Background)
            {
                foreground = Process.Start(info);
                for (int i = 0; i < 200; i++)
                {
                    if (foreground.HasExited) throw new Exception("The foreground proxy exited. Open Logs for details.");
                    if (IsRunning()) return "";
                    Thread.Sleep(50);
                }
                foreground.Kill(); foreground.WaitForExit();
                throw new Exception("The foreground proxy did not become ready. Open Logs for details.");
            }
            using (Process process = Process.Start(info))
            {
                Task<string> diagnostic = settings == null ? process.StandardError.ReadToEndAsync() : null;
                if (!process.WaitForExit(20000)) { process.Kill(); throw new Exception("The proxy command timed out. Check Logs and the current status."); }
                if (process.ExitCode != 0)
                {
                    string details = diagnostic != null && diagnostic.Wait(1000) ? diagnostic.Result : "";
                    if (settings != null) {
                        string currentLog = ReadLog();
                        // Historical request failures are not startup diagnostics.
                        details = currentLog.StartsWith(previousLog, StringComparison.Ordinal) ? currentLog.Substring(previousLog.Length) :
                            currentLog == previousLog ? "" : currentLog;
                    }
                    if (String.IsNullOrWhiteSpace(details)) details = "No new diagnostic output was recorded. Open Logs to inspect proxy activity.";
                    string action = settings != null ? "start" : arguments.StartsWith("stop ", StringComparison.Ordinal) ? "stop" : "run the command for";
                    throw new ProxyCommandException("Could not " + action + " the proxy (exit code " + process.ExitCode + ").", details);
                }
                return "";
            }
        }
        internal string Help()
        {
            var info = new ProcessStartInfo(Binary, "--help") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var process = Process.Start(info)) {
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Help command timed out."); }
                return output.GetAwaiter().GetResult();
            }
        }
        internal string ReadLog()
        {
            if (!File.Exists(LogPath)) return "No logs yet. Start the proxy to see its activity here.";
            using (FileStream stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bool tail = stream.Length > 262144;
                if (tail) stream.Seek(-262144, SeekOrigin.End);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    if (tail) reader.ReadLine();
                    return (tail ? "Showing the most recent 256 KB.\r\n" : "") + reader.ReadToEnd();
                }
            }
        }
        internal void ClearLogs()
        {
            if (File.Exists(LogPath))
                using (FileStream file = new FileStream(LogPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) file.SetLength(0);
            string backup = Path.ChangeExtension(LogPath, ".log.1");
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    // Native buttons and panels avoid the legacy TabControl's fixed light strip.
    sealed class PageSwitcher : TableLayoutPanel
    {
        readonly FlowLayoutPanel navigation = new FlowLayoutPanel();
        readonly Panel container = new Panel();
        readonly List<Control> pages = new List<Control>();
        readonly List<Button> buttons = new List<Button>();
        int selected = -1;
        internal event EventHandler SelectedIndexChanged;
        internal PageSwitcher()
        {
            ColumnCount = 1; RowCount = 2; Dock = DockStyle.Fill;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.AutoSize)); RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            navigation.Dock = DockStyle.Fill; navigation.AutoSize = true; navigation.WrapContents = false;
            navigation.Margin = new Padding(0, 0, 0, 8);
            container.Dock = DockStyle.Fill; container.Margin = new Padding(0);
            Controls.Add(navigation, 0, 0); Controls.Add(container, 0, 1);
        }
        internal void AddPage(string title, Control page)
        {
            int index = pages.Count;
            var button = new Button { Text = title, Width = 110, Height = 34, Margin = new Padding(0, 0, 6, 0), AccessibleName = title + " page" };
            button.Click += delegate { SelectedIndex = index; };
            page.Dock = DockStyle.Fill; page.Visible = false;
            pages.Add(page); buttons.Add(button); navigation.Controls.Add(button); container.Controls.Add(page);
            if (selected < 0) SelectedIndex = 0;
        }
        internal int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                for (int i = 0; i < pages.Count; i++)
                {
                    pages[i].Visible = i == selected;
                    buttons[i].FlatAppearance.BorderSize = i == selected ? 2 : 1;
                }
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
    }

    sealed class MainWindow : Form
    {
        readonly Controller controller;
        readonly TextBox upstream = new TextBox(), apiKey = new TextBox(), bind = new TextBox(),
            completion = new TextBox(), reasoning = new TextBox(), mapping = new TextBox(), ignore = new TextBox();
        readonly NumericUpDown port = new NumericUpDown();
        readonly CheckBox passthrough = new CheckBox(), capture = new CheckBox(), autostart = new CheckBox(), showKey = new CheckBox();
        readonly Button apply = new Button(), start = new Button(), stop = new Button();
        readonly ComboBox profileNames = new ComboBox();
        readonly Button saveProfile = new Button(), loadProfile = new Button(), renameProfile = new Button(), importProfile = new Button(), exportProfile = new Button();
        readonly CheckBox debug = new CheckBox(), background = new CheckBox(), noEnvFile = new CheckBox();
        readonly TextBox configFile = new TextBox(), pidFile = new TextBox(), logFile = new TextBox();
        string warningMessage = "";
        internal enum State { Running, Stopped, Warning }
        State indicator = State.Stopped;
        string loadedFingerprint = "";
        readonly Label status = new Label(), feedback = new Label();
        readonly RichTextBox logs = new RichTextBox();
        readonly PageSwitcher tabs = new PageSwitcher();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool busy, polling, exitRequested;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
        readonly ToolStripMenuItem trayState = new ToolStripMenuItem("Stopped");
        readonly ToolStripMenuItem trayStart = new ToolStripMenuItem("Start proxy with these settings");
        readonly ToolStripMenuItem trayStop = new ToolStripMenuItem("Stop proxy");
        internal static readonly int ShowMessage = RegisterWindowMessage("Anthropic-Proxy-RS-Win.ShowSettings");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
        internal bool TrayVisible { get { return tray.Visible && tray.Icon != null; } }
        bool? lastDark;
        bool lastHighContrast;
        string lastLog = "";
        internal bool? ThemeOverride;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string id);

        internal MainWindow(Controller control, bool automation = false, bool startHidden = false)
        {
            controller = control;
            Text = "Anthropic-Proxy-RS-Win";
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Icon = Icon; tray.Text = "Anthropic-Proxy-RS-Win — Stopped"; tray.ContextMenuStrip = trayMenu;
            var openSettings = new ToolStripMenuItem("Show settings"); openSettings.Click += delegate { ShowSettingsWindow(); };
            var viewLogs = new ToolStripMenuItem("View logs"); viewLogs.Click += delegate { tabs.SelectedIndex = 1; RefreshLogs(); ShowSettingsWindow(); };
            var exit = new ToolStripMenuItem("Exit and stop proxy");
            trayStart.Click += async delegate { await ApplyAsync(true); };
            trayStop.Click += async delegate { await StopAsync(); };
            exit.Click += async delegate { await StopAsync(); if (!controller.IsRunning()) { exitRequested = true; Close(); } };
            trayMenu.Items.AddRange(new ToolStripItem[] { trayState, new ToolStripSeparator(), openSettings, viewLogs, trayStart, trayStop, new ToolStripSeparator(), exit });
            tray.DoubleClick += delegate { ShowSettingsWindow(); }; tray.Visible = true;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(800, Math.Min(810, Math.Max(620, area.Height - 80)));
            MinimumSize = new Size(700, 660);
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = new Label { Text = "Anthropic-Proxy-RS-Win", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
            root.Controls.Add(title, 0, 0);
            status.AutoSize = true; status.MaximumSize = new Size(750, 0); status.Margin = new Padding(0, 0, 0, 16); status.Text = "Checking proxy status...";
            root.Controls.Add(status, 0, 1);
            var profileBar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 7, RowCount = 2, Margin = new Padding(0, 0, 0, 12) };
            profileBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) profileBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            profileBar.Controls.Add(new Label { Text = "Saved settings", AutoSize = true, Margin = new Padding(0, 7, 10, 0) }, 0, 0);
            profileNames.Dock = DockStyle.Fill; profileNames.DropDownStyle = ComboBoxStyle.DropDown;
            profileNames.AccessibleName = "Saved settings profile"; profileNames.MaxLength = 80;
            profileNames.AutoCompleteMode = AutoCompleteMode.SuggestAppend; profileNames.AutoCompleteSource = AutoCompleteSource.ListItems;
            profileBar.Controls.Add(profileNames, 1, 0);
            saveProfile.Text = "Save"; loadProfile.Text = "Load"; renameProfile.Text = "Rename"; importProfile.Text = "Import..."; exportProfile.Text = "Export...";
            int column = 2;
            foreach (Button button in new[] { saveProfile, loadProfile, renameProfile, importProfile, exportProfile })
            { button.AutoSize = true; button.MinimumSize = new Size(65, 28); profileBar.Controls.Add(button, column++, 0); }
            saveProfile.Click += delegate { TryAction(SaveProfile); };
            loadProfile.Click += delegate { TryAction(delegate { LoadProfile(true); }); };
            renameProfile.Click += delegate { TryAction(delegate {
                string name = PromptProfileName(profileNames.Text);
                if (name != null) RenameProfile(name);
            }); };
            importProfile.Click += delegate { TryAction(delegate {
                using (var dialog = new OpenFileDialog { Title = "Import proxy settings", Filter = "Proxy settings (*.env;*.json)|*.env;*.json|All files (*.*)|*.*", CheckFileExists = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK && ConfirmLoad()) ImportProfile(dialog.FileName);
            }); };
            exportProfile.Click += delegate { TryAction(delegate {
                using (var options = new ExportOptions()) {
                    ApplyDialogTheme(options);
                    if (options.ShowDialog(this) != DialogResult.OK) return;
                    using (var file = new SaveFileDialog { Title = "Export settings", Filter = "JSON settings (*.json)|*.json", DefaultExt = "json", FileName = "proxy-settings.json", OverwritePrompt = true })
                        if (file.ShowDialog(this) == DialogResult.OK) {
                            ProfileStore.Export(file.FileName, ReadSettings(), options.IncludeKey.Checked);
                            feedback.Text = options.IncludeKey.Checked ? "Exported with API key: don't share with others" : "Exported settings without the API key.";
                            if (options.IncludeKey.Checked) warningMessage = "Export contains an API key: don't share with others";
                        }
                }
            }); };
            var profileHint = new Label { Text = "Type a name to save a new profile. Load fills the form; apply it when ready.", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
            profileBar.Controls.Add(profileHint, 0, 1); profileBar.SetColumnSpan(profileHint, 7);
            root.Controls.Add(profileBar, 0, 2);
            tabs.Dock = DockStyle.Fill;
            var settingsTab = new Panel { Padding = new Padding(10), AutoScroll = true, AccessibleName = "Settings" };
            var logTab = new Panel { Padding = new Padding(10), AccessibleName = "Logs" };
            tabs.AddPage("Settings", settingsTab); tabs.AddPage("Logs", logTab);
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(fields, "Upstream URLs", MultipleEntries(upstream, "Upstream URLs in failover order"));
            AddField(fields, "API key", apiKey); apiKey.UseSystemPasswordChar = true;
            showKey.Text = "Show API key"; showKey.AutoSize = true;
            showKey.CheckedChanged += delegate { apiKey.UseSystemPasswordChar = !showKey.Checked; };
            AddField(fields, "", showKey);
            passthrough.Text = "Use each client's API key"; passthrough.AutoSize = true;
            AddField(fields, "Authentication", passthrough);
            var listener = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = new Padding(0) };
            listener.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); listener.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); listener.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            bind.Dock = DockStyle.Fill; bind.AccessibleName = "Listen address";
            port.Minimum = 1; port.Maximum = 65535; port.Dock = DockStyle.Fill; port.AccessibleName = "Port";
            listener.Controls.Add(bind, 0, 0); listener.Controls.Add(new Label { Text = "Port", AutoSize = true, Margin = new Padding(10, 6, 6, 0) }, 1, 0); listener.Controls.Add(port, 2, 0);
            AddField(fields, "Listen address", listener);
            AddField(fields, "Completion model", completion);
            AddField(fields, "Reasoning model", reasoning);
            AddField(fields, "Model mapping", MultipleEntries(mapping, "Model mappings: source=target"));
            mapping.AccessibleDescription = "Optional source=target entries separated by semicolons.";
            AddField(fields, "Ignore prompt terms", MultipleEntries(ignore, "System prompt terms to remove"));
            var hint = new Label { Text = "Models are optional. Mapping: source=target;other=target.\nUse semicolons for multiple upstream URLs or ignored terms.", AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(3, 2, 3, 10) };
            AddField(fields, "", hint);
            autostart.Text = "Start proxy when I sign in to Windows"; autostart.AutoSize = true;
            AddField(fields, "Autostart", autostart);
            debug.Text = "Debug (-d, --debug)"; debug.AutoSize = true;
            AddField(fields, "Logging", debug);
            capture.Text = "Verbose (-v, --verbose): request/response bodies"; capture.AutoSize = true;
            AddField(fields, "", capture);
            background.Text = "Keep running after closing this window (--daemon)"; background.AutoSize = true;
            AddField(fields, "Background mode", background);
            noEnvFile.Text = "Skip .env discovery (--no-env-file)"; noEnvFile.AutoSize = true;
            AddField(fields, "Configuration", noEnvFile);
            AddField(fields, "Config file (--config)", FileSetting(configFile, true));
            AddField(fields, "PID file (--pid-file)", FileSetting(pidFile, false));
            AddField(fields, "Log file (--log-file)", FileSetting(logFile, false));
            var help = new Button { Text = "Show command-line help (--help)", AutoSize = true };
            help.Click += delegate { TryAction(delegate { using (var dialog = new Form { Text = "Command-line help", Size = new Size(730, 550), StartPosition = FormStartPosition.CenterParent }) {
                dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 10), Text = controller.Help() });
                ApplyDialogTheme(dialog); dialog.ShowDialog(this);
            } }); };
            AddField(fields, "Help and version", help);
            AddField(fields, "", new Label { Text = "Includes prompts, responses and streamed content. Saved locally.\nOff by default. Save and apply restarts a running proxy.", AutoSize = true, MaximumSize = new Size(450, 0) });
            settingsTab.Controls.Add(fields);

            var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            logLayout.Controls.Add(new Label { Text = "Live activity • Body logging is controlled in Settings.", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
            logs.Dock = DockStyle.Fill; logs.ReadOnly = true; logs.Font = new Font("Consolas", 9); logs.WordWrap = false; logs.DetectUrls = false; logs.AccessibleName = "Request and response logs";
            logLayout.Controls.Add(logs, 0, 1);
            var logButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var clear = new Button { Text = "Clear logs", AutoSize = true };
            clear.Click += delegate { TryAction(delegate { controller.ClearLogs(); warningMessage = ""; RefreshLogs(); }); };
            var open = new Button { Text = "Open log folder", AutoSize = true };
            open.Click += delegate { TryAction(delegate { Process.Start(new ProcessStartInfo("explorer.exe", Controller.Quote(controller.DirectoryPath)) { UseShellExecute = true }); }); };
            logButtons.Controls.Add(clear); logButtons.Controls.Add(open);
            logLayout.Controls.Add(logButtons, 0, 2); logTab.Controls.Add(logLayout);
            root.Controls.Add(tabs, 0, 3);
            feedback.AutoSize = true; feedback.MaximumSize = new Size(700, 0); feedback.Margin = new Padding(0, 10, 0, 6);
            feedback.Text = "API keys are encrypted for your Windows account. Apply settings to change the running proxy.";
            root.Controls.Add(feedback, 0, 4);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            apply.Text = "Save and apply"; start.Text = "Save and start"; stop.Text = "Stop proxy";
            foreach (Button button in new[] { apply, start, stop }) { button.AutoSize = true; button.Padding = new Padding(8, 5, 8, 5); actions.Controls.Add(button); }
            apply.Click += async delegate { await ApplyAsync(false); };
            start.Click += async delegate { await ApplyAsync(true); };
            stop.Click += async delegate { await StopAsync(); };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            footer.Controls.Add(actions, 0, 0);
            var about = new TableLayoutPanel { Size = new Size(300, 60), Anchor = AnchorStyles.Right | AnchorStyles.Bottom, ColumnCount = 1, RowCount = 3 };
            about.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) about.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            about.Controls.Add(new Label { Text = "v" + typeof(Program).Assembly.GetName().Version.ToString(3) + " • Silviu Vlasceanu", AutoSize = true, Font = new Font("Segoe UI", 8.5f) });
            var github = new LinkLabel { Text = "GitHub: Anthropic-Proxy-RS-Win", AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            github.LinkClicked += delegate { OpenLink("https://github.com/silviuk/Anthropic-Proxy-RS-Win"); };
            var fork = new LinkLabel { Text = "Original proxy: Ivan Zakutnii (m0n0x41d)", AutoSize = true, Font = new Font("Segoe UI", 8.5f) };
            fork.LinkClicked += delegate { OpenLink("https://github.com/m0n0x41d/anthropic-proxy-rs"); };
            about.Controls.Add(github); about.Controls.Add(fork);
            foreach (Label label in about.Controls) { label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleRight; }
            footer.Controls.Add(about, 1, 0);
            root.Controls.Add(footer, 0, 5); Controls.Add(root);
            AcceptButton = apply;
            LoadSettings();
            timer.Interval = 1000;
            timer.Tick += async delegate { ApplyTheme(); if (tabs.SelectedIndex == 1) RefreshLogs(); await RefreshStatusAsync(); };
            tabs.SelectedIndexChanged += delegate { RefreshLogs(); };
            Shown += async delegate { ApplyTheme(); timer.Start(); if (startHidden) { Hide(); ShowInTaskbar = false; } await RefreshStatusAsync(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); ShowInTaskbar = false; } };
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (!automation && !exitRequested && args.CloseReason == CloseReason.UserClosing && !controller.HasForeground) {
                    args.Cancel = true; Hide(); ShowInTaskbar = false;
                }
            };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); if (controller.HasForeground) controller.Stop(); };
        }
        void ShowSettingsWindow() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
        static void OpenLink(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        internal void SetIndicator(State state, string text)
        {
            indicator = state; status.Text = text.Length > 180 ? text.Substring(0, 177) + "..." : text;
            tray.Text = "Anthropic-Proxy-RS-Win — " + state.ToString();
            trayState.Text = state.ToString();
            status.ForeColor = state == State.Warning ? (lastDark == true ? Color.Orange : Color.DarkOrange) :
                state == State.Running ? (lastDark == true ? Color.LimeGreen : Color.ForestGreen) : (lastDark == true ? Color.Salmon : Color.Firebrick);
            trayState.ForeColor = status.ForeColor;
        }
        internal Color StatusColor { get { return status.ForeColor; } }
        internal void ApplyDialogTheme(Form dialog)
        {
            ThemeControl(dialog, BackColor, ForeColor, apiKey.BackColor, lastDark == true, SystemInformation.HighContrast);
            dialog.Shown += delegate { int dark = lastDark == true ? 1 : 0; DwmSetWindowAttribute(dialog.Handle, 20, ref dark, sizeof(int)); };
        }
        Control MultipleEntries(TextBox text, string title)
        {
            var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            text.Dock = DockStyle.Fill;
            var edit = new Button { Text = "Entries...", AutoSize = true };
            edit.Click += delegate { using (var dialog = new EntryEditor(title, text.Text)) { ApplyDialogTheme(dialog); if (dialog.ShowDialog(this) == DialogResult.OK) text.Text = dialog.Value; } };
            row.Controls.Add(text, 0, 0); row.Controls.Add(edit, 1, 0); return row;
        }
        Control FileSetting(TextBox text, bool config)
        {
            var row = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2 };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); text.Dock = DockStyle.Fill;
            var browse = new Button { Text = config ? "Load file..." : "Browse...", AutoSize = true };
            browse.Click += delegate { TryAction(delegate {
                if (config) {
                    using (var file = new OpenFileDialog { Filter = "Environment files (*.env)|*.env|All files (*.*)|*.*", CheckFileExists = true })
                        if (file.ShowDialog(this) == DialogResult.OK && ConfirmLoad()) {
                            ImportProfile(file.FileName); configFile.Text = file.FileName; noEnvFile.Checked = false;
                        }
                } else {
                    using (var file = new SaveFileDialog { FileName = text == pidFile ? "proxy.pid" : "requests.log", OverwritePrompt = false })
                        if (file.ShowDialog(this) == DialogResult.OK) text.Text = file.FileName;
                }
            }); };
            row.Controls.Add(text, 0, 0); row.Controls.Add(browse, 1, 0); return row;
        }
        static void AddField(TableLayoutPanel table, string label, Control field)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 8, 7) };
            table.Controls.Add(caption, 0, row);
            field.Dock = DockStyle.Fill; field.Margin = new Padding(3, 4, 3, 4);
            if (String.IsNullOrEmpty(field.AccessibleName)) field.AccessibleName = label;
            table.Controls.Add(field, 1, row);
        }
        void LoadSettings()
        {
            var library = controller.Profiles.Read();
            FillSettings(library.Profiles.First(p => String.Equals(p.Name, library.Selected, StringComparison.OrdinalIgnoreCase)).Settings);
            RefreshProfiles(library.Selected);
            autostart.Checked = controller.Autostart;
        }
        void FillSettings(Settings settings)
        {
            settings.Validate();
            string key = settings.GetKey();
            upstream.Text = settings.Upstream; apiKey.Text = key; bind.Text = settings.Bind;
            port.Value = settings.Port; completion.Text = settings.Completion;
            reasoning.Text = settings.Reasoning; mapping.Text = settings.ModelMap; ignore.Text = settings.IgnoreTerms;
            passthrough.Checked = settings.Passthrough; capture.Checked = settings.CaptureBodies; debug.Checked = settings.Debug;
            background.Checked = settings.Background; noEnvFile.Checked = settings.NoEnvFile;
            configFile.Text = settings.ConfigFile; pidFile.Text = settings.PidFile; logFile.Text = settings.LogFile;
            loadedFingerprint = Fingerprint();
        }
        void RefreshProfiles(string selected)
        {
            profileNames.Items.Clear();
            foreach (string name in controller.Profiles.Read().Profiles.Select(p => p.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)) profileNames.Items.Add(name);
            profileNames.Text = selected;
        }
        string Fingerprint()
        {
            string data = new JavaScriptSerializer().Serialize(new[] { upstream.Text, apiKey.Text, bind.Text, port.Value.ToString(),
                completion.Text, reasoning.Text, mapping.Text, ignore.Text, passthrough.Checked.ToString(), capture.Checked.ToString(), debug.Checked.ToString(), background.Checked.ToString(), noEnvFile.Checked.ToString(), configFile.Text, pidFile.Text, logFile.Text });
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }
        bool ConfirmLoad()
        {
            return Fingerprint() == loadedFingerprint || MessageBox.Show(this, "Replace the form's unsaved changes? Save them as a profile first if you want to keep them.",
                "Load settings", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }
        internal void SaveProfile()
        {
            string name = controller.Profiles.Save(profileNames.Text, ReadSettings());
            RefreshProfiles(name); loadedFingerprint = Fingerprint();
            feedback.Text = "Saved profile '" + name + "'. Apply it to change the running proxy.";
        }
        internal bool LoadProfile(bool confirm)
        {
            if (confirm && !ConfirmLoad()) return false;
            string name = profileNames.Text;
            FillSettings(controller.Profiles.Load(name)); RefreshProfiles(controller.Profiles.Read().Selected);
            feedback.Text = "Loaded profile '" + profileNames.Text + "'. Choose Save and apply to use it.";
            return true;
        }
        internal void RenameProfile(string newName)
        {
            string name = controller.Profiles.Rename(profileNames.Text, newName);
            RefreshProfiles(name); feedback.Text = "Renamed the saved profile to '" + name + "'.";
        }
        internal void ImportProfile(string file)
        {
            string name = controller.Profiles.Import(file);
            FillSettings(controller.Profiles.Load(name)); RefreshProfiles(name);
            feedback.Text = "Imported profile '" + name + "'. Review it, then choose Save and apply to use it.";
        }
        internal ComboBox ProfileDropdown { get { return profileNames; } }
        string PromptProfileName(string current)
        {
            using (var dialog = new Form { Text = "Rename saved settings", ClientSize = new Size(420, 135), Font = Font,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent })
            {
                var label = new Label { Text = "New profile name", AutoSize = true, Left = 16, Top = 14 };
                var input = new TextBox { Text = current, MaxLength = 80, Left = 16, Top = 40, Width = 388, AccessibleName = "New profile name" };
                var ok = new Button { Text = "Rename", DialogResult = DialogResult.OK, Left = 226, Top = 88, Width = 85, Height = 30 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 319, Top = 88, Width = 85, Height = 30 };
                dialog.Controls.AddRange(new Control[] { label, input, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                ThemeControl(dialog, BackColor, ForeColor, apiKey.BackColor, lastDark == true, SystemInformation.HighContrast);
                dialog.Shown += delegate { input.Focus(); input.SelectAll(); int dark = lastDark == true ? 1 : 0; DwmSetWindowAttribute(dialog.Handle, 20, ref dark, sizeof(int)); };
                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
            }
        }
        internal Settings ReadSettings()
        {
            var settings = new Settings { Upstream = upstream.Text.Trim(), Bind = bind.Text.Trim(), Port = (int)port.Value,
                Completion = completion.Text, Reasoning = reasoning.Text, ModelMap = mapping.Text, IgnoreTerms = ignore.Text,
                Passthrough = passthrough.Checked, CaptureBodies = capture.Checked, Debug = debug.Checked,
                Background = background.Checked, NoEnvFile = noEnvFile.Checked, ConfigFile = configFile.Text.Trim(), PidFile = pidFile.Text.Trim(), LogFile = logFile.Text.Trim() };
            settings.SetKey(apiKey.Text); settings.Validate(); return settings;
        }
        async Task ApplyAsync(bool ensureStarted)
        {
            if (busy) return;
            SetBusy(true);
            try
            {
                Settings settings = ReadSettings();
                bool enable = autostart.Checked;
                if (enable && !settings.Background) throw new Exception("Autostart requires background mode. Enable Keep running after closing this window.");
                warningMessage = "";
                string profileName = ProfileStore.Name(profileNames.Text);
                await Task.Run(delegate {
                    controller.Profiles.Save(profileName, settings);
                    controller.Save(settings); controller.SetAutostart(enable);
                    bool running = controller.IsRunning();
                    if (running) controller.Stop();
                    if (running || ensureStarted) controller.Start(settings);
                });
                RefreshProfiles(controller.Profiles.Read().Selected); loadedFingerprint = Fingerprint();
                feedback.Text = "Settings saved." + (enable ? " Autostart is enabled for your Windows sign-in." : " Autostart is disabled.");
            }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
            await RefreshStatusAsync(); RefreshLogs();
        }
        async Task StopAsync()
        {
            if (busy) return; SetBusy(true);
            try { await Task.Run(delegate { controller.Stop(); }); warningMessage = ""; feedback.Text = "Proxy stopped."; }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
            await RefreshStatusAsync();
        }
        void SetBusy(bool value)
        {
            busy = value;
            foreach (Control control in new Control[] { apply, start, stop, profileNames, saveProfile, loadProfile, renameProfile, importProfile, exportProfile }) control.Enabled = !value;
        }
        internal async Task RefreshStatusAsync()
        {
            if (polling || busy || IsDisposed) return;
            polling = true;
            try
            {
                var state = await Task.Run(delegate {
                    string log = controller.ReadLog(); int session = log.LastIndexOf("Starting Anthropic Proxy", StringComparison.Ordinal);
                    if (session >= 0) log = log.Substring(session);
                    return new { Running = controller.IsRunning(), Warning = System.Text.RegularExpressions.Regex.IsMatch(log, @"(?m)^\d{4}-\d{2}-\d{2}T\S+ +(WARN|ERROR) ") };
                });
                bool running = state.Running;
                if (IsDisposed || busy) return;
                bool warning = !String.IsNullOrEmpty(warningMessage) || (running && state.Warning);
                SetIndicator(warning ? State.Warning : running ? State.Running : State.Stopped,
                    warning ? (running ? "Running with warning • " : "Warning • ") + (warningMessage.Length > 0 ? warningMessage : "Open Logs for details.") :
                    running ? "Running • Settings changes apply after saving." : "Stopped • Save and start when ready.");
                stop.Enabled = running; start.Enabled = !running; trayStop.Enabled = running; trayStart.Enabled = !running;
            }
            catch (Exception error) { if (!IsDisposed) SetIndicator(State.Warning, "Status unavailable: " + error.Message); }
            finally { polling = false; }
        }
        void TryAction(Action action) { try { action(); } catch (Exception error) { ShowError(error); } }
        void ShowError(Exception error)
        {
            warningMessage = ErrorDialog.Summary(error);
            SetIndicator(State.Warning, "Warning • " + warningMessage); feedback.Text = warningMessage;
            using (var dialog = new ErrorDialog(error)) {
                bool contrast = SystemInformation.HighContrast;
                bool dark = !contrast && (ThemeOverride ?? SystemDark());
                ThemeControl(dialog, contrast ? SystemColors.Window : dark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(246, 246, 246),
                    contrast ? SystemColors.WindowText : dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(30, 30, 30),
                    contrast ? SystemColors.Window : dark ? Color.FromArgb(43, 43, 43) : Color.White, dark, contrast);
                dialog.ShowDialog(this);
            }
        }
        internal void RefreshLogs()
        {
            try
            {
                string text = controller.ReadLog();
                if (text == lastLog) return;
                lastLog = text; logs.Text = text; logs.SelectionStart = logs.TextLength; logs.ScrollToCaret();
            }
            catch (IOException error) { warningMessage = "Cannot read logs: " + error.Message; feedback.Text = warningMessage; }
        }
        internal static bool SystemDark()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        internal void ApplyTheme()
        {
            bool contrast = SystemInformation.HighContrast;
            bool dark = !contrast && (ThemeOverride ?? SystemDark());
            if (lastDark == dark && lastHighContrast == contrast) return;
            lastDark = dark; lastHighContrast = contrast;
            Color back = contrast ? SystemColors.Window : dark ? Color.FromArgb(30, 30, 30) : Color.FromArgb(246, 246, 246);
            Color fore = contrast ? SystemColors.WindowText : dark ? Color.FromArgb(238, 238, 238) : Color.FromArgb(30, 30, 30);
            Color input = contrast ? SystemColors.Window : dark ? Color.FromArgb(43, 43, 43) : Color.White;
            ThemeControl(this, back, fore, input, dark, contrast);
            SetIndicator(indicator, status.Text);
            trayMenu.BackColor = input; trayMenu.ForeColor = fore;
            foreach (ToolStripItem item in trayMenu.Items) { item.BackColor = input; item.ForeColor = fore; }
            trayState.ForeColor = status.ForeColor;
            if (IsHandleCreated)
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int));
            }
            tabs.Invalidate();
        }
        static void ThemeControl(Control control, Color back, Color fore, Color input, bool dark, bool contrast)
        {
            control.BackColor = (control is TextBoxBase || control is NumericUpDown || control is ComboBox) ? input : back;
            control.ForeColor = fore;
            var button = control as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = contrast;
                button.FlatStyle = contrast ? FlatStyle.System : FlatStyle.Flat;
                button.BackColor = input;
                button.FlatAppearance.BorderColor = dark ? Color.FromArgb(100, 100, 100) : Color.FromArgb(160, 160, 160);
            }
            var link = control as LinkLabel;
            if (link != null) { link.LinkColor = dark ? Color.LightSkyBlue : Color.RoyalBlue; link.VisitedLinkColor = link.LinkColor; }
            var combo = control as ComboBox;
            if (combo != null) combo.FlatStyle = contrast ? FlatStyle.Standard : FlatStyle.Flat;
            var checkbox = control as CheckBox;
            if (checkbox != null) checkbox.FlatStyle = contrast ? FlatStyle.System : FlatStyle.Flat;
            if (control.IsHandleCreated && (control is TextBoxBase || control is NumericUpDown))
                SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            foreach (Control child in control.Controls) ThemeControl(child, back, fore, input, dark, contrast);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ShowMessage) { ShowSettingsWindow(); return; }
            base.WndProc(ref message);
            if (message.Msg == 0x001A || message.Msg == 0x031A) { lastDark = null; if (IsHandleCreated) ApplyTheme(); }
        }
        internal void CapturePreview(string file, bool dark, bool logTab)
        {
            ThemeOverride = dark; tabs.SelectedIndex = logTab ? 1 : 0; ApplyTheme(); RefreshLogs();
            Show(); Application.DoEvents();
            using (var image = new Bitmap(Width, Height)) { DrawToBitmap(image, new Rectangle(0, 0, Width, Height)); image.Save(file); }
        }
    }

    static class Program
    {
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnthropicProxy");
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length > 0 && args[0] == "--package-self-test") {
                    if (!PackagedStartup.IsPackaged) throw new Exception("The Store runtime test has no package identity.");
                    bool enabled = PackagedStartup.Enabled;
                    using (var window = new MainWindow(control: new Controller(Path.Combine(args[1], "package-runtime"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "anthropic-proxy.exe")), automation: true)) {
                        window.Show(); Application.DoEvents();
                        if (!window.TrayVisible) throw new Exception("Packaged tray icon did not initialize.");
                        window.Close();
                    }
                    File.WriteAllText(Path.Combine(args[1], "msix-runtime-result.txt"), "PASS: package identity, packaged startup state " + enabled + ", native window and tray initialization.");
                    return 0;
                }
                if (args.Length > 0 && args[0] == "--installer-preview") { DesktopTests.PreviewInstaller(args[1], args[2]); return 0; }
                if (args.Length > 0 && args[0] == "--self-test") { DesktopTests.Run(args.Skip(1).ToArray()); return 0; }
                var controller = new Controller(DataDirectory, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "anthropic-proxy.exe"));
                if (args.Contains("--uninstall")) { controller.Stop(); controller.SetAutostart(false); return 0; }
                if (args.Contains("--autostart"))
                {
                    controller.AutoStart(); if (!controller.Autostart) return 0;
                }
                bool created;
                using (var mutex = new Mutex(true, @"Local\AnthropicProxySettings", out created))
                {
                    if (!created) {
                        if (!args.Contains("--autostart")) { var window = MainWindow.FindWindow(null, "Anthropic-Proxy-RS-Win"); if (window != IntPtr.Zero) MainWindow.PostMessage(window, MainWindow.ShowMessage, IntPtr.Zero, IntPtr.Zero); }
                        return 0;
                    }
                    Application.Run(new MainWindow(controller, false, args.Contains("--autostart")));
                }
                return 0;
            }
            catch (Exception error)
            {
                if (!args.Contains("--self-test") && !args.Contains("--installer-preview") && !args.Contains("--package-self-test"))
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.WriteAllText(Path.Combine(DataDirectory, "startup-error.txt"), error.ToString());
                }
                if (!args.Contains("--autostart") && !args.Contains("--self-test") && !args.Contains("--installer-preview") && !args.Contains("--package-self-test")) MessageBox.Show(error.Message, "Anthropic-Proxy-RS-Win", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (args.Contains("--self-test") || args.Contains("--installer-preview") || args.Contains("--package-self-test")) File.WriteAllText(Path.Combine(args.Last(), "desktop-test-error.txt"), error.ToString());
                return 1;
            }
        }
    }
}
