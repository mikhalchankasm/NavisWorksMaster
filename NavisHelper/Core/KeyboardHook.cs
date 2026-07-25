using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace NavisHelper.Core
{
    /// <summary>
    /// Глобальный хук клавиатуры для Navisworks.
    /// Перехватывает нажатия когда Navisworks в фокусе.
    /// </summary>
    public sealed class KeyboardHook : IDisposable
    {
        public event Action<Key, ModifierKeys> KeyPressed;

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                    GetModuleHandle(module.ModuleName), 0);
            }
        }

        public void Uninstall()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            Uninstall();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                // Проверяем что Navisworks в фокусе
                var foreground = GetForegroundWindow();
                var currentProcess = Process.GetCurrentProcess().Id;
                GetWindowThreadProcessId(foreground, out uint processId);

                if (processId == currentProcess)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    var key = KeyInterop.KeyFromVirtualKey(vkCode);
                    var modifiers = Keyboard.Modifiers;

                    try
                    {
                        KeyPressed?.Invoke(key, modifiers);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Keyboard hook handler failed: " + ex.Message, "KeyboardHook");
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
