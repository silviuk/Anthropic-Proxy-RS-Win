using System;
#if MSIX_BUILD
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
        [DllImport("StartupControl.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32)]
        static extern int ControlStartup(int operation, out int state);

        enum Operation { Read, Enable, Disable }
        enum State { Disabled, DisabledByUser, Enabled, DisabledByPolicy, EnabledByPolicy }
        static State Command(Operation operation)
        {
            try {
                int state;
                int result = ControlStartup((int)operation, out state);
                if (result < 0) {
                    Exception error = Marshal.GetExceptionForHR(result, new IntPtr(-1));
                    throw new InvalidOperationException(String.Format("Windows startup control failed (0x{0:X8}): {1}", result, error.Message), error);
                }
                return (State)state;
            } catch (DllNotFoundException error) {
                throw new InvalidOperationException("Windows startup control could not load its packaged component.", error);
            } catch (EntryPointNotFoundException error) {
                throw new InvalidOperationException("Windows startup control could not find its packaged entry point.", error);
            } catch (BadImageFormatException error) {
                throw new InvalidOperationException("Windows startup control could not use its packaged component.", error);
            }
        }
        static bool IsEnabled(State state) { return state == State.Enabled || state == State.EnabledByPolicy; }
        internal static bool Enabled { get { return IsEnabled(Command(Operation.Read)); } }
        internal static void SetEnabled(bool enabled)
        {
            State state = Command(enabled ? Operation.Enable : Operation.Disable);
            if (enabled != IsEnabled(state))
                throw new InvalidOperationException(state == State.DisabledByUser
                    ? "Enable Anthropic-Proxy-RS-Win in Windows Settings > Apps > Startup, then try again."
                    : "Windows did not allow the requested change to sign-in startup.");
        }
#else
        internal static bool IsPackaged { get { return false; } }
        internal static bool Enabled { get { return false; } }
        internal static void SetEnabled(bool enabled) { throw new InvalidOperationException("This build does not use packaged startup."); }
#endif
    }
}
