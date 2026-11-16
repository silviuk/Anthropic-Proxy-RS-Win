using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AnthropicProxyDesktop
{
    static class DesktopTests
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        internal static void Run(string[] args)
        {
            string binary = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
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

                controller.AutoStart(); Require(!controller.IsRunning(), "Disabled autostart started proxy");
                controller.SetAutostart(true); controller.AutoStart(); controller.SetAutostart(false);
                Require(controller.IsRunning(), "Enabled autostart did not start proxy");
                controller.Start(settings); // Repeated start must leave the managed instance running.
                string health = new WebClient().DownloadString("http://127.0.0.1:" + port + "/health");
                Require(health == "OK", "Health failed");
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
                using (var window = new MainWindow(controller))
                {
                    window.Show(); Application.DoEvents();
                    Require(window.ReadSettings().GetKey() == "desktop-secret-key", "API key did not load into settings UI");
                    Require(window.ReadSettings().Port == port, "Port did not load into settings UI");
                    window.CapturePreview(Path.Combine(output, "settings-light.png"), false, false);
                    Require(window.BackColor.GetBrightness() > 0.5f, "Light theme did not apply");
                    window.CapturePreview(Path.Combine(output, "settings-dark.png"), true, false);
                    Require(window.BackColor.GetBrightness() < 0.5f, "Dark theme did not apply");
                    window.CapturePreview(Path.Combine(output, "logs-dark.png"), true, true);
                    window.ThemeOverride = null; window.ApplyTheme();
                    Require((window.BackColor.GetBrightness() < 0.5f) == MainWindow.SystemDark(), "System theme selection failed");
                    window.Close(); Application.DoEvents();
                }
                Require(controller.IsRunning(), "Closing settings stopped the proxy");
                controller.Stop(); Require(!controller.IsRunning(), "UI controller could not stop proxy");
                settings.CaptureBodies = false; controller.Save(settings); controller.ClearLogs();
                controller.Start(settings); controller.Stop();
                Require(!controller.ReadLog().Contains("desktop-request-marker"), "Clearing logs did not remove bodies");
                Require(!controller.Load().CaptureBodies, "Logging disable did not persist");
                File.WriteAllText(Path.Combine(output, "desktop-test-result.txt"), "PASS: saved settings, DPAPI key encryption, input validation, autostart enable/disable, quoted paths, native window rendering, light/dark/system themes, proxy start/stop, requests/responses in logs, closing UI keeps proxy running, log clearing and capture setting persistence.\r\n");
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
