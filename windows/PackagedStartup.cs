using System;
#if MSIX_BUILD
using System.Runtime.InteropServices;
using System.Text;
using Windows.ApplicationModel;
#endif

namespace AnthropicProxyDesktop
{
    static class PackagedStartup
    {
#if MSIX_BUILD
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);
        internal static bool IsPackaged { get { int size = 0; return GetCurrentPackageFullName(ref size, null) == 122; } }
        static StartupTask Task()
        {
            return System.WindowsRuntimeSystemExtensions.AsTask(StartupTask.GetAsync("AnthropicProxyStartup")).GetAwaiter().GetResult();
        }
        internal static bool Enabled { get { var state = Task().State; return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy; } }
        internal static void SetEnabled(bool enabled)
        {
            var task = Task();
            if (!enabled) { task.Disable(); return; }
            var state = System.WindowsRuntimeSystemExtensions.AsTask(task.RequestEnableAsync()).GetAwaiter().GetResult();
            if (state != StartupTaskState.Enabled && state != StartupTaskState.EnabledByPolicy)
                throw new Exception(state == StartupTaskState.DisabledByUser ?
                    "Enable Anthropic-Proxy-RS-Win in Windows Settings > Apps > Startup, then save again." :
                    "Windows policy does not allow this startup task to be enabled.");
        }
#else
        internal static bool IsPackaged { get { return false; } }
        internal static bool Enabled { get { return false; } }
        internal static void SetEnabled(bool enabled) { throw new InvalidOperationException("This build does not use packaged startup."); }
#endif
    }
}
