// NetSeep - 桌面宠物
//   一只自己画出来的小家伙，会根据当前网速切换状态：
//     0 流量 → 睡觉(zZz) ／ 低速 → 发呆眨眼 ／ 中速 → 踱步 ／ 高速 → 小跑 ／ 极速 → 冲刺
//   点一下 = 摸摸它（冒爱心、脸红、开心 2 秒）；拖拽 = 搬家；右键 = 和浮窗一样的菜单。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace NetSeep
{
    internal enum PetMood
    {
        Sleep = 0,
        Idle = 1,
        Walk = 2,
        Run = 3,
        Sprint = 4,
        Happy = 5
    }

    internal sealed class HeartParticle
    {
        public float X;
        public float Y;
        public float Size;
        public float Life;      // 1 → 0
        public float Drift;
    }

    /// <summary>一帧要画的内容。</summary>
    internal sealed class PetFrame
    {
        public PetMood Mood = PetMood.Idle;
        public double Phase;          // 动画相位（秒）
        public double Speed;          // 当前总速率 B/s
        public float Scale = 1f;      // DPI × 宠物大小
        public int Facing = 1;        // 1 朝右，-1 朝左
        public double EyeX, EyeY;     // 眼睛朝向 -1..1
        public double Blink;          // 0 睁眼 → 1 闭眼
        public HeartParticle[] Hearts = new HeartParticle[0];
        public Color Accent = Color.FromArgb(90, 214, 140);   // 主题色（天线光球）

        public Size Size
        {
            get { return PetRenderer.Measure(Scale); }
        }
    }

    internal static class PetRenderer
    {
        // 本体按 50 单位宽绘制；整张画布更宽，左右各留出拖尾空间给“跑/冲刺”的速度线
        public const float BodyW = 50f;
        public const float BaseW = 62f;
        public const float BaseH = 56f;

        private static Font _zFont;
        private static float _zFontScale;

        public static Size Measure(float scale)
        {
            if (scale < 0.5f) scale = 0.5f;
            return new Size((int)Math.Ceiling(BaseW * scale), (int)Math.Ceiling(BaseH * scale));
        }

        public static void Draw(Graphics g, PetFrame f)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float u = f.Scale;
            float w = BaseW * u;
            float h = BaseH * u;
            double t = f.Phase;

            // ---- 速度线（跑 / 冲刺时拖在身后）----
            DrawSpeedLines(g, f, u, w, h);

            GraphicsState st = g.Save();
            // 把 50 单位宽的本体摆到更宽画布的中间
            g.TranslateTransform((BaseW - BodyW) / 2f * u, 0);
            if (f.Facing < 0)
            {
                g.TranslateTransform(BodyW * u, 0);
                g.ScaleTransform(-1f, 1f);
            }

            float breathe = MoodBob(f, t, u);
            DrawCreature(g, f, u, breathe);
            g.Restore(st);

            // ---- 头顶特效 ----
            if (f.Mood == PetMood.Sleep) DrawZzz(g, u, w, t);
            DrawHearts(g, f, u);
        }

        /// <summary>呼吸/走路的上下浮动量。</summary>
        private static float MoodBob(PetFrame f, double t, float u)
        {
            switch (f.Mood)
            {
                case PetMood.Sleep: return (float)(Math.Sin(t * 1.6) * 0.5) * u;
                case PetMood.Idle: return (float)(Math.Sin(t * 2.2) * 0.8) * u;
                case PetMood.Walk: return (float)(Math.Abs(Math.Sin(t * 6.0)) * 1.4) * u;
                case PetMood.Run: return (float)(Math.Abs(Math.Sin(t * 11.0)) * 2.2) * u;
                case PetMood.Sprint: return (float)(Math.Abs(Math.Sin(t * 15.0)) * 2.6) * u;
                case PetMood.Happy: return (float)(Math.Abs(Math.Sin(t * 7.0)) * 2.0) * u;
            }
            return 0f;
        }

        private static void DrawCreature(Graphics g, PetFrame f, float u, float bob)
        {
            float cx = 25f * u;
            float groundY = 52f * u + bob * 0.25f;
            float bodyTop = 15f * u + bob;
            float bodyBottom = groundY - 2f * u;
            float bodyLeft = 8f * u;
            float bodyRight = 42f * u;

            // ---- 影子 ----
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(46, 0, 0, 0)))
                g.FillEllipse(sh, cx - 14f * u, groundY - 2f * u, 28f * u, 6f * u);

            // ---- 尾巴 ----
            double wag = Math.Sin(f.Phase * (f.Mood == PetMood.Sleep ? 1.0 : 4.0)) * 5.0;
            using (Pen tail = new Pen(BodyColor(f, 0), 3.2f * u))
            {
                tail.StartCap = LineCap.Round;
                tail.EndCap = LineCap.Round;
                g.DrawBezier(tail,
                    bodyRight - 3f * u, bodyBottom - 12f * u,
                    bodyRight + 6f * u, bodyBottom - 14f * u,
                    bodyRight + 4f * u + (float)wag * u, bodyBottom - 24f * u,
                    bodyRight + 8f * u + (float)wag * u, bodyBottom - 30f * u);
            }

            // ---- 脚 ----
            float step = 0f;
            if (f.Mood == PetMood.Walk) step = (float)Math.Sin(f.Phase * 6.0) * 1.6f * u;
            else if (f.Mood == PetMood.Run) step = (float)Math.Sin(f.Phase * 11.0) * 2.6f * u;
            else if (f.Mood == PetMood.Sprint) step = (float)Math.Sin(f.Phase * 15.0) * 3.0f * u;
            else if (f.Mood == PetMood.Happy) step = (float)Math.Sin(f.Phase * 7.0) * 2.0f * u;
            using (SolidBrush foot = new SolidBrush(BodyColor(f, -18)))
            {
                g.FillEllipse(foot, cx - 11f * u + step, groundY - 5f * u, 10f * u, 5f * u);
                g.FillEllipse(foot, cx + 1f * u - step, groundY - 5f * u, 10f * u, 5f * u);
            }

            // ---- 耳朵 ----
            using (SolidBrush ear = new SolidBrush(BodyColor(f, -10)))
            {
                g.FillPolygon(ear, new PointF[]
                {
                    new PointF(12f * u, bodyTop + 8f * u),
                    new PointF(15f * u, bodyTop - 8f * u),
                    new PointF(23f * u, bodyTop + 4f * u)
                });
                g.FillPolygon(ear, new PointF[]
                {
                    new PointF(38f * u, bodyTop + 8f * u),
                    new PointF(35f * u, bodyTop - 8f * u),
                    new PointF(27f * u, bodyTop + 4f * u)
                });
            }
            using (SolidBrush inner = new SolidBrush(Color.FromArgb(210, 255, 170, 190)))
            {
                g.FillPolygon(inner, new PointF[]
                {
                    new PointF(14.5f * u, bodyTop + 5f * u),
                    new PointF(16f * u, bodyTop - 4f * u),
                    new PointF(20.5f * u, bodyTop + 3f * u)
                });
                g.FillPolygon(inner, new PointF[]
                {
                    new PointF(35.5f * u, bodyTop + 5f * u),
                    new PointF(34f * u, bodyTop - 4f * u),
                    new PointF(29.5f * u, bodyTop + 3f * u)
                });
            }

            // ---- 天线 + 速度指示光球 ----
            using (Pen ant = new Pen(Color.FromArgb(200, 120, 130, 140), 1.6f * u))
                g.DrawLine(ant, cx, bodyTop + 2f * u, cx, bodyTop - 9f * u);
            DrawAntennaBall(g, f, cx, bodyTop - 11f * u, u);

            // ---- 身体 ----
            RectangleF body = new RectangleF(bodyLeft, bodyTop, bodyRight - bodyLeft, bodyBottom - bodyTop);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(body);
                using (PathGradientBrush pg = new PathGradientBrush(path))
                {
                    pg.CenterPoint = new PointF(cx - 4f * u, bodyTop + 8f * u);
                    pg.CenterColor = BodyColor(f, 26);
                    pg.SurroundColors = new Color[] { BodyColor(f, -14) };
                    g.FillPath(pg, path);
                }
            }

            // ---- 肚皮 ----
            using (SolidBrush belly = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                g.FillEllipse(belly, cx - 10f * u, bodyTop + 16f * u, 20f * u, 16f * u);

            // ---- 脸 ----
            DrawFace(g, f, u, cx, bodyTop);
        }

        private static void DrawFace(Graphics g, PetFrame f, float u, float cx, float bodyTop)
        {
            float eyeY = bodyTop + 15f * u;
            float eyeDx = 7f * u;
            float eyeR = 4.6f * u;

            if (f.Mood == PetMood.Happy)
            {
                // ^ ^ 开心眼
                using (Pen p = new Pen(Color.FromArgb(255, 45, 55, 62), 1.8f * u))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, cx - eyeDx - 3.4f * u, eyeY - 1f * u, 6.8f * u, 6f * u, 200f, 140f);
                    g.DrawArc(p, cx + eyeDx - 3.4f * u, eyeY - 1f * u, 6.8f * u, 6f * u, 200f, 140f);
                }
            }
            else if (f.Mood == PetMood.Sleep || f.Blink > 0.6)
            {
                // 闭眼：一条弧线
                using (Pen p = new Pen(Color.FromArgb(255, 45, 55, 62), 1.8f * u))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, cx - eyeDx - 3.2f * u, eyeY - 3f * u, 6.4f * u, 5f * u, 20f, 140f);
                    g.DrawArc(p, cx + eyeDx - 3.2f * u, eyeY - 3f * u, 6.4f * u, 5f * u, 20f, 140f);
                }
            }
            else if (f.Mood == PetMood.Sprint)
            {
                // 冲刺：星星眼
                DrawStar(g, cx - eyeDx, eyeY, 4.6f * u, Color.FromArgb(255, 255, 210, 60));
                DrawStar(g, cx + eyeDx, eyeY, 4.6f * u, Color.FromArgb(255, 255, 210, 60));
            }
            else
            {
                // 正常眼：白眼球 + 会跟着鼠标转的瞳孔
                using (SolidBrush white = new SolidBrush(Color.FromArgb(255, 252, 253, 255)))
                {
                    g.FillEllipse(white, cx - eyeDx - eyeR, eyeY - eyeR, eyeR * 2f, eyeR * 2f);
                    g.FillEllipse(white, cx + eyeDx - eyeR, eyeY - eyeR, eyeR * 2f, eyeR * 2f);
                }
                float pr = 2.3f * u;
                float px = (float)f.EyeX * 1.5f * u;
                float py = (float)f.EyeY * 1.2f * u;
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, 38, 48, 56)))
                {
                    g.FillEllipse(dark, cx - eyeDx - pr + px, eyeY - pr + py, pr * 2f, pr * 2f);
                    g.FillEllipse(dark, cx + eyeDx - pr + px, eyeY - pr + py, pr * 2f, pr * 2f);
                }
                using (SolidBrush glint = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                {
                    g.FillEllipse(glint, cx - eyeDx - 1.4f * u + px, eyeY - 1.8f * u + py, 1.2f * u, 1.2f * u);
                    g.FillEllipse(glint, cx + eyeDx - 1.4f * u + px, eyeY - 1.8f * u + py, 1.2f * u, 1.2f * u);
                }
            }

            // ---- 嘴 ----
            float my = bodyTop + 23f * u;
            using (Pen p = new Pen(Color.FromArgb(255, 45, 55, 62), 1.6f * u))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                if (f.Mood == PetMood.Sleep)
                {
                    g.DrawArc(p, cx - 2.5f * u, my - 1f * u, 5f * u, 4f * u, 200f, 140f);
                }
                else if (f.Mood == PetMood.Run || f.Mood == PetMood.Sprint || f.Mood == PetMood.Happy)
                {
                    // 张嘴笑
                    using (SolidBrush mouth = new SolidBrush(Color.FromArgb(255, 60, 68, 78)))
                        g.FillEllipse(mouth, cx - 4f * u, my - 2f * u, 8f * u, 6f * u);
                    using (SolidBrush tongue = new SolidBrush(Color.FromArgb(230, 255, 140, 160)))
                        g.FillEllipse(tongue, cx - 2f * u, my + 1.4f * u, 4f * u, 2.6f * u);
                }
                else
                {
                    g.DrawArc(p, cx - 3.2f * u, my - 3f * u, 6.4f * u, 5f * u, 20f, 140f);
                }
            }

            // ---- 脸红（开心 / 冲刺）----
            if (f.Mood == PetMood.Happy || f.Mood == PetMood.Sprint)
            {
                using (SolidBrush blush = new SolidBrush(Color.FromArgb(110, 255, 130, 150)))
                {
                    g.FillEllipse(blush, cx - 14f * u, my - 2.5f * u, 6.4f * u, 3.6f * u);
                    g.FillEllipse(blush, cx + 7.6f * u, my - 2.5f * u, 6.4f * u, 3.6f * u);
                }
            }
        }

        private static void DrawStar(Graphics g, float cx, float cy, float r, Color color)
        {
            PointF[] pts = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 4;
                float rr = (i % 2 == 0) ? r : r * 0.42f;
                pts[i] = new PointF(cx + (float)Math.Cos(a) * rr, cy + (float)Math.Sin(a) * rr);
            }
            using (SolidBrush b = new SolidBrush(color))
                g.FillPolygon(b, pts);
        }

        private static void DrawAntennaBall(Graphics g, PetFrame f, float cx, float cy, float u)
        {
            Color c;
            int glow;
            switch (f.Mood)
            {
                case PetMood.Sleep:
                    c = Color.FromArgb(255, 130, 140, 150); glow = 0; break;
                case PetMood.Idle:
                case PetMood.Walk:
                    c = f.Accent; glow = 40; break;
                case PetMood.Run:
                    c = Color.FromArgb(255, 255, 196, 64); glow = 90; break;
                case PetMood.Sprint:
                    c = Color.FromArgb(255, 255, 96, 72); glow = 140; break;
                default:
                    c = Color.FromArgb(255, 255, 120, 170); glow = 110; break;
            }

            if (glow > 0)
            {
                float pulse = (float)(0.5 + 0.5 * Math.Sin(f.Phase * (f.Mood == PetMood.Sprint ? 12.0 : 4.0)));
                for (int i = 2; i >= 1; i--)
                {
                    float rr = (2.6f + i * 2.2f + pulse * 1.4f) * u;
                    int a = (int)(glow * (1.0f - (i - 1) * 0.45f));
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(a, c.R, c.G, c.B)))
                        g.FillEllipse(gb, cx - rr, cy - rr, rr * 2f, rr * 2f);
                }
            }
            using (SolidBrush b = new SolidBrush(c))
                g.FillEllipse(b, cx - 2.6f * u, cy - 2.6f * u, 5.2f * u, 5.2f * u);
        }

        private static void DrawSpeedLines(Graphics g, PetFrame f, float u, float w, float h)
        {
            int lines;
            float len, alpha;
            switch (f.Mood)
            {
                case PetMood.Run: lines = 3; len = 9f; alpha = 110; break;
                case PetMood.Sprint: lines = 5; len = 14f; alpha = 165; break;
                default: return;
            }
            // 拖尾画在本体左侧（朝右）或右侧（朝左）的留白里
            float baseX = (f.Facing > 0) ? 1.5f * u : w - 1.5f * u;
            float trail = (BaseW - BodyW) / 2f * u;   // 可用留白宽度
            for (int i = 0; i < lines; i++)
            {
                float y = (28f + i * 4.6f) * u;
                float phase = (float)((f.Phase * 14.0 + i * 1.7) % 1.0);
                float ext = (trail - 1.5f * u) * (0.35f + phase * 0.65f) * (len / 14f + 0.35f);
                if (ext > trail - 1.5f * u) ext = trail - 1.5f * u;
                int a = (int)(alpha * (1.0f - phase * 0.7f));
                // 中性灰蓝：无论桌面是浅色还是深色都看得见（纯白在浅色壁纸上会消失）
                using (Pen p = new Pen(Color.FromArgb(a, 118, 128, 142), 1.3f * u))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    if (f.Facing > 0) g.DrawLine(p, baseX, y, baseX + ext, y);
                    else g.DrawLine(p, baseX, y, baseX - ext, y);
                }
            }
        }

        private static void DrawZzz(Graphics g, float u, float w, double t)
        {
            if (_zFont == null || Math.Abs(_zFontScale - u) > 0.01f)
            {
                if (_zFont != null) _zFont.Dispose();
                _zFontScale = u;
                _zFont = new Font("Segoe UI", 7f * u, FontStyle.Bold, GraphicsUnit.Pixel);
            }
            for (int i = 0; i < 3; i++)
            {
                double k = (t * 0.45 + i * 0.33) % 1.0;      // 0..1 循环
                float size = (float)(5.0 + i * 2.2) * u;
                float x = (float)(40 + i * 4 + k * 6) * u;
                float y = (float)(12 - k * 12 - i * 3) * u;
                int a = (int)(200 * (1.0 - k));
                Font f2 = _zFont;
                if (f2 == null) continue;
                // 先描一圈浅色再压深色字，浅色/深色桌面上都看得清
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(a / 2, 255, 255, 255)))
                    g.DrawString("z", f2, halo, x + 1.2f * u, y - size * 0.5f + 1.2f * u);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 78, 90, 106)))
                    g.DrawString("z", f2, b, x, y - size * 0.5f);
            }
        }

        private static void DrawHearts(Graphics g, PetFrame f, float u)
        {
            if (f.Hearts == null) return;
            foreach (HeartParticle hp in f.Hearts)
            {
                if (hp == null || hp.Life <= 0f) continue;
                int a = (int)(220 * Math.Min(1f, hp.Life * 1.6f));
                float s = hp.Size * u;
                float x = hp.X * u;
                float y = hp.Y * u;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, 255, 96, 140)))
                {
                    g.FillEllipse(b, x - s * 0.5f, y - s * 0.3f, s * 0.55f, s * 0.55f);
                    g.FillEllipse(b, x + s * 0.0f, y - s * 0.3f, s * 0.55f, s * 0.55f);
                    g.FillPolygon(b, new PointF[]
                    {
                        new PointF(x - s * 0.52f, y - s * 0.06f),
                        new PointF(x + s * 0.57f, y - s * 0.06f),
                        new PointF(x + s * 0.02f, y + s * 0.55f)
                    });
                }
            }
        }

        /// <summary>身体主色（按状态微调明暗，睡着时偏灰）。</summary>
        private static Color BodyColor(PetFrame f, int delta)
        {
            Color b;
            switch (f.Mood)
            {
                case PetMood.Sleep: b = Color.FromArgb(255, 138, 158, 168); break;
                case PetMood.Sprint: b = Color.FromArgb(255, 92, 226, 214); break;
                default: b = Color.FromArgb(255, 108, 214, 196); break;
            }
            return Color.FromArgb(255,
                Clamp(b.R + delta), Clamp(b.G + delta), Clamp(b.B + delta));
        }

        private static int Clamp(int v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return v;
        }
    }

    /// <summary>宠物窗口：独立的分层浮窗，显示在桌面上。</summary>
    internal sealed class PetForm : Form
    {
        private const int TimerMs = 80;

        private readonly AppConfig _cfg;
        private readonly LayeredSurface _surface = new LayeredSurface();
        private readonly Timer _timer;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly List<HeartParticle> _hearts = new List<HeartParticle>();

        private double _upBps, _downBps;
        private PetMood _mood = PetMood.Idle;
        private float _scale = 1f;
        private int _facing = 1;
        private double _blink;              // 剩余眨眼时间
        private double _nextBlink;          // 距离下一次眨眼
        private double _happyUntil;         // 摸到后开心到什么时候
        private double _eyeX, _eyeY;
        private float _wander;              // 相对“家”的徘徊偏移（像素）
        private int _wanderDir = 1;
        private double _nextWanderFlip;
        private int _petCount;

        private bool _dragging;
        private Point _dragStart;
        private Point _winStart;
        private bool _moved;

        /// <summary>右键菜单回调（由主浮窗提供，共用同一份菜单）。</summary>
        public Action<Point> ContextMenuRequested;

        /// <summary>天线光球的颜色，跟随主题。</summary>
        public Color Accent = Color.FromArgb(90, 214, 140);

        public int PetCount { get { return _petCount; } }

        public PetForm(AppConfig cfg)
        {
            _cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            TopMost = cfg.TopMost;
            Text = "NetSeep 宠物";
            Size = new Size(50, 56);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            _clock.Start();
            _nextBlink = 1.5 + new Random().NextDouble() * 3.0;

            _timer = new Timer();
            _timer.Interval = TimerMs;
            _timer.Tick += OnTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void SetSpeed(double upBps, double downBps)
        {
            _upBps = upBps;
            _downBps = downBps;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _scale = Native.GetDpiScale(Handle);
            ApplyPosition();
            Render();
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            _surface.Dispose();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------
        //  位置
        // ------------------------------------------------------------------
        private void ApplyPosition()
        {
            Size size = CurrentSize();
            int w = size.Width, h = size.Height;
            if (_cfg.PetX.HasValue && _cfg.PetY.HasValue && IsOnScreen(_cfg.PetX.Value, _cfg.PetY.Value, w, h))
            {
                Location = new Point(_cfg.PetX.Value, _cfg.PetY.Value);
                return;
            }
            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            int margin = (int)Math.Round(24 * _scale);
            Location = new Point(wa.Right - w - margin, wa.Bottom - h - (int)Math.Round(8 * _scale));
        }

        public void MoveHome()
        {
            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            Size size = CurrentSize();
            _wander = 0f;
            Location = new Point(wa.Right - size.Width - (int)Math.Round(24 * _scale),
                                 wa.Bottom - size.Height - (int)Math.Round(8 * _scale));
            _cfg.PetX = Left;
            _cfg.PetY = Top;
            _cfg.Save();
        }

        private static bool IsOnScreen(int x, int y, int w, int h)
        {
            Rectangle target = new Rectangle(x, y, w, h);
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle r = Rectangle.Intersect(target, s.WorkingArea);
                if (r.Width > 8 && r.Height > 8) return true;
            }
            return false;
        }

        private Size CurrentSize()
        {
            return PetRenderer.Measure(_scale * (_cfg.PetSize / 100f));
        }

        // ------------------------------------------------------------------
        //  动画
        // ------------------------------------------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalSeconds;
            UpdateMood(now);
            UpdateEye();
            UpdateBlink(now);
            UpdateWander(now);
            UpdateHearts();
            Render();
        }

        private void UpdateMood(double now)
        {
            if (now < _happyUntil)
            {
                _mood = PetMood.Happy;
                return;
            }

            double total = _upBps + _downBps;
            if (total <= 1.0) _mood = PetMood.Sleep;
            else if (total < 16 * 1024) _mood = PetMood.Idle;
            else if (total < 160 * 1024) _mood = PetMood.Walk;
            else if (total < 1.5 * 1024 * 1024) _mood = PetMood.Run;
            else _mood = PetMood.Sprint;
        }

        private void UpdateEye()
        {
            Size size = CurrentSize();
            Point c = Cursor.Position;
            float cx = Left + size.Width / 2f;
            float cy = Top + size.Height * 0.62f;
            double dx = (c.X - cx) / (140.0 * _scale);
            double dy = (c.Y - cy) / (140.0 * _scale);
            _eyeX = Math.Max(-1.0, Math.Min(1.0, dx));
            _eyeY = Math.Max(-1.0, Math.Min(1.0, dy));
            if (_eyeX > 0.15) _facing = 1;
            else if (_eyeX < -0.15) _facing = -1;
        }

        private void UpdateBlink(double now)
        {
            if (_blink > 0)
            {
                _blink -= TimerMs / 1000.0;
                return;
            }
            _nextBlink -= TimerMs / 1000.0;
            if (_nextBlink <= 0)
            {
                _blink = 0.14;
                _nextBlink = 1.6 + new Random().NextDouble() * 3.4;
            }
        }

        private void UpdateWander(double now)
        {
            if (_dragging) return;

            // 只有发呆/踱步时才会自己溜达，跑起来和冲刺时回到窝里
            if (_mood != PetMood.Idle && _mood != PetMood.Walk)
            {
                _wander = 0f;
                return;
            }

            if (now > _nextWanderFlip)
            {
                _wanderDir = new Random().Next(0, 2) == 0 ? -1 : 1;
                _nextWanderFlip = now + 1.6 + new Random().NextDouble() * 2.4;
            }

            float target = _wanderDir * 34f * _scale;
            float speed = (_mood == PetMood.Walk ? 26f : 9f) * _scale;
            float dt = TimerMs / 1000f;
            if (Math.Abs(target - _wander) < 1.5f * _scale) _wanderDir = -_wanderDir;
            _wander += Math.Sign(target - _wander) * speed * dt;
            if (_wander > 40f * _scale) _wander = 40f * _scale;
            if (_wander < -40f * _scale) _wander = -40f * _scale;
            _facing = _wanderDir;
        }

        private void UpdateHearts()
        {
            for (int i = _hearts.Count - 1; i >= 0; i--)
            {
                HeartParticle hp = _hearts[i];
                hp.Life -= TimerMs / 1400f;
                hp.Y -= 0.9f;
                hp.X += hp.Drift;
                if (hp.Life <= 0f) _hearts.RemoveAt(i);
            }
        }

        private void Render()
        {
            if (!IsHandleCreated) return;

            PetFrame f = new PetFrame();
            f.Mood = _mood;
            f.Phase = _clock.Elapsed.TotalSeconds;
            f.Speed = _upBps + _downBps;
            f.Scale = _scale * (_cfg.PetSize / 100f);
            f.Facing = _facing;
            f.EyeX = _eyeX;
            f.EyeY = _eyeY;
            f.Blink = _blink > 0 ? 1.0 : 0.0;
            f.Hearts = _hearts.ToArray();
            f.Accent = Accent;

            Size want = f.Size;
            if (ClientSize != want)
            {
                ClientSize = want;
                _surface.EnsureSize(want);
            }
            _surface.Draw(delegate (Graphics g)
            {
                g.Clear(Color.Transparent);
                PetRenderer.Draw(g, f);
            });
            _surface.Flush(Handle, Left + (int)_wander, Top);
        }

        // ------------------------------------------------------------------
        //  鼠标
        // ------------------------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (ContextMenuRequested != null) ContextMenuRequested(Cursor.Position);
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (!_cfg.Locked)
                {
                    _dragging = true;
                    _dragStart = Cursor.Position;
                    _winStart = Location;
                    _moved = false;
                    Capture = true;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point p = Cursor.Position;
                if (Math.Abs(p.X - _dragStart.X) > 3 || Math.Abs(p.Y - _dragStart.Y) > 3) _moved = true;
                Location = new Point(_winStart.X + (p.X - _dragStart.X), _winStart.Y + (p.Y - _dragStart.Y));
                _wander = 0f;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                if (_moved)
                {
                    _cfg.PetX = Left;
                    _cfg.PetY = Top;
                    _cfg.Save();
                }
                else
                {
                    Pet();     // 没拖动 = 摸它
                }
            }
            base.OnMouseUp(e);
        }

        /// <summary>摸摸它：冒爱心 + 脸红 + 开心 2 秒。</summary>
        public void Pet()
        {
            _petCount++;
            _happyUntil = _clock.Elapsed.TotalSeconds + 2.0;
            _mood = PetMood.Happy;

            Random r = new Random();
            Size size = CurrentSize();
            float cx = size.Width / (2f * _scale * (_cfg.PetSize / 100f));
            for (int i = 0; i < 3; i++)
            {
                HeartParticle hp = new HeartParticle();
                hp.X = cx + (float)(r.NextDouble() * 16 - 8);
                hp.Y = 8f + (float)(r.NextDouble() * 6);
                hp.Size = 6f + (float)(r.NextDouble() * 3);
                hp.Life = 1f;
                hp.Drift = (float)(r.NextDouble() * 0.5 - 0.25);
                _hearts.Add(hp);
            }
            Render();
        }

        public void ApplyTopMost()
        {
            TopMost = _cfg.TopMost;
            Native.SetWindowPos(Handle,
                _cfg.TopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        /// <summary>全屏程序运行时让位：隐藏并停掉动画定时器（省电）。</summary>
        public void SetAutoHidden(bool hidden)
        {
            if (hidden == !Visible) return;
            Visible = !hidden;
            if (hidden)
            {
                _timer.Stop();
            }
            else
            {
                Render();
                ApplyTopMost();
                _timer.Start();
            }
        }

        public void RefreshScaleAndSize()
        {
            if (!IsHandleCreated) return;
            _scale = Native.GetDpiScale(Handle);
            Size want = CurrentSize();
            ClientSize = want;
            _surface.EnsureSize(want);
            ApplyPosition();
            Render();
        }
    }
}
