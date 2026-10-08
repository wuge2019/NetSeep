// NetSeep - 悬浮窗绘制（自绘 + 逐像素 alpha 分层窗口）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace NetSeep
{
    /// <summary>一帧要显示的内容与样式。</summary>
    internal sealed class WidgetVisual
    {
        public string UpValue = "0";
        public string UpUnit = "KB/s";
        public string DownValue = "0";
        public string DownUnit = "KB/s";

        public bool ShowUp = true;
        public bool ShowDown = true;
        public bool ShowGraph = false;

        public double[] UpHistory = new double[0];
        public double[] DownHistory = new double[0];

        /// <summary>整体不透明度 0.2 ~ 1.0。</summary>
        public float Opacity = 0.88f;

        public Color BgColor = Color.FromArgb(24, 24, 27);
        public Color BorderColor = Color.FromArgb(255, 255, 255);
        public Color UpColor = Color.FromArgb(90, 214, 140);
        public Color DownColor = Color.FromArgb(86, 170, 245);
    }

    /// <summary>一次布局计算出的全部像素尺寸。</summary>
    internal sealed class WidgetMetrics : IDisposable
    {
        public float Scale = 1f;
        public Font Font;
        public float PadX, PadY, RowH, RowGap, ArrowW, ArrowH, Gap, Radius, GraphH, GraphGap, ValueW;
        public int Rows;
        public Size Size;
        public bool ShowGraph;

        public void Dispose()
        {
            if (Font != null) { Font.Dispose(); Font = null; }
        }
    }

    internal static class WidgetRenderer
    {
        private const float BaseFontSize = 9f;

        /// <summary>用于固定数值列宽，避免数字位数变化时窗口宽度抖动。</summary>
        private static readonly string[] WidthTemplates =
        {
            "888.8KB/s", "888.8MB/s", "8.88GB/s", "888B/s"
        };

        public static WidgetMetrics Measure(WidgetVisual v, float scale, Graphics g)
        {
            WidgetMetrics m = new WidgetMetrics();
            if (scale < 1f) scale = 1f;
            m.Scale = scale;

            m.ShowGraph = v.ShowGraph;
            m.Rows = (v.ShowUp ? 1 : 0) + (v.ShowDown ? 1 : 0);
            if (m.Rows == 0) m.Rows = 1;

            m.PadX = 7f * scale;
            m.PadY = 4f * scale;
            m.RowGap = 1f * scale;
            m.ArrowW = 8f * scale;
            m.Gap = 4f * scale;
            m.Radius = 6f * scale;
            m.GraphGap = 3f * scale;
            m.GraphH = 20f * scale;

            m.Font = new Font("Segoe UI", BaseFontSize * scale, FontStyle.Regular, GraphicsUnit.Point);

            float rowH = m.Font.GetHeight(g) + 1f * scale;
            if (rowH < 14f * scale) rowH = 14f * scale;
            m.RowH = rowH;
            m.ArrowH = rowH * 0.62f;
            if (m.ArrowH > 10f * scale) m.ArrowH = 10f * scale;

            float valueW = 0f;
            foreach (string t in WidthTemplates)
            {
                SizeF sz = g.MeasureString(t, m.Font, PointF.Empty, StringFormat.GenericTypographic);
                if (sz.Width > valueW) valueW = sz.Width;
            }
            m.ValueW = (float)Math.Ceiling(valueW) + 1f * scale;

            float w = m.PadX * 2f + m.ArrowW + m.Gap + m.ValueW;
            float h = m.PadY * 2f + m.Rows * m.RowH + (m.Rows - 1) * m.RowGap;
            if (m.ShowGraph) h += m.GraphGap + m.GraphH;

            if (w < 60f * scale) w = 60f * scale;
            m.Size = new Size((int)Math.Ceiling(w), (int)Math.Ceiling(h));
            return m;
        }

        public static void Draw(Graphics g, WidgetVisual v, WidgetMetrics m)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float s = m.Scale;
            float alpha = v.Opacity;
            int bgAlpha = ScaleAlpha(190, alpha);
            int borderA = ScaleAlpha(46, alpha);

            RectangleF full = new RectangleF(0.5f * s, 0.5f * s, m.Size.Width - 1f * s, m.Size.Height - 1f * s);

            // 圆角背景
            using (GraphicsPath path = RoundedRect(full, m.Radius))
            {
                using (SolidBrush b = new SolidBrush(WithAlpha(v.BgColor, bgAlpha)))
                    g.FillPath(b, path);
                using (Pen p = new Pen(WithAlpha(v.BorderColor, borderA), Math.Max(1f, s)))
                    g.DrawPath(p, path);
            }

            // 两行速率
            int row = 0;
            if (v.ShowUp)
                DrawRow(g, m, row++, true, v.UpValue, v.UpUnit, v.UpColor, alpha);
            if (v.ShowDown)
                DrawRow(g, m, row++, false, v.DownValue, v.DownUnit, v.DownColor, alpha);

            // 迷你曲线
            if (v.ShowGraph)
            {
                RectangleF gr = new RectangleF(m.PadX, m.Size.Height - m.PadY - m.GraphH,
                    m.Size.Width - m.PadX * 2f, m.GraphH);
                DrawGraph(g, gr, v, m, alpha);
            }
        }

        private static void DrawRow(Graphics g, WidgetMetrics m, int index, bool up,
            string value, string unit, Color color, float opacity)
        {
            float s = m.Scale;
            float top = m.PadY + index * (m.RowH + m.RowGap);

            // 箭头
            float ax = m.PadX;
            float ay = top + (m.RowH - m.ArrowH) / 2f;
            DrawArrow(g, ax, ay, m.ArrowW, m.ArrowH, up, WithAlpha(color, ScaleAlpha(255, opacity)));

            // 数值（右对齐）＋ 单位
            StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
            sf.Alignment = StringAlignment.Far;
            sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags |= StringFormatFlags.NoWrap;

            float textLeft = m.PadX + m.ArrowW + m.Gap;
            float textW = m.ValueW;
            RectangleF unitRect = new RectangleF(textLeft, top, textW, m.RowH);
            float unitW = g.MeasureString(unit, m.Font, PointF.Empty, StringFormat.GenericTypographic).Width;

            using (SolidBrush b = new SolidBrush(WithAlpha(color, ScaleAlpha(190, opacity))))
                g.DrawString(unit, m.Font, b, unitRect, sf);

            RectangleF valueRect = new RectangleF(textLeft, top, textW - unitW, m.RowH);
            using (SolidBrush b = new SolidBrush(WithAlpha(color, ScaleAlpha(255, opacity))))
                g.DrawString(value, m.Font, b, valueRect, sf);

            sf.Dispose();
        }

        /// <summary>矢量箭头：三角箭头 + 短杆，任何 DPI 下都清晰。</summary>
        public static void DrawArrow(Graphics g, float x, float y, float w, float h, bool up, Color color)
        {
            float cx = x + w / 2f;
            float headH = h * 0.62f;
            float stemW = Math.Max(1f, w * 0.26f);

            PointF p1, p2, p3;
            RectangleF stem;
            if (up)
            {
                p1 = new PointF(cx, y);
                p2 = new PointF(x, y + headH);
                p3 = new PointF(x + w, y + headH);
                stem = new RectangleF(cx - stemW / 2f, y + headH * 0.75f, stemW, h - headH * 0.75f);
            }
            else
            {
                p1 = new PointF(cx, y + h);
                p2 = new PointF(x, y + h - headH);
                p3 = new PointF(x + w, y + h - headH);
                stem = new RectangleF(cx - stemW / 2f, y, stemW, h - headH * 0.75f);
            }

            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, new PointF[] { p1, p2, p3 });
                g.FillRectangle(b, stem);
            }
        }

        private static void DrawGraph(Graphics g, RectangleF r, WidgetVisual v, WidgetMetrics m, float opacity)
        {
            double[] up = v.UpHistory ?? new double[0];
            double[] down = v.DownHistory ?? new double[0];
            int n = Math.Max(up.Length, down.Length);
            if (n < 2 || r.Width < 4f) return;

            double peak = 1024.0;
            for (int i = 0; i < up.Length; i++) if (up[i] > peak) peak = up[i];
            for (int i = 0; i < down.Length; i++) if (down[i] > peak) peak = down[i];

            using (Pen basePen = new Pen(WithAlpha(Color.White, ScaleAlpha(28, opacity)), Math.Max(1f, m.Scale)))
                g.DrawLine(basePen, r.Left, r.Bottom, r.Right, r.Bottom);

            DrawSeries(g, r, up, peak, v.UpColor, opacity);
            DrawSeries(g, r, down, peak, v.DownColor, opacity);
        }

        private static void DrawSeries(Graphics g, RectangleF r, double[] data, double peak, Color color, float opacity)
        {
            int n = data.Length;
            if (n < 2) return;

            PointF[] pts = new PointF[n + 2];
            pts[0] = new PointF(r.Left, r.Bottom);
            for (int i = 0; i < n; i++)
            {
                float x = r.Left + r.Width * i / (float)(n - 1);
                float y = r.Bottom - (float)(data[i] / peak) * (r.Height - 1f);
                if (y < r.Top) y = r.Top;
                pts[i + 1] = new PointF(x, y);
            }
            pts[n + 1] = new PointF(r.Right, r.Bottom);

            using (SolidBrush fill = new SolidBrush(WithAlpha(color, ScaleAlpha(58, opacity))))
                g.FillPolygon(fill, pts);

            PointF[] line = new PointF[n];
            Array.Copy(pts, 1, line, 0, n);
            using (Pen p = new Pen(WithAlpha(color, ScaleAlpha(210, opacity)), Math.Max(1f, 1f * (r.Height / 20f))))
            {
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, line);
            }
        }

        // ------------------------------------------------------------------
        //  基础工具
        // ------------------------------------------------------------------
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0.1f)
            {
                p.AddRectangle(r);
                return p;
            }
            float d = radius * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.Left, r.Top, d, d, 180f, 90f);
            p.AddArc(r.Right - d, r.Top, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }

        public static Color WithAlpha(Color c, int a)
        {
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        private static int ScaleAlpha(int baseAlpha, float opacity)
        {
            int a = (int)Math.Round(baseAlpha * opacity);
            if (a < 0) a = 0;
            if (a > 255) a = 255;
            return a;
        }
    }

    // ======================================================================
    //  深色右键菜单（与浮窗风格一致）
    // ======================================================================
    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(40, 40, 44);
        private static readonly Color Hover = Color.FromArgb(66, 68, 74);
        private static readonly Color Line = Color.FromArgb(70, 72, 78);

        public override Color ToolStripDropDownBackground { get { return Bg; } }
        public override Color ImageMarginGradientBegin { get { return Bg; } }
        public override Color ImageMarginGradientMiddle { get { return Bg; } }
        public override Color ImageMarginGradientEnd { get { return Bg; } }
        public override Color MenuBorder { get { return Line; } }
        public override Color MenuItemBorder { get { return Hover; } }
        public override Color MenuItemSelected { get { return Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Bg; } }
        public override Color MenuItemPressedGradientEnd { get { return Bg; } }
        public override Color SeparatorDark { get { return Line; } }
        public override Color SeparatorLight { get { return Bg; } }
        public override Color CheckBackground { get { return Hover; } }
        public override Color CheckSelectedBackground { get { return Hover; } }
        public override Color CheckPressedBackground { get { return Hover; } }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Text = Color.FromArgb(234, 234, 238);
        private static readonly Color TextDisabled = Color.FromArgb(128, 128, 134);
        private static readonly Color Accent = Color.FromArgb(86, 170, 245);

        public DarkMenuRenderer() : base(new DarkColorTable())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle box = e.ImageRectangle;
            int size = Math.Min(box.Width, box.Height) - 4;
            if (size < 6) size = 6;
            RectangleF r = new RectangleF(
                box.Left + (box.Width - size) / 2f,
                box.Top + (box.Height - size) / 2f,
                size, size);

            using (GraphicsPath p = WidgetRenderer.RoundedRect(r, size * 0.28f))
            using (SolidBrush b = new SolidBrush(Accent))
                e.Graphics.FillPath(b, p);

            using (Pen pen = new Pen(Color.White, Math.Max(1.4f, size * 0.16f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float x0 = r.Left + size * 0.22f;
                float y0 = r.Top + size * 0.52f;
                float x1 = r.Left + size * 0.42f;
                float y1 = r.Top + size * 0.72f;
                float x2 = r.Left + size * 0.78f;
                float y2 = r.Top + size * 0.28f;
                e.Graphics.DrawLines(pen, new PointF[]
                {
                    new PointF(x0, y0), new PointF(x1, y1), new PointF(x2, y2)
                });
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // 去掉默认的白色边线，用 ColorTable 里的菜单边框
            Rectangle r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            r.Width -= 1;
            r.Height -= 1;
            using (Pen p = new Pen(new DarkColorTable().MenuBorder))
                e.Graphics.DrawRectangle(p, r);
        }
    }

    /// <summary>不依赖任何图标文件，直接画出托盘/程序图标。</summary>
    internal static class IconFactory
    {
        public static Icon Create(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    float pad = Math.Max(1f, size * 0.06f);
                    RectangleF r = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                    using (GraphicsPath p = WidgetRenderer.RoundedRect(r, size * 0.22f))
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 32, 34, 38)))
                            g.FillPath(b, p);
                        using (Pen pen = new Pen(Color.FromArgb(120, 120, 128), Math.Max(1f, size * 0.035f)))
                            g.DrawPath(pen, p);
                    }

                    float aw = size * 0.26f;
                    float ah = size * 0.42f;
                    WidgetRenderer.DrawArrow(g, size * 0.19f, size * 0.29f, aw, ah, true,
                        Color.FromArgb(255, 90, 214, 140));
                    WidgetRenderer.DrawArrow(g, size * 0.55f, size * 0.29f, aw, ah, false,
                        Color.FromArgb(255, 86, 170, 245));
                }

                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h))
                        return (Icon)tmp.Clone();
                }
                finally
                {
                    Native.DestroyIcon(h);
                }
            }
        }
    }
}
