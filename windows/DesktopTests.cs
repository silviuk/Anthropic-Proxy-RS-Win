using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using Microsoft.Win32;

namespace AnthropicProxyDesktop
{
    static class DesktopTests
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Reject(Action action, string message)
        {
            bool rejected = false; try { action(); } catch { rejected = true; }
            Require(rejected, message);
        }
        static void TestProfiles(Controller controller, string directory, Settings active)
        {
            var store = controller.Profiles;
            Require(store.Read().Profiles.Single().Name == "Default", "Existing settings did not migrate to Default");
            Require(store.Load("Default").GetKey() == "desktop-secret-key", "Migration lost the encrypted key");
            var alternate = new Settings { Upstream = "https://example.com/v1", Port = 3210, Completion = "alternate-model" };
            alternate.SetKey("alternate-secret"); store.Save("Local / Example", alternate);
            Require(store.Load("local / example").Port == 3210, "Profile lookup should ignore case");
            alternate.Port = 3211; store.Save("LOCAL / EXAMPLE", alternate);
            Require(store.Read().Profiles.Count == 2 && store.Load("Local / Example").Port == 3211, "Save did not replace existing profile");
            store.Rename("Local / Example", "Local example");
            Require(store.Read().Selected == "Local example", "Rename did not preserve selection");
            Require(store.Load("Local example").GetKey() == "alternate-secret", "Rename lost profile values");
            Reject(delegate { store.Rename("Local example", "Default"); }, "Duplicate rename should be rejected");
            Reject(delegate { store.Save("  ", alternate); }, "Blank profile name should be rejected");
            string import = Path.Combine(directory, "environment.env");
            File.WriteAllText(import, "# imported settings\r\nexport UPSTREAM_BASE_URL='https://example.com/v2'\r\nUPSTREAM_API_KEY='import-secret#$literal' # comment\r\nPORT=3111\r\nCOMPLETION_MODEL=import-model\r\nANTHROPIC_PROXY_SYSTEM_PROMPT_IGNORE_TERMS=\"one;two\\nthree\"\r\nVERBOSE=false\r\nUNRELATED_OPTION=ignored\r\n");
            string imported = store.Import(import);
            Require(store.Load(imported).GetKey() == "import-secret#$literal", "Import did not preserve quoted API key");
            Require(store.Load(imported).IgnoreTerms == "one;two\nthree", "Import did not decode double-quoted escapes");
            Require(store.Import(import) == "environment (2)", "Repeated import overwrote an existing profile");
            store.Rename(imported, "Imported configuration");
            Require(store.Import(controller.SettingsPath) == "settings", "Desktop JSON import failed");
            Require(store.Load("settings").GetKey() == active.GetKey(), "JSON import lost encrypted key");
            Require(controller.Load().Port == active.Port && !controller.Autostart, "Profiles changed applied settings or autostart");
            string path = Path.Combine(directory, "profiles.json"); string before = File.ReadAllText(path);
            Require(!before.Contains("desktop-secret-key") && !before.Contains("import-secret#$literal") && !before.Contains("alternate-secret"), "A profile API key was stored in plaintext");
            string bad = Path.Combine(directory, "bad.env");
            foreach (string contents in new[] { "PORT=9000", "UPSTREAM_BASE_URL=invalid", "UPSTREAM_BASE_URL=https://example.com\nVERBOSE=maybe", "UPSTREAM_BASE_URL=\"unclosed", "{\"Port\":1}", "null" })
            {
                File.WriteAllText(bad, contents); Reject(delegate { store.Import(bad); }, "Invalid import should fail");
                Require(File.ReadAllText(path) == before, "Failed import changed existing profiles");
            }
            store.Load("Default");
        }
        static void Pump(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do { Application.DoEvents(); Thread.Sleep(10); } while (!task.IsCompleted && DateTime.UtcNow < deadline);
            Require(task.IsCompleted, "UI operation timed out"); task.GetAwaiter().GetResult(); Application.DoEvents();
        }
        static void TestExport(Controller controller, string directory)
        {
            Settings settings = controller.Load(); string file = Path.Combine(directory, "export.json");
            ProfileStore.Export(file, settings, false);
            string json = File.ReadAllText(file);
            Require(!json.Contains("ApiKey") && !json.Contains("Secret") && !json.Contains(settings.GetKey()), "Default export included key material");
            string imported = controller.Profiles.Import(file);
            Require(controller.Profiles.Load(imported).GetKey() == "", "Key-free export restored a key");
            ProfileStore.Export(file, settings, true);
            imported = controller.Profiles.Import(file);
            Require(controller.Profiles.Load(imported).GetKey() == settings.GetKey(), "Explicit key export did not round-trip");
            controller.Profiles.Load("Default");
        }
        internal static void Run(string[] args)
        {
            string binary = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            Action<string> progress = delegate(string text) { File.AppendAllText(Path.Combine(output, "desktop-test-progress.txt"), text + "\r\n"); };
            progress("Starting settings and encryption tests");
            string directory = Path.Combine(Path.GetTempPath(), "Anthropic Proxy UI test " + Guid.NewGuid().ToString("N"));
            string runKey = @"Software\AnthropicProxy\Tests\" + Guid.NewGuid().ToString("N");
            var controller = new Controller(directory, binary, runKey);
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start(); int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
            var upstream = new TcpListener(IPAddress.Loopback, 0);
            upstream.Start(); int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
            var settings = new Settings { Upstream = "http://127.0.0.1:" + upstreamPort, Port = port, CaptureBodies = true, Completion = "test-model" };
            settings.SetKey("desktop-secret-key");
            try
            {
                Require(controller.Help().Contains("--daemon"), "Upstream CLI help is not available");
                controller.Save(settings);
                Require(!File.ReadAllText(controller.SettingsPath).Contains("desktop-secret-key"), "Key was saved in plaintext");
                Require(controller.Load().GetKey() == "desktop-secret-key", "Saved key did not decrypt");
                Require(controller.Load().CaptureBodies, "Logging setting did not persist");
                settings.Port = port; controller.Save(settings); // Exercise atomic replacement.
                Require(!controller.Autostart, "Autostart should default to disabled");
                controller.SetAutostart(true); Require(controller.Autostart, "Autostart did not enable");
                using (var key = Registry.CurrentUser.OpenSubKey(runKey))
                    Require((string)key.GetValue(Controller.RunValue) == Controller.Quote(Application.ExecutablePath) + " --autostart", "Startup command is not quoted correctly");
                controller.SetAutostart(false); Require(!controller.Autostart, "Autostart did not disable");
                Require(Controller.Quote(@"C:\folder with spaces\") == "\"C:\\folder with spaces\\\\\"", "Trailing backslash quoting failed");
                bool rejected = false;
                try { var invalid = new Settings { Upstream = "invalid" }; controller.Save(invalid); }
                catch { rejected = true; }
                Require(rejected, "Invalid URL was accepted");

                progress("Starting named settings profile and import tests");
                TestProfiles(controller, directory, settings);
                TestExport(controller, directory);

                progress("Starting autostart and process control tests");
                controller.AutoStart(); Require(!controller.IsRunning(), "Disabled autostart started proxy");
                controller.SetAutostart(true); controller.AutoStart(); controller.SetAutostart(false);
                Require(controller.IsRunning(), "Enabled autostart did not start proxy");
                controller.Start(settings); // Repeated start must leave the managed instance running.
                string health = new WebClient().DownloadString("http://127.0.0.1:" + port + "/health");
                Require(health == "OK", "Health failed");
                progress("Starting request and log capture tests");
                Task serve = Task.Run(delegate {
                    using (TcpClient connection = upstream.AcceptTcpClient())
                    using (NetworkStream stream = connection.GetStream())
                    {
                        var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                        string line; int length = 0;
                        reader.ReadLine();
                        while (!String.IsNullOrEmpty(line = reader.ReadLine()))
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = Int32.Parse(line.Substring(15).Trim());
                        char[] body = new char[length]; int read = 0;
                        while (read < length) { int count = reader.Read(body, read, length - read); if (count == 0) throw new Exception("Short upstream request"); read += count; }
                        Require(new string(body).Contains("test-model"), "UI model setting not used");
                        string json = "{\"id\":\"desktop-test\",\"model\":\"test-model\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"desktop-response-marker\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
                        byte[] bytes = Encoding.UTF8.GetBytes(json);
                        byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                        stream.Write(header, 0, header.Length); stream.Write(bytes, 0, bytes.Length);
                    }
                });
                using (var client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.ContentType] = "application/json";
                    string response = client.UploadString("http://127.0.0.1:" + port + "/v1/messages", "{\"model\":\"client-model\",\"max_tokens\":16,\"messages\":[{\"role\":\"user\",\"content\":\"desktop-request-marker\"}]}");
                    Require(response.Contains("desktop-response-marker"), "Request from UI-managed proxy failed");
                }
                Require(serve.Wait(10000), "Upstream test timed out");
                string log = controller.ReadLog();
                Require(log.Contains("desktop-request-marker") && log.Contains("desktop-response-marker"), "Request/response logging missing");
                Require(!log.Contains("desktop-secret-key"), "API key leaked to logs");
                progress("Starting native window and theme rendering tests");
                using (var window = new MainWindow(controller))
                {
                    window.Show(); Application.DoEvents();
                    Require(window.ReadSettings().GetKey() == "desktop-secret-key", "API key did not load into settings UI");
                    Require(window.ReadSettings().Port == port, "Port did not load into settings UI");
                    Require(window.ProfileDropdown.Items.Contains("Default"), "Default profile is missing from dropdown");
                    window.ProfileDropdown.Text = "Work laptop"; window.SaveProfile();
                    Require(window.ProfileDropdown.Items.Contains("Work laptop"), "Saving a profile did not update dropdown");
                    window.RenameProfile("Work setup");
                    Require(window.ProfileDropdown.Items.Contains("Work setup") && !window.ProfileDropdown.Items.Contains("Work laptop"), "Renaming did not update dropdown");
                    window.ProfileDropdown.Text = "Imported configuration"; window.LoadProfile(false);
                    Require(window.ReadSettings().GetKey() == "import-secret#$literal", "Loading from dropdown did not restore imported key");
                    Require(window.ReadSettings().Port == 3111, "Loading from dropdown did not restore imported port");
                    Require(controller.Load().Port == port && controller.IsRunning(), "Loading a profile changed the active proxy settings");
                    window.ImportProfile(Path.Combine(directory, "environment.env"));
                    Require(window.ProfileDropdown.Text.StartsWith("environment"), "Import did not select new dropdown profile");
                    window.ProfileDropdown.Text = "Work setup"; window.LoadProfile(false);
                    Require(window.ReadSettings().GetKey() == "desktop-secret-key", "Switching profiles lost the encrypted key");
                    Pump(window.RefreshStatusAsync());
                    window.CapturePreview(Path.Combine(output, "settings-light.png"), false, false);
                    Require(window.StatusColor == Color.ForestGreen, "Running state is not green in light mode");
                    Require(window.BackColor.GetBrightness() > 0.5f, "Light theme did not apply");
                    window.CapturePreview(Path.Combine(output, "settings-dark.png"), true, false);
                    Require(window.BackColor.GetBrightness() < 0.5f, "Dark theme did not apply");
                    window.CapturePreview(Path.Combine(output, "logs-dark.png"), true, true);
                    using (var options = new ExportOptions()) {
                        window.ApplyDialogTheme(options); options.Show(window); Application.DoEvents();
                        Require(!options.IncludeKey.Checked && !options.Warning.Visible, "Export key checkbox must default to off");
                        options.IncludeKey.Checked = true; Application.DoEvents();
                        Require(options.Warning.Visible && options.Warning.Text == "don't share with others", "Missing key-sharing warning");
                        using (var image = new Bitmap(options.Width, options.Height)) { options.DrawToBitmap(image, new Rectangle(0, 0, options.Width, options.Height)); image.Save(Path.Combine(output, "export-warning.png")); }
                        options.Close();
                    }
                    File.AppendAllText(controller.LogPath, "\r\n2026-11-16T00:00:00Z  WARN anthropic_proxy: Test warning generated by desktop tests\r\n");
                    Pump(window.RefreshStatusAsync());
                    Require(window.StatusColor == Color.Orange, "Warning state is not orange in dark mode");
                    window.CapturePreview(Path.Combine(output, "warning-dark.png"), true, false);
                    controller.Stop(); Pump(window.RefreshStatusAsync());
                    Require(window.StatusColor == Color.Salmon, "Stopped state is not red in dark mode");
                    controller.Start(settings); controller.ClearLogs();
                    window.ThemeOverride = null; window.ApplyTheme();
                    Require((window.BackColor.GetBrightness() < 0.5f) == MainWindow.SystemDark(), "System theme selection failed");
                    window.Close(); Application.DoEvents();
                }
                progress("Starting stop and log clearing tests");
                Require(controller.IsRunning(), "Closing settings stopped the proxy");
                controller.Stop(); Require(!controller.IsRunning(), "UI controller could not stop proxy");
                settings.CaptureBodies = false; controller.Save(settings); controller.ClearLogs();
                controller.Start(settings); controller.Stop();
                Require(!controller.ReadLog().Contains("desktop-request-marker"), "Clearing logs did not remove bodies");
                Require(!controller.Load().CaptureBodies, "Logging disable did not persist");
                var optionsSettings = new Settings { Upstream = settings.Upstream, Port = port, Debug = true, Background = false,
                    NoEnvFile = false, ConfigFile = Path.Combine(directory, "runtime.env"),
                    PidFile = Path.Combine(directory, "custom process.pid"), LogFile = Path.Combine(directory, "custom requests.log") };
                File.WriteAllText(optionsSettings.ConfigFile, "COMPLETION_MODEL=config-model\n");
                string arguments = Controller.StartArguments(optionsSettings, optionsSettings.PidFile, optionsSettings.LogFile);
                Require(arguments.Contains("--debug") && arguments.Contains("--config") && !arguments.Contains("--verbose") && !arguments.Contains("--no-env-file"), "Logging/config flags do not match CLI settings");
                optionsSettings.CaptureBodies = true;
                Require(Controller.StartArguments(optionsSettings, optionsSettings.PidFile, optionsSettings.LogFile).Contains("--verbose"), "Verbose flag was not forwarded");
                optionsSettings.CaptureBodies = false;
                controller.Start(optionsSettings);
                Require(controller.HasForeground && controller.PidPath == optionsSettings.PidFile && controller.LogPath == optionsSettings.LogFile, "Foreground/custom path controls failed");
                Require(new WebClient().DownloadString("http://127.0.0.1:" + port + "/health") == "OK", "Foreground proxy is not serving");
                controller.Stop(); Require(!controller.IsRunning(), "Foreground stop failed");
                File.WriteAllText(Path.Combine(output, "desktop-test-result.txt"), "PASS: saved settings, DPAPI key encryption, input validation, autostart enable/disable, quoted paths, native window rendering, light/dark/system themes, proxy start/stop, requests/responses in logs, closing UI keeps proxy running, log clearing and capture setting persistence, profile migration/save/load/rename, dropdown selection, .env and encrypted JSON import, duplicate import names and invalid-import preservation, export without key by default and explicit-key round-trip, export warning, green/red/orange status, debug/verbose CLI flags, config file, foreground mode and custom PID/log files.\r\n");
            }
            finally
            {
                try { controller.Stop(); } catch {}
                upstream.Stop();
                Registry.CurrentUser.DeleteSubKeyTree(runKey, false);
                Directory.Delete(directory, true);
            }
        }
    }
}
