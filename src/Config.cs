// NetSeep - 配置持久化与开机自启
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
#if !MINIMAL_BUILD
using Microsoft.Win32;
#endif

namespace NetSeep
{
    internal sealed class AppConfig
    {
        public const string AppName = "NetSeep";
        public const string RunKeyValueName = "NetSeep";

        public Guid NicId = Guid.Empty;
        public bool ShowUp = true;
        public bool ShowDown = true;
        public bool ShowGraph = false;
        public int IntervalMs = 1000;
        public int OpacityPercent = 88;
        public string Theme = Themes.DefaultId;   // dark / light / midnight / graphite / contrast / glass / system / custom
        public int BgAlpha = 190;
        public bool TopMost = true;
        public bool Locked = false;
        public bool ClickThrough = false;
        public bool AutoHideFullscreen = true;
        public int? X = null;
        public int? Y = null;

        // ---- 桌面宠物 ----
        public bool Pet = true;
        public int PetSize = 100;      // 百分比
        public int? PetX = null;
        public int? PetY = null;

        public string UpColor = "90,214,140";
        public string DownColor = "86,170,245";
        public string BgColor = "24,24,27";

        /// <summary>本次是否首次运行（配置文件原本不存在）。</summary>
        public bool FirstRun = false;

        private static string PortableDir
        {
            get
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                if (File.Exists(Path.Combine(exeDir, "portable.txt")))
                    return Path.Combine(exeDir, "NetSeepData");
                return null;
            }
        }

        public static string DataDir
        {
            get
            {
                string p = PortableDir;
                if (p != null) return p;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetSeep");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(DataDir, "config.ini"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(DataDir, "error.log"); }
        }

        // ------------------------------------------------------------------
        //  读写
        // ------------------------------------------------------------------
        public static AppConfig Load()
        {
            AppConfig c = new AppConfig();
            string path = FilePath;
            if (!File.Exists(path))
            {
                c.FirstRun = true;
                return c;
            }

            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();
                    c.Set(k, v);
                }
            }
            catch (Exception)
            {
                // 配置损坏时退回默认值，不影响启动
            }

