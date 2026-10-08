// NetSeep - 主题预设
// 一个主题 = 底板颜色/透明度 + 边框 + 上行色 + 下行色。
// “跟随系统”会读取 Windows 的浅色/深色应用模式，并在系统切换时自动生效。
using System;
using System.Drawing;
#if !MINIMAL_BUILD
using Microsoft.Win32;
#endif

namespace NetSeep
{
    internal sealed class WidgetTheme
    {
        public string Id = "dark";
        public string Name = "深色";
        public Color Bg = Color.FromArgb(24, 24, 27);
        public int BgAlpha = 190;
        public Color Border = Color.White;
        public int BorderAlpha = 46;
        public Color Up = Color.FromArgb(90, 214, 140);
        public Color Down = Color.FromArgb(86, 170, 245);

        /// <summary>是否为“跟随系统”的占位主题（真正颜色在解析时确定）。</summary>
        public bool IsSystem;
        /// <summary>是否为“沿用配置文件里的自定义颜色”。</summary>
        public bool IsCustom;
    }

    internal static class Themes
    {
        public const string DefaultId = "dark";
        public const string SystemId = "system";
        public const string CustomId = "custom";

        /// <summary>菜单里的顺序即此数组顺序。</summary>
        public static readonly WidgetTheme[] Presets = new WidgetTheme[]
        {
            new WidgetTheme
            {
                Id = "dark", Name = "深色（默认）",
                Bg = Color.FromArgb(24, 24, 27), BgAlpha = 190,
                Border = Color.White, BorderAlpha = 46,
                Up = Color.FromArgb(90, 214, 140), Down = Color.FromArgb(86, 170, 245)
            },
            new WidgetTheme
            {
                Id = "light", Name = "浅色",
                Bg = Color.FromArgb(247, 248, 250), BgAlpha = 238,
                Border = Color.FromArgb(0, 0, 0), BorderAlpha = 38,
                Up = Color.FromArgb(16, 148, 88), Down = Color.FromArgb(20, 108, 208)
            },
            new WidgetTheme
            {
                Id = "midnight", Name = "午夜蓝",
                Bg = Color.FromArgb(12, 20, 38), BgAlpha = 205,
                Border = Color.FromArgb(120, 170, 255), BorderAlpha = 44,
                Up = Color.FromArgb(74, 222, 128), Down = Color.FromArgb(56, 189, 248)
            },
            new WidgetTheme
            {
                Id = "graphite", Name = "石墨灰",
                Bg = Color.FromArgb(38, 40, 44), BgAlpha = 205,
                Border = Color.White, BorderAlpha = 34,
                Up = Color.FromArgb(154, 230, 180), Down = Color.FromArgb(144, 205, 244)
            },
            new WidgetTheme
            {
                Id = "contrast", Name = "高对比",
                Bg = Color.FromArgb(0, 0, 0), BgAlpha = 246,
                Border = Color.White, BorderAlpha = 72,
                Up = Color.FromArgb(0, 255, 140), Down = Color.FromArgb(64, 156, 255)
            },
            new WidgetTheme
            {
                Id = "glass", Name = "极简透明",
                Bg = Color.FromArgb(20, 20, 22), BgAlpha = 92,
                Border = Color.White, BorderAlpha = 34,
                Up = Color.FromArgb(120, 232, 162), Down = Color.FromArgb(110, 190, 255)
            },
            new WidgetTheme
            {
                Id = SystemId, Name = "跟随系统", IsSystem = true
            },
            new WidgetTheme
            {
                Id = CustomId, Name = "自定义（改配置文件）", IsCustom = true
            }
        };

        public static WidgetTheme Find(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (WidgetTheme t in Presets)
                {
                    if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            return Presets[0];
        }

        public static bool IsSystem(string id)
        {
            return string.Equals(id, SystemId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Windows 是否处于浅色应用模式（读不到时按深色处理）。</summary>
        public static bool SystemPrefersLight()
        {
#if MINIMAL_BUILD
            return false;
#else
            try
            {
                object v = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", null);
                if (v is int) return (int)v != 0;
            }
            catch (Exception) { }
            return false;
#endif
        }

        /// <summary>
        /// 把配置里的主题 id 解析成实际颜色。
        /// custom 时用配置文件里的 bgcolor/bgalpha/upcolor/downcolor，
        /// system 时按当前系统模式映射到 light / dark。
        /// </summary>
        public static WidgetTheme Resolve(string id, Color customBg, int customBgAlpha, Color customUp, Color customDown)
        {
            WidgetTheme t = Find(id);

            if (t.IsSystem)
            {
                t = Find(SystemPrefersLight() ? "light" : "dark");
            }
            else if (t.IsCustom)
            {
                t = new WidgetTheme();
                t.Id = CustomId;
                t.Name = "自定义";
                t.Bg = customBg;
                t.BgAlpha = customBgAlpha;
                t.Border = Color.White;
                t.BorderAlpha = 40;
                t.Up = customUp;
                t.Down = customDown;
            }
            return t;
        }
    }
}
