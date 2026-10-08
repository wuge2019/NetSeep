// NetSeep - 逐像素 alpha 的分层绘制表面
// 直接包裹 CreateDIBSection 出来的内存绘制：GDI+ 按预乘 alpha 写入，
// 正好符合 UpdateLayeredWindow + AC_SRC_ALPHA 的要求，且没有额外拷贝。
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace NetSeep
{
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _hBitmap = IntPtr.Zero;
        private IntPtr _bits = IntPtr.Zero;
        private Bitmap _bitmap;
        private Graphics _graphics;
        private Size _size = Size.Empty;

        public Size Size { get { return _size; } }

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
