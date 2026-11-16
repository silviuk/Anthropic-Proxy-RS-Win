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
[assembly: System.Reflection.AssemblyTitle("Anthropic Proxy")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]

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
            if (!System.Net.IPAddress.TryParse(Bind, out address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new Exception("Enter an IPv4 listening address, such as 127.0.0.1 or 0.0.0.0.");
            if (Port < 1 || Port > 65535) throw new Exception("The listening port must be between 1 and 65535.");
            if (Passthrough && GetKey().Length != 0) throw new Exception("Clear the saved API key before enabling client API key passthrough.");
            foreach (string entry in ModelMap.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('=');
                if (separator < 1 || String.IsNullOrWhiteSpace(entry.Substring(separator + 1)))
                    throw new Exception("Model mapping must use source=target entries separated by semicolons.");
            }
        }
    }

    sealed class Controller
    {
        internal readonly string DirectoryPath, SettingsPath, PidPath, LogPath, Binary;
        internal readonly string RunKey;
        internal const string RunValue = "AnthropicProxy";
        internal Controller(string directory, string binary, string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run")
        {
            DirectoryPath = directory;
            Directory.CreateDirectory(directory);
            SettingsPath = Path.Combine(directory, "settings.json");
            PidPath = Path.Combine(directory, "proxy.pid");
            LogPath = Path.Combine(directory, "requests.log");
            Binary = Path.GetFullPath(binary);
            RunKey = runKey;
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
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && String.Equals(key.GetValue(RunValue) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
            }
        }
        internal string StartupCommand { get { return Quote(Application.ExecutablePath) + " --autostart"; } }
        internal void SetAutostart(bool enabled)
        {
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
            if (!File.Exists(PidPath)) return false;
            int pid;
            if (!Int32.TryParse(File.ReadAllText(PidPath).Trim(), out pid) || pid <= 0) return false;
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
            if (Autostart) Start(Load());
        }
        internal void Start(Settings settings)
        {
            settings.Validate();
            if (IsRunning()) return;
            Execute("--daemon --no-env-file --log-file " + Quote(LogPath) + " --pid-file " + Quote(PidPath), settings);
            if (!IsRunning()) throw new Exception("The proxy did not start. Open Logs for details.");
        }
        internal void Stop()
        {
            if (IsRunning()) Execute("stop --pid-file " + Quote(PidPath), null);
        }
        internal string Execute(string arguments, Settings settings)
        {
            if (!File.Exists(Binary)) throw new Exception("Keep anthropic-proxy.exe in the same folder as this app.");
            ProcessStartInfo info = new ProcessStartInfo(Binary, arguments);
            info.WorkingDirectory = DirectoryPath;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            // Do not create redirected pipes: a detached Windows child can inherit
            // their handles and keep a reader waiting after the launcher exits.
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
                info.EnvironmentVariables["VERBOSE"] = settings.CaptureBodies ? "true" : "false";
            }
            using (Process process = Process.Start(info))
            {
                if (!process.WaitForExit(20000)) { process.Kill(); throw new Exception("The proxy command timed out. Check Logs and the current status."); }
                if (process.ExitCode != 0)
                {
                    string details = ReadLog();
                    if (details.Length > 3000) details = details.Substring(details.Length - 3000);
                    throw new Exception("Proxy command failed (exit code " + process.ExitCode + ").\r\n" + details);
                }
                return "";
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

    sealed class MainWindow : Form
    {
        readonly Controller controller;
        readonly TextBox upstream = new TextBox(), apiKey = new TextBox(), bind = new TextBox(),
            completion = new TextBox(), reasoning = new TextBox(), mapping = new TextBox(), ignore = new TextBox();
        readonly NumericUpDown port = new NumericUpDown();
        readonly CheckBox passthrough = new CheckBox(), capture = new CheckBox(), autostart = new CheckBox(), showKey = new CheckBox();
        readonly Button apply = new Button(), start = new Button(), stop = new Button();
        readonly Label status = new Label(), feedback = new Label();
        readonly RichTextBox logs = new RichTextBox();
        readonly TabControl tabs = new TabControl();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool busy, polling;
        bool? lastDark;
        bool lastHighContrast;
        string lastLog = "";
        internal bool? ThemeOverride;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string app, string id);

        internal MainWindow(Controller control)
        {
            controller = control;
            Text = "Anthropic Proxy";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(760, 740);
            MinimumSize = new Size(700, 660);
            StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var title = new Label { Text = "Anthropic Proxy", AutoSize = true, Font = new Font("Segoe UI", 20, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
            root.Controls.Add(title, 0, 0);
            status.AutoSize = true; status.Margin = new Padding(0, 0, 0, 16); status.Text = "Checking proxy status...";
            root.Controls.Add(status, 0, 1);
            tabs.Dock = DockStyle.Fill;
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed; tabs.ItemSize = new Size(110, 32); tabs.SizeMode = TabSizeMode.Fixed;
            tabs.DrawItem += DrawTab;
            var settingsTab = new TabPage("Settings") { Padding = new Padding(14), AutoScroll = true };
            var logTab = new TabPage("Logs") { Padding = new Padding(10) };
            tabs.TabPages.Add(settingsTab); tabs.TabPages.Add(logTab);
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(fields, "Upstream URL", upstream);
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
            AddField(fields, "Model mapping", mapping);
            mapping.AccessibleDescription = "Optional source=target entries separated by semicolons.";
            AddField(fields, "Ignore prompt terms", ignore);
            var hint = new Label { Text = "Models are optional. Mapping: source=target;other=target.\nUse semicolons for multiple upstream URLs or ignored terms.", AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(3, 2, 3, 10) };
            AddField(fields, "", hint);
            autostart.Text = "Start proxy when I sign in to Windows"; autostart.AutoSize = true;
            AddField(fields, "Autostart", autostart);
            capture.Text = "Record request and response bodies"; capture.AutoSize = true;
            AddField(fields, "Logging", capture);
            AddField(fields, "", new Label { Text = "Includes prompts, responses and streamed content. Saved locally.\nOff by default. Save and apply restarts a running proxy.", AutoSize = true, MaximumSize = new Size(450, 0) });
            settingsTab.Controls.Add(fields);

            var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            logLayout.Controls.Add(new Label { Text = "Live activity • Body logging is controlled in Settings.", AutoSize = true, Margin = new Padding(0, 0, 0, 8) }, 0, 0);
            logs.Dock = DockStyle.Fill; logs.ReadOnly = true; logs.Font = new Font("Consolas", 9); logs.WordWrap = false; logs.DetectUrls = false; logs.AccessibleName = "Request and response logs";
            logLayout.Controls.Add(logs, 0, 1);
            var logButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            var clear = new Button { Text = "Clear logs", AutoSize = true };
            clear.Click += delegate { TryAction(delegate { controller.ClearLogs(); RefreshLogs(); }); };
            var open = new Button { Text = "Open log folder", AutoSize = true };
            open.Click += delegate { TryAction(delegate { Process.Start(new ProcessStartInfo("explorer.exe", Controller.Quote(controller.DirectoryPath)) { UseShellExecute = true }); }); };
            logButtons.Controls.Add(clear); logButtons.Controls.Add(open);
            logLayout.Controls.Add(logButtons, 0, 2); logTab.Controls.Add(logLayout);
            root.Controls.Add(tabs, 0, 2);
            feedback.AutoSize = true; feedback.MaximumSize = new Size(700, 0); feedback.Margin = new Padding(0, 10, 0, 6);
            feedback.Text = "API keys are encrypted for your Windows account. Closing this window keeps the proxy running.";
            root.Controls.Add(feedback, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            apply.Text = "Save and apply"; start.Text = "Save and start"; stop.Text = "Stop proxy";
            foreach (Button button in new[] { apply, start, stop }) { button.AutoSize = true; button.Padding = new Padding(8, 5, 8, 5); actions.Controls.Add(button); }
            apply.Click += async delegate { await ApplyAsync(false); };
            start.Click += async delegate { await ApplyAsync(true); };
            stop.Click += async delegate { await StopAsync(); };
            root.Controls.Add(actions, 0, 4); Controls.Add(root);
            AcceptButton = apply;
            LoadSettings();
            timer.Interval = 1000;
            timer.Tick += async delegate { ApplyTheme(); if (tabs.SelectedIndex == 1) RefreshLogs(); await RefreshStatusAsync(); };
            tabs.SelectedIndexChanged += delegate { RefreshLogs(); };
            Shown += async delegate { ApplyTheme(); timer.Start(); await RefreshStatusAsync(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
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
            Settings settings = controller.Load();
            upstream.Text = settings.Upstream; apiKey.Text = settings.GetKey(); bind.Text = settings.Bind;
            port.Value = Math.Max(1, Math.Min(65535, settings.Port)); completion.Text = settings.Completion;
            reasoning.Text = settings.Reasoning; mapping.Text = settings.ModelMap; ignore.Text = settings.IgnoreTerms;
            passthrough.Checked = settings.Passthrough; capture.Checked = settings.CaptureBodies; autostart.Checked = controller.Autostart;
        }
        internal Settings ReadSettings()
        {
            var settings = new Settings { Upstream = upstream.Text.Trim(), Bind = bind.Text.Trim(), Port = (int)port.Value,
                Completion = completion.Text, Reasoning = reasoning.Text, ModelMap = mapping.Text, IgnoreTerms = ignore.Text,
                Passthrough = passthrough.Checked, CaptureBodies = capture.Checked };
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
                await Task.Run(delegate {
                    controller.Save(settings); controller.SetAutostart(enable);
                    bool running = controller.IsRunning();
                    if (running) controller.Stop();
                    if (running || ensureStarted) controller.Start(settings);
                });
                feedback.Text = "Settings saved." + (enable ? " Autostart is enabled for your Windows sign-in." : " Autostart is disabled.");
            }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
            await RefreshStatusAsync(); RefreshLogs();
        }
        async Task StopAsync()
        {
            if (busy) return; SetBusy(true);
            try { await Task.Run(delegate { controller.Stop(); }); feedback.Text = "Proxy stopped."; }
            catch (Exception error) { ShowError(error); }
            finally { SetBusy(false); }
            await RefreshStatusAsync();
        }
        void SetBusy(bool value) { busy = value; apply.Enabled = start.Enabled = stop.Enabled = !value; }
        async Task RefreshStatusAsync()
        {
            if (polling || busy || IsDisposed) return;
            polling = true;
            try
            {
                bool running = await Task.Run(delegate { return controller.IsRunning(); });
                if (IsDisposed || busy) return;
                status.Text = running ? "Running • Settings changes apply after saving." : "Stopped • Save and start when ready.";
                stop.Enabled = running; start.Enabled = !running;
            }
            catch (Exception error) { if (!IsDisposed) status.Text = "Status unavailable: " + error.Message; }
            finally { polling = false; }
        }
        void TryAction(Action action) { try { action(); } catch (Exception error) { ShowError(error); } }
        void ShowError(Exception error) { feedback.Text = error.Message; MessageBox.Show(this, error.Message, "Anthropic Proxy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        internal void RefreshLogs()
        {
            try
            {
                string text = controller.ReadLog();
                if (text == lastLog) return;
                lastLog = text; logs.Text = text; logs.SelectionStart = logs.TextLength; logs.ScrollToCaret();
            }
            catch (IOException error) { feedback.Text = "Cannot read logs: " + error.Message; }
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
            if (IsHandleCreated)
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref value, sizeof(int));
            }
            tabs.Invalidate();
        }
        static void ThemeControl(Control control, Color back, Color fore, Color input, bool dark, bool contrast)
        {
            control.BackColor = (control is TextBoxBase || control is NumericUpDown) ? input : back;
            control.ForeColor = fore;
            var button = control as Button;
            if (button != null)
            {
                button.UseVisualStyleBackColor = contrast;
                button.FlatStyle = contrast ? FlatStyle.System : FlatStyle.Flat;
                button.BackColor = input;
                button.FlatAppearance.BorderColor = dark ? Color.FromArgb(100, 100, 100) : Color.FromArgb(160, 160, 160);
            }
            var checkbox = control as CheckBox;
            if (checkbox != null) checkbox.FlatStyle = contrast ? FlatStyle.System : FlatStyle.Flat;
            if (control.IsHandleCreated && (control is TextBoxBase || control is NumericUpDown))
                SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            foreach (Control child in control.Controls) ThemeControl(child, back, fore, input, dark, contrast);
        }
        void DrawTab(object sender, DrawItemEventArgs args)
        {
            bool selected = args.Index == tabs.SelectedIndex;
            Color background = lastDark == true ? Color.FromArgb(selected ? 43 : 30, selected ? 43 : 30, selected ? 43 : 30) : SystemColors.Control;
            if (SystemInformation.HighContrast) background = selected ? SystemColors.Highlight : SystemColors.Window;
            Color foreground = SystemInformation.HighContrast && selected ? SystemColors.HighlightText : ForeColor;
            using (var brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
            TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, Font, args.Bounds, foreground, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void WndProc(ref Message message)
        {
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
                if (args.Length > 0 && args[0] == "--self-test") { DesktopTests.Run(args.Skip(1).ToArray()); return 0; }
                var controller = new Controller(DataDirectory, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "anthropic-proxy.exe"));
                if (args.Contains("--autostart"))
                {
                    controller.AutoStart(); return 0;
                }
                bool created;
                using (var mutex = new Mutex(true, @"Local\AnthropicProxySettings", out created))
                {
                    if (!created) { MessageBox.Show("The settings window is already open.", "Anthropic Proxy"); return 0; }
                    Application.Run(new MainWindow(controller));
                }
                return 0;
            }
            catch (Exception error)
            {
                if (!args.Contains("--self-test"))
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.WriteAllText(Path.Combine(DataDirectory, "startup-error.txt"), error.ToString());
                }
                if (!args.Contains("--autostart") && !args.Contains("--self-test")) MessageBox.Show(error.Message, "Anthropic Proxy", MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (args.Contains("--self-test")) File.WriteAllText(Path.Combine(args.Last(), "desktop-test-error.txt"), error.ToString());
                return 1;
            }
        }
    }
}
