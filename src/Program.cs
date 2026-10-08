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
        public const string Version = "1.0.0";
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
            Cli.WriteLine("  NetSeep.exe --preview 路径 [缩放]  生成界面预览图");
            Cli.WriteLine("  NetSeep.exe --reset        删除配置文件");
            Cli.WriteLine("  NetSeep.exe --help         显示本帮助");
            Cli.Close(true);
            return 0;
        }

        private static int PreviewMode(string[] args)
        {
            string path = args.Length > 1 ? args[1] : "netseep-preview.png";
            float scale = 1f;
            if (args.Length > 2)
                float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out scale);
            if (scale < 1f) scale = 1f;

            WidgetVisual v = new WidgetVisual();
            v.UpValue = "256.4";
            v.UpUnit = "KB/s";
            v.DownValue = "3.42";
            v.DownUnit = "MB/s";
            v.ShowUp = true;
            v.ShowDown = true;
            v.ShowGraph = true;
            v.Opacity = 1f;

            // 造一条像样的历史曲线
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
                    SaveComposite(bmp, Color.FromArgb(255, 32, 34, 38), Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-dark.png"));
                    SaveComposite(bmp, Color.FromArgb(255, 240, 240, 242), Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + "-light.png"));
                }
            }

            Cli.Open();
            Cli.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "已生成预览图：{0}（{1}x{2} @ {3:0.##}x）", Path.GetFullPath(path), size.Width, size.Height, scale));
            Cli.Close(false);
            return 0;
        }

        private static void SaveComposite(Bitmap widget, Color background, string path)
        {
            int pad = 24;
            using (Bitmap canvas = new Bitmap(widget.Width + pad * 2, widget.Height + pad * 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(canvas))
                {
                    g.Clear(background);
                    g.DrawImageUnscaled(widget, pad, pad);
                }
                canvas.Save(path, ImageFormat.Png);
            }
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
