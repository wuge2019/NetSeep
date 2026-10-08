// NetSeep - 悬浮窗主体（分层窗口逐像素透明 + 交互 + 托盘 + 右键菜单）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetSeep
{
    internal sealed class WidgetForm : Form
    {
        private const int HotkeyId = 0x4E53; // 'NS'
        private const int HistoryLength = 60;

        private readonly AppConfig _cfg;
        private readonly bool _silent;
        private readonly TrafficSampler _sampler = new TrafficSampler();
        private readonly LayeredSurface _surface = new LayeredSurface();
        private readonly List<double> _upHist = new List<double>();
        private readonly List<double> _downHist = new List<double>();

        private Timer _sampleTimer;
        private Timer _watchTimer;
        private NotifyIcon _tray;
        private ContextMenuStrip _menu;
        private Icon _icon;

        private List<NicEntry> _nics = new List<NicEntry>();
        private float _scale = 1f;
        private double _upBps, _downBps;
        private double _sessionUp, _sessionDown;
        private bool _dragging;
        private Point _dragStart;
        private Point _winStart;
        private bool _hiddenByFullscreen;
        private bool _userHidden;
        private bool _hotkeyOn;
        private bool _hover;

        public WidgetForm(AppConfig cfg, bool silent)
        {
            _cfg = cfg;
            _silent = silent;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Text = "NetSeep";
            TopMost = cfg.TopMost;
            Size = new Size(96, 40);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // 说明：曾尝试用 cp.ClassName 换成自定义类名，但 WinForms 的
                // NativeWindow.WindowClass.RegisterClass 只接受它自己生成的类名，
                // 自定义类名会抛“窗口类名无效”，因此这里保持默认。
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                // 注意：CreateParams 会在基类构造期间被访问，此时 _cfg 可能尚未赋值
                if (_cfg != null && _cfg.ClickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        // ==================================================================
        //  生命周期
        // ==================================================================
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTopMost();
            ApplyClickThrough();
            UpdateHotkey();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _scale = Native.GetDpiScale(Handle);
            EnsureNicList();
            _sampler.Prime();
            Render();
            ApplyPosition();

            _sampleTimer = new Timer();
            _sampleTimer.Interval = _cfg.IntervalMs;
            _sampleTimer.Tick += OnSampleTick;
            _sampleTimer.Start();

            _watchTimer = new Timer();
            _watchTimer.Interval = 700;
            _watchTimer.Tick += OnWatchTick;
            _watchTimer.Start();

            CreateTray();

            if (_cfg.FirstRun && !_silent)
            {
                ShowBalloon("NetSeep 已启动",
                    "拖动可移动位置，右键（或托盘图标）可设置网卡、透明度、开机自启等。\nCtrl+Alt+N 可随时开关鼠标穿透。",
                    ToolTipIcon.Info);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_sampleTimer != null) _sampleTimer.Stop();
            if (_watchTimer != null) _watchTimer.Stop();

            if (_hotkeyOn)
            {
                try { Native.UnregisterHotKey(Handle, HotkeyId); } catch (Exception) { }
                _hotkeyOn = false;
            }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            if (_icon != null)
            {
                _icon.Dispose();
                _icon = null;
            }
            if (_menu != null)
            {
                _menu.Dispose();
                _menu = null;
            }
            _surface.Dispose();

            _cfg.X = Left;
            _cfg.Y = Top;
            _cfg.Save();
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                SetClickThrough(!_cfg.ClickThrough, true);
                return;
            }
            if (m.Msg == Native.WM_DPICHANGED)
            {
                // 拖动到不同缩放的显示器：按新 DPI 重新排版，位置保持不变
                _scale = Native.GetDpiScale(Handle);
                Render();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // 分层窗口完全由 UpdateLayeredWindow 绘制，屏蔽 WinForms 自身的绘制
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        // ==================================================================
        //  采样与刷新
        // ==================================================================
        private void OnSampleTick(object sender, EventArgs e)
        {
            TrafficSample s = _sampler.Sample(_cfg.NicId);
            _upBps = s.UpBps;
            _downBps = s.DownBps;
            _sessionUp += s.UpBytes;
            _sessionDown += s.DownBytes;

            Push(_upHist, _upBps);
            Push(_downHist, _downBps);

            Render();
            UpdateTrayText();
        }

        private static void Push(List<double> list, double v)
        {
            list.Add(v);
            while (list.Count > HistoryLength) list.RemoveAt(0);
        }

        private void OnWatchTick(object sender, EventArgs e)
        {
            CheckFullscreen();
        }

        private WidgetVisual BuildVisual()
        {
            WidgetVisual v = new WidgetVisual();
            string value, unit;

            SpeedFormat.Split(_upBps, out value, out unit);
            v.UpValue = value;
            v.UpUnit = unit;

            SpeedFormat.Split(_downBps, out value, out unit);
            v.DownValue = value;
            v.DownUnit = unit;

            v.ShowUp = _cfg.ShowUp;
            v.ShowDown = _cfg.ShowDown;
            v.ShowGraph = _cfg.ShowGraph;
            v.Opacity = _cfg.OpacityPercent / 100f;
            v.BgColor = AppConfig.ParseColor(_cfg.BgColor, Color.FromArgb(24, 24, 27));
            v.UpColor = AppConfig.ParseColor(_cfg.UpColor, Color.FromArgb(90, 214, 140));
            v.DownColor = AppConfig.ParseColor(_cfg.DownColor, Color.FromArgb(86, 170, 245));
            v.UpHistory = _upHist.ToArray();
            v.DownHistory = _downHist.ToArray();
            return v;
        }

        private void Render()
        {
            if (!IsHandleCreated) return;

            WidgetVisual v = BuildVisual();
            Size want;

            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics pg = Graphics.FromImage(probe))
            using (WidgetMetrics m = WidgetRenderer.Measure(v, _scale, pg))
            {
                want = m.Size;
                if (ClientSize != want)
                {
                    ClientSize = want;
                    _surface.EnsureSize(want);
                }
                _surface.Draw(delegate (Graphics g)
                {
                    g.Clear(Color.Transparent);
                    WidgetRenderer.Draw(g, v, m);
                });
            }
            _surface.Flush(Handle, Left, Top);
        }

        // ==================================================================
        //  位置 / 层级 / 穿透
        // ==================================================================
        private void ApplyPosition()
        {
            int w = Width, h = Height;
            if (_cfg.X.HasValue && _cfg.Y.HasValue && IsOnScreen(_cfg.X.Value, _cfg.Y.Value, w, h))
            {
                Location = new Point(_cfg.X.Value, _cfg.Y.Value);
                return;
            }
            Rectangle wa = Screen.FromHandle(Handle).WorkingArea;
            int margin = (int)Math.Round(12 * _scale);
            Location = new Point(wa.Right - w - margin, wa.Top + margin);
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

        private void ResetPosition()
        {
            _cfg.X = null;
            _cfg.Y = null;
            ApplyPosition();
            _cfg.X = Left;
            _cfg.Y = Top;
            _cfg.Save();
        }

        private void ApplyTopMost()
        {
            TopMost = _cfg.TopMost;
            Native.SetWindowPos(Handle,
                _cfg.TopMost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST,
                0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void ApplyClickThrough()
        {
            if (!IsHandleCreated) return;
            if (_cfg.ClickThrough)
            {
#if MINIMAL_BUILD
                _cfg.ClickThrough = false;   // 精简构建不带穿透
#else
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT);
#endif
            }
            else
            {
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_TRANSPARENT);
            }
        }

        /// <summary>
        /// 全局热键只在“鼠标穿透”开启期间注册 —— 它是穿透状态下唯一的逃生通道。
        /// 平时不注册全局热键，既不占键位，也不给安全软件的行为引擎添话题。
        /// </summary>
        private void UpdateHotkey()
        {
#if MINIMAL_BUILD
            return;
#else
            if (!IsHandleCreated) return;
            bool need = _cfg.ClickThrough;
            if (need == _hotkeyOn) return;
            try
            {
                if (need)
                {
                    _hotkeyOn = Native.RegisterHotKey(Handle, HotkeyId,
                        Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (int)Keys.N);
                }
                else
                {
                    Native.UnregisterHotKey(Handle, HotkeyId);
                    _hotkeyOn = false;
                }
            }
            catch (Exception)
            {
                _hotkeyOn = false;
            }
#endif
        }

        private void SetClickThrough(bool on, bool announce)
        {
#if MINIMAL_BUILD
            if (on) return;
#endif
            _cfg.ClickThrough = on;
            ApplyClickThrough();
            UpdateHotkey();
            _cfg.Save();
            if (announce)
            {
                // 穿透状态下浮窗无法再接收鼠标消息，所以通过托盘气泡提示如何恢复
                ShowBalloon("NetSeep",
                    on ? "已开启鼠标穿透（浮窗不再拦截点击）。\n按 Ctrl+Alt+N 或用托盘菜单可恢复。"
                       : "已关闭鼠标穿透，浮窗可以正常拖动和右键。",
                    ToolTipIcon.Info);
            }
        }

        private void CheckFullscreen()
        {
            if (!_cfg.AutoHideFullscreen)
            {
                UpdateVisibility(false);
                return;
            }

            // 交给系统的“当前是否适合弹通知”来判断全屏/演示模式：
            // 不枚举前台窗口、不读取别的进程信息，行为更干净也更可靠。
            UpdateVisibility(Native.IsFullscreenBusy());
        }

        /// <summary>综合“用户手动隐藏”和“全屏程序遮挡”决定浮窗是否显示。</summary>
        private void UpdateVisibility(bool fullscreenCovers)
        {
            bool want = !_userHidden && !fullscreenCovers;
            _hiddenByFullscreen = !want && !_userHidden;
            if (want == Visible) return;

            Visible = want;
            if (want)
            {
                Render();
                ApplyTopMost();
            }
        }

        // ==================================================================
        //  鼠标交互
        // ==================================================================
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowMenu();
            }
            else if (e.Button == MouseButtons.Left && !_cfg.Locked)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _winStart = Location;
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Point p = Cursor.Position;
                Location = new Point(_winStart.X + (p.X - _dragStart.X), _winStart.Y + (p.Y - _dragStart.Y));
            }
            else if (!_hover)
            {
                _hover = true;
                if (_cfg.Locked) Cursor = Cursors.SizeAll;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                Capture = false;
                _cfg.X = Left;
                _cfg.Y = Top;
                _cfg.Save();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Cursor = Cursors.Default;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _cfg.Locked = !_cfg.Locked;
                _cfg.Save();
                ShowBalloon("NetSeep", _cfg.Locked ? "已锁定位置（双击解锁）" : "已解锁位置，可以拖动", ToolTipIcon.Info);
            }
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int step = e.Delta > 0 ? 5 : -5;
            int v = _cfg.OpacityPercent + step;
            if (v < 30) v = 30;
            if (v > 100) v = 100;
            if (v != _cfg.OpacityPercent)
            {
                _cfg.OpacityPercent = v;
                _cfg.Save();
                Render();
            }
            base.OnMouseWheel(e);
        }

        // ==================================================================
        //  托盘与菜单
        // ==================================================================
        private void CreateTray()
        {
            _menu = new ContextMenuStrip();
            _menu.Renderer = new DarkMenuRenderer();
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;
            _menu.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            _menu.ForeColor = Color.FromArgb(234, 234, 238);
            _menu.BackColor = Color.FromArgb(40, 40, 44);
            _menu.Opening += delegate { BuildMenu(); };

            _icon = IconFactory.Create(32);

            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "NetSeep";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate
            {
                SetClickThrough(false, false);
                _userHidden = false;
                UpdateVisibility(false);
                ResetPosition();
                ShowBalloon("NetSeep", "浮窗已恢复显示并回到默认位置", ToolTipIcon.Info);
            };

            UpdateTrayText();
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (_tray == null) return;
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = icon;
                _tray.ShowBalloonTip(4000);
            }
            catch (Exception) { }
        }

        private void UpdateTrayText()
        {
            if (_tray == null) return;
            string uv, uu, dv, du;
            SpeedFormat.Split(_upBps, out uv, out uu);
            SpeedFormat.Split(_downBps, out dv, out du);
            string t = "NetSeep  ↑ " + uv + uu + "  ↓ " + dv + du;
            if (t.Length > 63) t = t.Substring(0, 63);
            try { _tray.Text = t; } catch (Exception) { }
        }

        private void ShowMenu()
        {
            BuildMenu();
            _menu.Show(Cursor.Position);
        }

        private void EnsureNicList()
        {
            _nics = NicTable.List();
            if (_cfg.NicId != Guid.Empty)
            {
                bool found = false;
                foreach (NicEntry n in _nics)
                    if (n.Id == _cfg.NicId) { found = true; break; }
                if (!found)
                {
                    _cfg.NicId = Guid.Empty; // 原来选的网卡已不存在，回退到合计
                    _cfg.Save();
                }
            }
        }

        private ToolStripMenuItem Item(string text, bool check, EventHandler onClick)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.Checked = check;
            it.CheckOnClick = false;
            if (onClick != null) it.Click += onClick;
            return it;
        }

        private void BuildMenu()
        {
            if (_menu == null) return;
            _menu.Items.Clear();

            string visText = Visible ? "隐藏浮窗" : (_hiddenByFullscreen ? "显示浮窗（当前被全屏程序遮挡）" : "显示浮窗");
            _menu.Items.Add(Item(visText, false, delegate
            {
                _userHidden = Visible;
                UpdateVisibility(false);
            }));

            // ---- 网卡 ----
            EnsureNicList();
            ToolStripMenuItem nicMenu = new ToolStripMenuItem("选择网卡");
            nicMenu.DropDown.Renderer = new DarkMenuRenderer();
            nicMenu.DropDownItems.Add(Item("全部网卡（合计）", _cfg.NicId == Guid.Empty, delegate
            {
                _cfg.NicId = Guid.Empty;
                _cfg.Save();
            }));
            if (_nics.Count > 0) nicMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (NicEntry n in _nics)
            {
                NicEntry captured = n;
                ToolStripMenuItem it = Item(n.Display + (n.IsUp ? "" : "（未连接）"), _cfg.NicId == n.Id, delegate
                {
                    _cfg.NicId = captured.Id;
                    _cfg.Save();
                });
                it.ToolTipText = n.ToolTip;
                nicMenu.DropDownItems.Add(it);
            }
            _menu.Items.Add(nicMenu);

            // ---- 显示内容 ----
            ToolStripMenuItem show = new ToolStripMenuItem("显示内容");
            show.DropDown.Renderer = new DarkMenuRenderer();
            show.DropDownItems.Add(Item("上传速度", _cfg.ShowUp, delegate
            {
                if (_cfg.ShowUp && !_cfg.ShowDown) return; // 至少保留一项
                _cfg.ShowUp = !_cfg.ShowUp;
                _cfg.Save();
                Render();
            }));
            show.DropDownItems.Add(Item("下载速度", _cfg.ShowDown, delegate
            {
                if (_cfg.ShowDown && !_cfg.ShowUp) return;
                _cfg.ShowDown = !_cfg.ShowDown;
                _cfg.Save();
                Render();
            }));
            show.DropDownItems.Add(new ToolStripSeparator());
            show.DropDownItems.Add(Item("迷你曲线图", _cfg.ShowGraph, delegate
            {
                _cfg.ShowGraph = !_cfg.ShowGraph;
                _cfg.Save();
                Render();
            }));
            _menu.Items.Add(show);

            // ---- 刷新间隔 ----
            ToolStripMenuItem iv = new ToolStripMenuItem("刷新间隔");
            iv.DropDown.Renderer = new DarkMenuRenderer();
            int[] options = new int[] { 500, 1000, 2000, 5000 };
            foreach (int ms in options)
            {
                int captured = ms;
                iv.DropDownItems.Add(Item(ms < 1000 ? ms + " 毫秒" : (ms / 1000) + " 秒",
                    _cfg.IntervalMs == ms, delegate
                {
                    _cfg.IntervalMs = captured;
                    _cfg.Save();
                    if (_sampleTimer != null) _sampleTimer.Interval = captured;
                }));
            }
            _menu.Items.Add(iv);

            // ---- 不透明度 ----
            ToolStripMenuItem op = new ToolStripMenuItem("不透明度");
            op.DropDown.Renderer = new DarkMenuRenderer();
            int[] ops = new int[] { 100, 90, 80, 70, 60, 50 };
            foreach (int o in ops)
            {
                int captured = o;
                op.DropDownItems.Add(Item(captured + " %", _cfg.OpacityPercent == captured, delegate
                {
                    _cfg.OpacityPercent = captured;
                    _cfg.Save();
                    Render();
                }));
            }
            op.DropDownItems.Add(new ToolStripSeparator());
            op.DropDownItems.Add(Item("降低 (滚轮向下)", false, delegate { AdjustOpacity(-5); }));
            op.DropDownItems.Add(Item("提高 (滚轮向上)", false, delegate { AdjustOpacity(5); }));
            _menu.Items.Add(op);

            _menu.Items.Add(new ToolStripSeparator());

            // ---- 行为开关 ----
            _menu.Items.Add(Item("置顶显示", _cfg.TopMost, delegate
            {
                _cfg.TopMost = !_cfg.TopMost;
                _cfg.Save();
                ApplyTopMost();
            }));
            _menu.Items.Add(Item("锁定位置", _cfg.Locked, delegate
            {
                _cfg.Locked = !_cfg.Locked;
                _cfg.Save();
            }));
#if !MINIMAL_BUILD
            _menu.Items.Add(Item("鼠标穿透 (开启后 Ctrl+Alt+N 恢复)", _cfg.ClickThrough, delegate
            {
                SetClickThrough(!_cfg.ClickThrough, true);
            }));
#endif
            _menu.Items.Add(Item("全屏时自动隐藏", _cfg.AutoHideFullscreen, delegate
            {
                _cfg.AutoHideFullscreen = !_cfg.AutoHideFullscreen;
                _cfg.Save();
                if (!_cfg.AutoHideFullscreen) UpdateVisibility(false);
            }));

#if !MINIMAL_BUILD
            // ---- 开机自启 ----
            ToolStripMenuItem auto = Item("开机自动启动", AppConfig.AutoStartEnabled, delegate
            {
                bool want = !AppConfig.AutoStartEnabled;
                if (!AppConfig.SetAutoStart(want))
                    ShowBalloon("NetSeep", "写入注册表失败，无法修改开机自启", ToolTipIcon.Warning);
            });
            auto.ToolTipText = AppConfig.AutoStartEnabled && !AppConfig.AutoStartIsCurrent
                ? "当前登记的是旧路径，重新勾选可更新"
                : "写入 HKEY_CURRENT_USER\\...\\Run";
            _menu.Items.Add(auto);
#endif

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Item("回到默认位置", false, delegate { ResetPosition(); }));
            _menu.Items.Add(Item("关于 NetSeep", false, delegate { ShowAbout(); }));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Item("退出", false, delegate { Close(); }));
        }

        private void AdjustOpacity(int delta)
        {
            int v = _cfg.OpacityPercent + delta;
            if (v < 30) v = 30;
            if (v > 100) v = 100;
            _cfg.OpacityPercent = v;
            _cfg.Save();
            Render();
        }

        private void ShowAbout()
        {
            string uv, uu, dv, du;
            SpeedFormat.Split(_upBps, out uv, out uu);
            SpeedFormat.Split(_downBps, out dv, out du);

            string text =
                "NetSeep " + Program.Version + "  ·  轻量级网速悬浮窗" + Environment.NewLine +
                "纯 C# / .NET Framework 实现，无第三方依赖" + Environment.NewLine +
                Environment.NewLine +
                "当前：↑ " + uv + uu + "   ↓ " + dv + du + Environment.NewLine +
                "本次运行累计：↑ " + SpeedFormat.Volume(_sessionUp) + "   ↓ " + SpeedFormat.Volume(_sessionDown) + Environment.NewLine +
                Environment.NewLine +
                "拖动 = 移动位置，双击 = 锁定/解锁" + Environment.NewLine +
                "滚轮 = 调整不透明度，右键 = 菜单" + Environment.NewLine +
                "Ctrl+Alt+N = 开关鼠标穿透" + Environment.NewLine +
                "托盘图标双击 = 拉回浮窗" + Environment.NewLine +
                Environment.NewLine +
                "配置文件：" + AppConfig.FilePath;

            MessageBox.Show(this, text, "关于 NetSeep", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ==================================================================
        //  逐像素 alpha 分层绘制表面
        // ==================================================================
        private sealed class LayeredSurface : IDisposable
        {
            private IntPtr _hBitmap = IntPtr.Zero;
            private IntPtr _bits = IntPtr.Zero;
            private Bitmap _bitmap;
            private Graphics _graphics;
            private Size _size = Size.Empty;

            public void EnsureSize(Size size)
            {
                if (size.Width <= 0 || size.Height <= 0) return;
                if (size == _size && _bitmap != null) return;
                Release();
                _size = size;

                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                try
                {
                    Native.BITMAPINFO bmi = new Native.BITMAPINFO();
                    bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                    bmi.bmiHeader.biWidth = size.Width;
                    bmi.bmiHeader.biHeight = -size.Height; // 自上而下
                    bmi.bmiHeader.biPlanes = 1;
                    bmi.bmiHeader.biBitCount = 32;
                    bmi.bmiHeader.biCompression = Native.BI_RGB;

                    _hBitmap = Native.CreateDIBSection(screenDc, ref bmi, Native.DIB_RGB_COLORS,
                        out _bits, IntPtr.Zero, 0);
                    if (_hBitmap == IntPtr.Zero || _bits == IntPtr.Zero)
                    {
                        Release();
                        return;
                    }

                    // 直接包裹 DIB 内存绘制：GDI+ 会按预乘 alpha 写入，正好符合 UpdateLayeredWindow 的要求
                    _bitmap = new Bitmap(size.Width, size.Height, size.Width * 4,
                        PixelFormat.Format32bppPArgb, _bits);
                    _graphics = Graphics.FromImage(_bitmap);
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            public void Draw(Action<Graphics> draw)
            {
                if (_graphics == null) return;
                draw(_graphics);
                _graphics.Flush(FlushIntention.Sync);
            }

            public void Flush(IntPtr hwnd, int x, int y)
            {
                if (_hBitmap == IntPtr.Zero) return;

                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                IntPtr memDc = Native.CreateCompatibleDC(screenDc);
                IntPtr old = IntPtr.Zero;
                try
                {
                    old = Native.SelectObject(memDc, _hBitmap);
                    Native.POINT src = new Native.POINT(0, 0);
                    Native.POINT dst = new Native.POINT(x, y);
                    Native.SIZE size = new Native.SIZE(_size.Width, _size.Height);
                    Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                    bf.BlendOp = Native.AC_SRC_OVER;
                    bf.BlendFlags = 0;
                    bf.SourceConstantAlpha = 255;
                    bf.AlphaFormat = Native.AC_SRC_ALPHA;
                    Native.UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
                }
                finally
                {
                    if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
                    Native.DeleteDC(memDc);
                    Native.ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            private void Release()
            {
                if (_graphics != null) { _graphics.Dispose(); _graphics = null; }
                if (_bitmap != null) { _bitmap.Dispose(); _bitmap = null; }
                if (_hBitmap != IntPtr.Zero) { Native.DeleteObject(_hBitmap); _hBitmap = IntPtr.Zero; }
                _bits = IntPtr.Zero;
                _size = Size.Empty;
            }

            public void Dispose()
            {
                Release();
            }
        }
    }
}