            c.Normalize();
            return c;
        }

        private void Set(string key, string v)
        {
            switch (key)
            {
                case "nic": NicId = ParseGuid(v); break;
                case "showup": ShowUp = ParseBool(v, ShowUp); break;
                case "showdown": ShowDown = ParseBool(v, ShowDown); break;
                case "showgraph": ShowGraph = ParseBool(v, ShowGraph); break;
                case "intervalms": IntervalMs = ParseInt(v, IntervalMs); break;
                case "opacity": OpacityPercent = ParseInt(v, OpacityPercent); break;
                case "theme": Theme = v; break;
                case "bgalpha": BgAlpha = ParseInt(v, BgAlpha); break;
                case "topmost": TopMost = ParseBool(v, TopMost); break;
                case "locked": Locked = ParseBool(v, Locked); break;
                case "clickthrough": ClickThrough = ParseBool(v, ClickThrough); break;
                case "autohidefullscreen": AutoHideFullscreen = ParseBool(v, AutoHideFullscreen); break;
                case "x": X = ParseInt(v, 0); break;
                case "y": Y = ParseInt(v, 0); break;
                case "pet": Pet = ParseBool(v, Pet); break;
                case "petsize": PetSize = ParseInt(v, PetSize); break;
                case "petx": PetX = ParseInt(v, 0); break;
                case "pety": PetY = ParseInt(v, 0); break;
                case "upcolor": UpColor = v; break;
                case "downcolor": DownColor = v; break;
                case "bgcolor": BgColor = v; break;
            }
        }

        public void Normalize()
        {
            if (!ShowUp && !ShowDown) ShowUp = true;
            IntervalMs = Clamp(IntervalMs, 250, 10000);
            OpacityPercent = Clamp(OpacityPercent, 20, 100);
            BgAlpha = Clamp(BgAlpha, 0, 255);
            PetSize = Clamp(PetSize, 50, 300);
            if (string.IsNullOrEmpty(Theme)) Theme = Themes.DefaultId;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# NetSeep 配置文件（删除本文件即可恢复默认设置）");
                sb.AppendLine("nic=" + (NicId == Guid.Empty ? "" : NicId.ToString()));
                sb.AppendLine("showup=" + (ShowUp ? "1" : "0"));
                sb.AppendLine("showdown=" + (ShowDown ? "1" : "0"));
                sb.AppendLine("showgraph=" + (ShowGraph ? "1" : "0"));
                sb.AppendLine("intervalms=" + IntervalMs.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("opacity=" + OpacityPercent.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("theme=" + Theme);
                sb.AppendLine("bgalpha=" + BgAlpha.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("topmost=" + (TopMost ? "1" : "0"));
                sb.AppendLine("locked=" + (Locked ? "1" : "0"));
                sb.AppendLine("clickthrough=" + (ClickThrough ? "1" : "0"));
                sb.AppendLine("autohidefullscreen=" + (AutoHideFullscreen ? "1" : "0"));
                if (X.HasValue) sb.AppendLine("x=" + X.Value.ToString(CultureInfo.InvariantCulture));
                if (Y.HasValue) sb.AppendLine("y=" + Y.Value.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pet=" + (Pet ? "1" : "0"));
                sb.AppendLine("petsize=" + PetSize.ToString(CultureInfo.InvariantCulture));
                if (PetX.HasValue) sb.AppendLine("petx=" + PetX.Value.ToString(CultureInfo.InvariantCulture));
                if (PetY.HasValue) sb.AppendLine("pety=" + PetY.Value.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("upcolor=" + UpColor);
                sb.AppendLine("downcolor=" + DownColor);
                sb.AppendLine("bgcolor=" + BgColor);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // 无写入权限时静默忽略，功能仍可用
            }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------
        //  颜色
        // ------------------------------------------------------------------
        public static Color ParseColor(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            string[] parts = s.Split(',');
            if (parts.Length < 3) return fallback;
            int r, g, b;
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return fallback;
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out g)) return fallback;
            if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) return fallback;
            return Color.FromArgb(Clamp(r, 0, 255), Clamp(g, 0, 255), Clamp(b, 0, 255));
        }

        // ------------------------------------------------------------------
        //  开机自启（HKCU\...\Run，无需管理员权限）
        //  这是全程序唯一写注册表的地方，只在用户主动勾选时才会执行；
        //  “精简构建”（build.ps1 -Minimal）会把整段注册表代码编译掉。
        // ------------------------------------------------------------------
#if MINIMAL_BUILD
        public static bool AutoStartEnabled { get { return false; } }
        public static bool AutoStartIsCurrent { get { return false; } }
        public static bool SetAutoStart(bool on) { return false; }
#else
        public static bool AutoStartEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", false))
                    {
                        if (k == null) return false;
                        object v = k.GetValue(RunKeyValueName);
                        return v != null && v.ToString().Length > 0;
                    }
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>已登记的自启项是否指向当前这份程序（换目录后需要重新登记）。</summary>
        public static bool AutoStartIsCurrent
        {
            get
            {
                try
                {
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", false))
                    {
                        if (k == null) return false;
                        object v = k.GetValue(RunKeyValueName);
                        if (v == null) return false;
                        string s = v.ToString().Trim().Trim('"');
                        return string.Equals(s, ExecutablePath, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception) { return false; }
            }
        }

        public static bool SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return false;
                    if (on)
                        k.SetValue(RunKeyValueName, "\"" + ExecutablePath + "\" --silent");
                    else
                        k.DeleteValue(RunKeyValueName, false);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }
#endif

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    string p = Assembly.GetEntryAssembly().Location;
                    if (!string.IsNullOrEmpty(p)) return p;
                }
                catch (Exception) { }
                return System.Windows.Forms.Application.ExecutablePath;
            }
        }

        // ------------------------------------------------------------------
        //  小工具
        // ------------------------------------------------------------------
        private static int Clamp(int v, int lo, int hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        private static bool ParseBool(string v, bool fallback)
        {
            if (string.IsNullOrEmpty(v)) return fallback;
            v = v.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return fallback;
        }

        private static int ParseInt(string v, int fallback)
        {
            int r;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            return fallback;
        }

        private static Guid ParseGuid(string v)
        {
            Guid g;
            if (!string.IsNullOrEmpty(v) && Guid.TryParse(v, out g)) return g;
            return Guid.Empty;
        }
    }
}
