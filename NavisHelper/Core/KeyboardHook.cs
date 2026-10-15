using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Core
{
    /// <summary>
    /// Перехват клавиатуры в потоке интерфейса Navisworks.
    /// Передаёт нажатия клавиш без Alt обработчикам горячих клавиш.
    /// </summary>
    public sealed class KeyboardHook : IDisposable
    {
        public event Action<Key, ModifierKeys> KeyPressed;

        private IntPtr _hookId = IntPtr.Zero;
        private readonly KeyboardProc _proc;

        private const int WH_KEYBOARD = 2;

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero) return;
            _hookId = SetWindowsHookEx(WH_KEYBOARD, _proc, IntPtr.Zero, GetCurrentThreadId());
            if (_hookId == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
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
            if (KeyboardHookPolicy.ShouldDispatch(nCode, lParam.ToInt64()))
            {
                var key = KeyInterop.KeyFromVirtualKey(wParam.ToInt32());
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
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private delegate IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, KeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
