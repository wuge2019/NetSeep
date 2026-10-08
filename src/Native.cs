// NetSeep - Win32 互操作层
// 只依赖 .NET Framework 自带的系统 DLL，无需任何第三方组件。
// 设计原则：只声明真正用到的 API，尽量不触及容易被安全软件的行为引擎关注的接口
// （不枚举前台窗口、不创建控制台、不写注册表 —— 详见 README「杀软误报」一节）。
using System;
using System.Runtime.InteropServices;

namespace NetSeep
{
    internal static class Native
    {
        // ---- 网卡类型 / 状态 ----
        public const int IF_TYPE_SOFTWARE_LOOPBACK = 24;
        public const int IF_TYPE_TUNNEL = 131;
        public const uint IF_OPER_STATUS_UP = 1;

        // ---- 窗口扩展样式 ----
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int GWL_EXSTYLE = -20;

        // ---- UpdateLayeredWindow ----
        public const int ULW_ALPHA = 0x02;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const int DIB_RGB_COLORS = 0;
        public const int BI_RGB = 0;

        // ---- SetWindowPos ----
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // ---- 消息 ----
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DPICHANGED = 0x02E0;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

        // ---- 热键（仅在开启“鼠标穿透”时才注册，用作逃生热键）----
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_NOREPEAT = 0x4000;

        // ---- 用户通知状态（SHQueryUserNotificationState）----
        // 用系统自己的“当前是否适合弹通知”判断来代替枚举前台窗口，
        // 既能识别全屏游戏/演示模式，又不需要探测别人的窗口。
        public const int QUNS_NOT_PRESENT = 1;
        public const int QUNS_BUSY = 2;
        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        public const int QUNS_PRESENTATION_MODE = 4;
        public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
        public const int QUNS_QUIET_TIME = 6;
        public const int QUNS_APP = 7;

        public const int LOGPIXELSY = 88;
        public const int ATTACH_PARENT_PROCESS = -1;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int w, int h) { cx = w; cy = h; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        // =====================================================================
        //  iphlpapi —— GetIfTable2 返回的是 MIB_IF_TABLE2：
        //      ULONG NumEntries;  MIB_IF_ROW2 Table[];
        //  其中 Table 相对表首偏移 8 字节。不同 SDK / 系统版本里
        //  MIB_IF_ROW2 的 PermanentPhysicalAddress 可能是 UCHAR[32] 或
        //  ULONG[32]，整行长度会相差 96 字节，因此这里不套结构体，
        //  而是在运行时探测行布局后按偏移取值（见 Traffic.cs）。
        // =====================================================================
        public const int IF_TABLE_ROWS_OFFSET = 8;

        [DllImport("iphlpapi.dll")]
        public static extern uint GetIfTable2(out IntPtr table);

        [DllImport("iphlpapi.dll")]
        public static extern void FreeMibTable(IntPtr memory);

        // =====================================================================
        //  shell32 —— 注意：SHQueryUserNotificationState 在 shell32.dll 里
        //  （不是 shlwapi.dll；写错 DLL 会在运行期抛 EntryPointNotFoundException，
        //    导致“全屏时自动隐藏”静默失效）
        // =====================================================================
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        // =====================================================================
        //  user32 / gdi32
        // =====================================================================
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage,
            out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        /// <summary>按窗口所在显示器的实际 DPI 返回缩放系数（1.0 = 96 DPI）。</summary>
        public static float GetDpiScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi >= 48 && dpi <= 960) return dpi / 96f;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    int dpi = GetDeviceCaps(dc, LOGPIXELSY);
                    ReleaseDC(IntPtr.Zero, dc);
                    if (dpi >= 48 && dpi <= 960) return dpi / 96f;
                }
            }
            catch (Exception) { }
            return 1f;
        }

        /// <summary>
        /// 当前是否处于“全屏程序/演示模式”，用于决定浮窗是否让位。
        /// SHQueryUserNotificationState 是系统提供的标准查询，失败时按“否”处理。
        /// </summary>
        public static bool IsFullscreenBusy()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) != 0) return false;
                return state == QUNS_BUSY
                    || state == QUNS_RUNNING_D3D_FULL_SCREEN
                    || state == QUNS_PRESENTATION_MODE;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            catch (Exception) { }
            return false;
        }
    }
}
