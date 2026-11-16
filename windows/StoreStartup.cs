using System;
using System.Diagnostics;
using System.IO;

static class StoreStartup
{
    [STAThread]
    static int Main()
    {
        try
        {
            string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Anthropic-Proxy-RS-Win.exe");
            Process.Start(new ProcessStartInfo(app, "--autostart") { UseShellExecute = false, CreateNoWindow = true });
            return 0;
        }
        catch { return 1; }
    }
}
