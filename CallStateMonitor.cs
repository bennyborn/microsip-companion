using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MicroSIPRemote {

    public enum CallState { Idle, Incoming, Active }

    internal sealed class CallStateMonitor : IDisposable {

        // Window enumeration
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        private const int  GWL_STYLE = -16;
        private const int  GWL_ID = -12;
        private const uint WS_VISIBLE = 0x10000000;

        // Control IDs from MicroSIP resource.h — stable regardless of UI language
        private const int IDC_END  = 1055;
        private const int IDC_HOLD = 1057;

        // Cross-process memory for SB_GETTEXT (kept for future use / debugging)
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint dwFreeType);
        [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, IntPtr lpNumberOfBytesRead);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint SB_GETTEXTA = 0x0402;  // WM_USER + 2
        private const uint SB_GETTEXTW = 0x040D;  // WM_USER + 13

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public event Action<CallState> StateChanged;
        public CallState Current => _current;
        public string DebugInfo => _debugInfo;

        private CallState _current = CallState.Idle;
        private string _debugInfo = "";
        private readonly Timer _timer;
        private bool _disposed;

        public CallStateMonitor() {
            _timer = new Timer(_ => Poll(), null, 0, 500);
        }

        private void Poll() {

            var (next, debug) = Detect();

            _debugInfo = debug;

            if( next != _current ) {
                _current = next;
                StateChanged?.Invoke(next);
            }
        }

        private static( CallState state, string debug ) Detect() {

            Process[] procs;
            try { procs = Process.GetProcessesByName("MicroSIP"); }
            catch { return (CallState.Idle, "no MicroSIP process"); }
            if (procs.Length == 0) return (CallState.Idle, "no MicroSIP process");

            // Collect PIDs to filter EnumWindows results
            var ids = new HashSet<uint>();
            foreach (var p in procs) { try { ids.Add((uint)p.Id); } catch { } }

            // Collect all window titles belonging to MicroSIP for diagnostics
            var windowTitles = new System.Collections.Generic.List<string>();
            bool foundRingin = false;
            EnumWindowsProc cb = (hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (!ids.Contains(pid)) return true;
                var sb = new StringBuilder(256);
                GetWindowText(hWnd, sb, 256);
                var t = sb.ToString();
                if (!string.IsNullOrEmpty(t)) windowTitles.Add(t);
                if (t.IndexOf("Incoming", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("Klingeln",  StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.IndexOf("Eingehend", StringComparison.OrdinalIgnoreCase) >= 0)
                    foundRingin = true;
                return true;
            };
            EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            if (foundRingin)
                return (CallState.Incoming, "ringin window found | windows: " + string.Join(", ", windowTitles));

            // Active: read status bar pane 0 from the main MicroSIP window.
            // UpdateWindowText() sets m_bar.SetPaneText(0, ...) — not the window title.
            IntPtr mainWnd = FindWindow("MicroSIP", null);
            if (mainWnd == IntPtr.Zero)
                return (CallState.Idle, "main window not found | windows: " + string.Join(", ", windowTitles));

            // --- Approach 1: read status bar pane 0 via SB_GETTEXTW ---
            // MFC DoPaint calls SB_SETTEXT(wParam=0, text) when the window is visible,
            // so SB_GETTEXTW(wParam=0) should return the current pane text.
            IntPtr statusBar = IntPtr.Zero;

            // --- Approach 2: End-button visibility (fallback) ---
            // When a call is active, MicroSIP calls ShowWindow(SW_SHOW) on IDC_END.
            // We check the button's own WS_VISIBLE flag (not IsWindowVisible which
            // also checks parents and returns false when MicroSIP is tray-hidden).
            IntPtr endBtnHwnd  = IntPtr.Zero;
            IntPtr holdBtnHwnd = IntPtr.Zero;

            var children = new System.Collections.Generic.List<string>();
            EnumWindowsProc childCb = (hWnd, _) =>
            {
                var cls = new StringBuilder(64);
                GetClassName(hWnd, cls, 64);
                var ttl = new StringBuilder(64);
                GetWindowText(hWnd, ttl, 64);
                string clsStr = cls.ToString();
                string ttlStr = ttl.ToString();
                bool ownVis = (GetWindowLong(hWnd, GWL_STYLE) & WS_VISIBLE) != 0;
                children.Add(clsStr + "=\"" + ttlStr + "\" vis=" + ownVis);
                if (clsStr == "msctls_statusbar32") statusBar = hWnd;
                if (clsStr == "Button")
                {
                    int ctrlId = (int)GetWindowLong(hWnd, GWL_ID);
                    if (ctrlId == IDC_END)  endBtnHwnd  = hWnd;
                    if (ctrlId == IDC_HOLD) holdBtnHwnd = hWnd;
                }
                return true;
            };
            EnumChildWindows(mainWnd, childCb, IntPtr.Zero);
            GC.KeepAlive(childCb);

            // Try status bar first (correct constants: SB_GETTEXTW = WM_USER+13 = 0x040D)
            string paneText = statusBar != IntPtr.Zero ? ReadStatusBarPane(statusBar, 0) : "";

            bool endVisible  = endBtnHwnd  != IntPtr.Zero && (GetWindowLong(endBtnHwnd,  GWL_STYLE) & WS_VISIBLE) != 0;
            bool holdVisible = holdBtnHwnd != IntPtr.Zero && (GetWindowLong(holdBtnHwnd, GWL_STYLE) & WS_VISIBLE) != 0;

            string debugMsg = $"pane0=\"{paneText}\" End={endVisible} Hold={holdVisible} | " +
                              string.Join(", ", children);

            if (IsActiveCallText(paneText)) return (CallState.Active, debugMsg);
            if (endVisible || holdVisible)  return (CallState.Active, debugMsg);
            return (CallState.Idle, debugMsg);
        }

        // Timer pattern like "0:00" or "1:02:30" in the status bar means a call
        // is connected or on hold — this is language-independent.
        // Text matching covers the brief pre-connection phase (Calling/Connecting)
        // where no timer exists yet.
        private static readonly Regex _timerPattern = new Regex(@"\d+:\d{2}", RegexOptions.Compiled);

        private static bool IsActiveCallText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (_timerPattern.IsMatch(text)) return true;
            return text.IndexOf("Calling",    StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Connecting", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Verbinde",   StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadStatusBarPane(IntPtr statusBar, int pane)
        {
            GetWindowThreadProcessId(statusBar, out uint pid);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero) return string.Empty;
            try
            {
                const int bufSize = 512;
                IntPtr remote = VirtualAllocEx(hProcess, IntPtr.Zero, bufSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (remote == IntPtr.Zero) return string.Empty;
                try
                {
                    // SB_GETTEXTW = WM_USER+13 = 0x040D (was incorrectly 0x040E before)
                    SendMessage(statusBar, SB_GETTEXTW, (IntPtr)pane, remote);
                    var local = new byte[bufSize];
                    ReadProcessMemory(hProcess, remote, local, local.Length, IntPtr.Zero);
                    string w = Encoding.Unicode.GetString(local).TrimEnd('\0');
                    if (w.Length > 0) return w;

                    // Fallback: try ANSI (SB_GETTEXTA = WM_USER+2 = 0x0402)
                    SendMessage(statusBar, SB_GETTEXTA, (IntPtr)pane, remote);
                    ReadProcessMemory(hProcess, remote, local, local.Length, IntPtr.Zero);
                    return Encoding.Default.GetString(local).TrimEnd('\0');
                }
                finally { VirtualFreeEx(hProcess, remote, 0, MEM_RELEASE); }
            }
            finally { CloseHandle(hProcess); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}
