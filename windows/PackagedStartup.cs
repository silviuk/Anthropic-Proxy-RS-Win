using System;
#if MSIX_BUILD
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace AnthropicProxyDesktop
{
    static class PackagedStartup
    {
#if MSIX_BUILD
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);
        internal static bool IsPackaged { get { int size = 0; return GetCurrentPackageFullName(ref size, null) == 122; } }
        static int Command(string command)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StartupControl.exe");
            var info = new ProcessStartInfo(executable, command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(info)) {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Windows startup control timed out."); }
                int state;
                if (process.ExitCode != 0 || !Int32.TryParse(output.GetAwaiter().GetResult().Trim(), out state))
                    throw new Exception("Windows startup control failed: " + error.GetAwaiter().GetResult());
                return state;
            }
        }
        internal static bool Enabled { get { int state = Command("state"); return state == 2 || state == 4; } }
        internal static void SetEnabled(bool enabled)
        {
            int state = Command(enabled ? "enable" : "disable");
            if (enabled && state != 2 && state != 4)
                throw new Exception(state == 1 ? "Enable Anthropic-Proxy-RS-Win in Windows Settings > Apps > Startup, then save again." : "Windows policy does not allow this startup task to be enabled.");
        }
#else
        internal static bool IsPackaged { get { return false; } }
        internal static bool Enabled { get { return false; } }
        internal static void SetEnabled(bool enabled) { throw new InvalidOperationException("This build does not use packaged startup."); }
#endif
    }
}
