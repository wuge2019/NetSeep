// NetSeep - 入口 / 命令行工具（--dump 实测速率，--preview 生成预览图）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NetSeep
{
    /// <summary>
    /// 命令行输出。
    /// 只“附着”到调用方已有的控制台，不主动新建控制台窗口
    /// （新建控制台是很多恶意程序的典型动作，容易被安全软件的行为引擎盯上）；
    /// 没有父控制台时，把结果写到 %APPDATA%\NetSeep\last-output.txt。
    /// </summary>
    internal static class Cli
    {
        private static StringWriter _buffer;
        private static bool _hasConsole = false;

        public static string OutputPath
        {
            get { return Path.Combine(AppConfig.DataDir, "last-output.txt"); }
        }

        public static void Open()
        {
            _buffer = new StringWriter();
#if !MINIMAL_BUILD
            try { _hasConsole = Native.AttachConsole(Native.ATTACH_PARENT_PROCESS); }
            catch (Exception) { _hasConsole = false; }
            if (_hasConsole)
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
            }
#endif
        }

        public static void WriteLine(string text)
        {
            if (_buffer != null) _buffer.WriteLine(text);
            if (_hasConsole)
            {
                try { Console.WriteLine(text); } catch (Exception) { }
            }
        }

        public static void WriteLine()
        {
            WriteLine("");
        }

        /// <summary>收尾：写文件；没有控制台时用气泡/对话框告知结果位置。</summary>
        public static void Close(bool notifyWhenNoConsole)
        {
            string text = _buffer == null ? "" : _buffer.ToString();
            if (text.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(AppConfig.DataDir);
                    File.WriteAllText(OutputPath, text, new UTF8Encoding(false));
                }
                catch (Exception) { }
            }
            if (!_hasConsole && notifyWhenNoConsole && text.Length > 0)
            {
                try
                {
                    MessageBox.Show("本程序是窗口程序，没有可用的控制台。\n\n结果已保存到：\n" + OutputPath,
                        "NetSeep", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception) { }
            }
            _buffer = null;
        }
    }

    internal static class Program
    {
        public const string Version = "1.2.0";
        private const string MutexName = @"Local\NetSeep.SingleInstance.v1";

        [STAThread]
        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                LogError(e.ExceptionObject as Exception);
            };

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            switch (mode)
            {
                case "--dump":
                    return DumpMode(args);
                case "--list":
                    return ListMode(args);
                case "--preview":
                    return PreviewMode(args);
                case "--themes":
                    return ThemesMode(args);
                case "--pet":
                    return PetMode(args);
                case "--reset":
                    Cli.Open();
                    AppConfig.Delete();
                    Cli.WriteLine("已删除配置：" + AppConfig.FilePath);
                    Cli.Close(true);
                    return 0;
                case "--version":
                case "-v":
                    Cli.Open();
                    Cli.WriteLine("NetSeep " + Version);
                    Cli.Close(true);
                    return 0;
                case "--help":
                case "-h":
                case "/?":
                    return HelpMode();
            }

            bool silent = HasFlag(args, "--silent");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已有实例在运行，直接退出（避免重复托盘图标）
                    return 0;
                }

                AppConfig cfg = AppConfig.Load();
                using (WidgetForm form = new WidgetForm(cfg, silent))
                {
                    Application.Run(form);
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ==================================================================
        //  控制台工具
        // ==================================================================
        private static int DumpMode(string[] args)
        {
            Cli.Open();
            double seconds = 10;
            if (args.Length > 1)
                double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
            if (seconds <= 0) seconds = 10;

            Cli.WriteLine("NetSeep " + Version + " - 流量采样测试（" + seconds.ToString("0", CultureInfo.InvariantCulture) + " 秒）");
            Cli.WriteLine("网卡列表：");
            List<NicEntry> nics = NicTable.List();
            Cli.WriteLine("  行布局：" + NicTable.LayoutDescription);
            foreach (NicEntry n in nics)
            {
                Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1}  ({2})", n.IsUp ? "已连接" : "未连接", n.Display, n.ToolTip));
            }
            Cli.WriteLine();
            Cli.WriteLine("  时间      下载速率        上传速率");
            Cli.WriteLine("  --------  --------------  --------------");

            TrafficSampler sampler = new TrafficSampler();
            sampler.Prime();
            Thread.Sleep(1000);

            double peakDown = 0, peakUp = 0;
            int ticks = (int)Math.Ceiling(seconds);
            for (int i = 0; i < ticks; i++)
            {
                TrafficSample s = sampler.Sample(Guid.Empty);
                if (s.DownBps > peakDown) peakDown = s.DownBps;
                if (s.UpBps > peakUp) peakUp = s.UpBps;

                string dv, du, uv, uu;
                SpeedFormat.Split(s.DownBps, out dv, out du);
                SpeedFormat.Split(s.UpBps, out uv, out uu);
                Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,8}  {1,14}  {2,14}", (i + 1).ToString() + "s", dv + du, uv + uu));
                Thread.Sleep(1000);
            }

            string pd, pdu, pu, puu;
            SpeedFormat.Split(peakDown, out pd, out pdu);
            SpeedFormat.Split(peakUp, out pu, out puu);
            Cli.WriteLine();
            Cli.WriteLine("峰值：下载 " + pd + pdu + "   上传 " + pu + puu);
            Cli.Close(true);
            return 0;
        }

        private static int ListMode(string[] args)
        {
            Cli.Open();
            foreach (NicEntry n in NicTable.List())
            {
                Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}\t{1}\t{2}\t{3}\t{4}",
                    n.Id, n.IsUp ? "up" : "down", n.Display, n.ToolTip, n.Type));
            }
            Cli.Close(true);
            return 0;
        }

        private static int HelpMode()
        {
            Cli.Open();
            Cli.WriteLine("NetSeep " + Version + " - 轻量级网速悬浮窗");
            Cli.WriteLine();
            Cli.WriteLine("  NetSeep.exe                启动悬浮窗");
            Cli.WriteLine("  NetSeep.exe --silent       启动但不显示首次运行提示");
            Cli.WriteLine("  NetSeep.exe --dump [秒]    实测网速（默认 10 秒）");
            Cli.WriteLine("  NetSeep.exe --list         列出所有网卡");
            Cli.WriteLine("  NetSeep.exe --preview 路径 [缩放] [主题]   生成界面预览图");
            Cli.WriteLine("  NetSeep.exe --themes 目录 [缩放]          每个内置主题各出一张预览图");
            Cli.WriteLine("  NetSeep.exe --pet 路径 [缩放]             宠物 6 种状态的对照图");
            Cli.WriteLine("  NetSeep.exe --reset        删除配置文件");
            Cli.WriteLine("  NetSeep.exe --help         显示本帮助");
            Cli.WriteLine();
            Cli.WriteLine("  主题：" + ThemesMenuText());
            Cli.Close(true);
            return 0;
        }

        private static string ThemesMenuText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (WidgetTheme t in Themes.Presets)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(t.Id);
            }
            return sb.ToString();
        }

        /// <summary>造一个用于预览的示例画面（含一条像样的历史曲线）。</summary>
        private static WidgetVisual BuildPreviewVisual(string themeId)
        {
            WidgetVisual v = new WidgetVisual();
            v.UpValue = "256.4";
            v.UpUnit = "KB/s";
            v.DownValue = "3.42";
            v.DownUnit = "MB/s";
            v.ShowUp = true;
            v.ShowDown = true;
            v.ShowGraph = true;
            v.Opacity = 1f;

            double[] up = new double[60];
            double[] down = new double[60];
            for (int i = 0; i < 60; i++)
            {
                double t = i / 59.0;
                down[i] = (0.35 + 0.65 * Math.Abs(Math.Sin(t * 5.2))) * 3.4 * 1024 * 1024;
                up[i] = (0.25 + 0.5 * Math.Abs(Math.Cos(t * 3.1))) * 0.26 * 1024 * 1024;
            }
            down[55] = 3.42 * 1024 * 1024;
            up[59] = 256.4 * 1024;
            v.UpHistory = up;
            v.DownHistory = down;

            v.ApplyTheme(Themes.Resolve(
                string.IsNullOrEmpty(themeId) ? Themes.DefaultId : themeId,
                Color.FromArgb(24, 24, 27), 190,
                Color.FromArgb(90, 214, 140), Color.FromArgb(86, 170, 245)));
            return v;
        }

        private static int PreviewMode(string[] args)
        {
            string path = args.Length > 1 ? args[1] : "netseep-preview.png";
            float scale = 1f;
            if (args.Length > 2)
                float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
            if (scale < 1f) scale = 1f;
            string themeId = args.Length > 3 ? args[3] : "";

            WidgetVisual v = BuildPreviewVisual(themeId);
            Size size;

            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics pg = Graphics.FromImage(probe))
            using (WidgetMetrics m = WidgetRenderer.Measure(v, scale, pg))
            {
                size = m.Size;

                using (Bitmap bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        WidgetRenderer.Draw(g, v, m);
                    }

                    string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    bmp.Save(path, ImageFormat.Png);
                    SaveComposite(bmp, Color.FromArgb(255, 32, 34, 38), Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-dark.png"), false);
                    SaveComposite(bmp, Color.FromArgb(255, 240, 240, 242), Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-light.png"), false);
                    SaveComposite(bmp, Color.Empty, Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-checker.png"), true);
                }
            }

            Cli.Open();
            Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "已生成预览图：{0}（{1}x{2} @ {3:0.##}x）", Path.GetFullPath(path), size.Width, size.Height, scale));
            Cli.Close(false);
            return 0;
        }

        /// <summary>把浮窗贴到背景上另存一张；checker=true 时用透明棋盘格背景。</summary>
        private static void SaveComposite(Bitmap widget, Color background, string path, bool checker)
        {
            int pad = 24;
            using (Bitmap canvas = new Bitmap(widget.Width + pad * 2, widget.Height + pad * 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    if (checker)
                    {
                        const int cell = 8;
                        for (int y = 0; y < canvas.Height; y += cell)
                        {
                            for (int x = 0; x < canvas.Width; x += cell)
                            {
                                bool odd = ((x / cell) + (y / cell)) % 2 == 1;
                                using (SolidBrush b = new SolidBrush(odd ? Color.FromArgb(255, 205, 205, 205) : Color.FromArgb(255, 240, 240, 240)))
                                    g.FillRectangle(b, x, y, cell, cell);
                            }
                        }
                    }
                    else
                    {
                        g.Clear(background);
                    }
                    g.DrawImageUnscaled(widget, pad, pad);
                }
                canvas.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>每个内置主题各出一张棋盘格背景的预览图，便于做主题画廊。</summary>
        private static int ThemesMode(string[] args)
        {
            string dir = args.Length > 1 ? args[1] : "themes";
            float scale = 2f;
            if (args.Length > 2)
                float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
            if (scale < 1f) scale = 1f;

            Directory.CreateDirectory(dir);
            int count = 0;
            foreach (WidgetTheme t in Themes.Presets)
            {
                if (t.IsCustom) continue;   // 自定义不在画廊里

                WidgetVisual v = BuildPreviewVisual(t.Id);
                using (Bitmap probe = new Bitmap(1, 1))
                using (Graphics pg = Graphics.FromImage(probe))
                using (WidgetMetrics m = WidgetRenderer.Measure(v, scale, pg))
                {
                    using (Bitmap bmp = new Bitmap(m.Size.Width, m.Size.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.Transparent);
                            WidgetRenderer.Draw(g, v, m);
                        }
                        SaveComposite(bmp, Color.Empty, Path.Combine(dir, t.Id + ".png"), true);
                        count++;
                    }
                }
            }

            Cli.Open();
            Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "已生成 {0} 张主题预览图：{1}", count, Path.GetFullPath(dir)));
            Cli.Close(false);
            return 0;
        }

        /// <summary>把宠物的 6 种状态画成一排，用于 README 插图 / 直观检查每个状态的样子。</summary>
        private static int PetMode(string[] args)
        {
            string path = args.Length > 1 ? args[1] : "netseep-pet.png";
            float scale = 3f;   // 宠物本体很小，默认放大 3 倍才好观察
            if (args.Length > 2)
                float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
            if (scale < 1f) scale = 1f;

            PetMood[] moods = new PetMood[]
            {
                PetMood.Sleep, PetMood.Idle, PetMood.Walk, PetMood.Run, PetMood.Sprint, PetMood.Happy
            };
            string[] labels = new string[]
            {
                "睡觉 · 无流量", "发呆 · 低速", "踱步 · 中速", "小跑 · 高速", "冲刺 · 极速", "被摸 · 开心"
            };

            Size cell = PetRenderer.Measure(scale);
            const int pad = 16, gap = 10, labelH = 26;
            int cw = cell.Width + gap;
            int cwTotal = pad * 2 + moods.Length * cell.Width + (moods.Length - 1) * gap;
            int chTotal = pad * 2 + cell.Height + labelH;

            using (Bitmap canvas = new Bitmap(cwTotal, chTotal, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    // 棋盘格底，方便看清透明区域
                    const int cs = 8;
                    for (int y = 0; y < chTotal; y += cs)
                    {
                        for (int x = 0; x < cwTotal; x += cs)
                        {
                            bool odd = ((x / cs) + (y / cs)) % 2 == 1;
                            using (SolidBrush b = new SolidBrush(odd
                                ? Color.FromArgb(255, 205, 205, 205)
                                : Color.FromArgb(255, 240, 240, 240)))
                                g.FillRectangle(b, x, y, cs, cs);
                        }
                    }

                    using (Font font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
                    using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(255, 60, 62, 68)))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        for (int i = 0; i < moods.Length; i++)
                        {
                            PetFrame f = new PetFrame();
                            f.Mood = moods[i];
                            f.Scale = scale;
                            f.Phase = 0.35 + i * 0.21;   // 固定相位，出图稳定
                            f.Facing = 1;
                            f.EyeX = 0.4;
                            f.EyeY = 0.15;
                            f.Accent = Color.FromArgb(90, 214, 140);
                            if (moods[i] == PetMood.Happy)
                            {
                                HeartParticle[] hs = new HeartParticle[3];
                                for (int k = 0; k < 3; k++)
                                {
                                    hs[k] = new HeartParticle();
                                    hs[k].X = 27f + k * 5f;
                                    hs[k].Y = 14f - k * 3.5f;
                                    hs[k].Size = 7f;
                                    hs[k].Life = 0.75f;
                                    hs[k].Drift = 0f;
                                }
                                f.Hearts = hs;
                            }

                            using (Bitmap cellBmp = new Bitmap(cell.Width, cell.Height, PixelFormat.Format32bppArgb))
                            {
                                using (Graphics cg = Graphics.FromImage(cellBmp))
                                {
                                    cg.Clear(Color.Transparent);
                                    PetRenderer.Draw(cg, f);
                                }
                                int x = pad + i * cw;
                                canvas.SetResolution(g.DpiX, g.DpiY);
                                g.DrawImageUnscaled(cellBmp, x, pad);
                                g.DrawString(labels[i], font, textBrush,
                                    new RectangleF(x - gap / 2f, pad + cell.Height + 2, cell.Width + gap, labelH), sf);
                            }
                        }
                    }
                }

                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                canvas.Save(path, ImageFormat.Png);
            }

            Cli.Open();
            Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "已生成宠物状态图：{0}", Path.GetFullPath(path)));
            Cli.Close(false);
            return 0;
        }

        private static void LogError(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(AppConfig.DataDir);
                File.AppendAllText(AppConfig.LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + ex + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
