using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace SchoolBell
{
    static class IconArt
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public static Bitmap Draw(int size, bool active)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float k = size / 32f;
                g.ScaleTransform(k, k);
                float penWidth = Math.Max(1.5f, 1.1f / k);

                Color light = active ? Color.FromArgb(255, 226, 120) : Color.FromArgb(215, 215, 215);
                Color dark = active ? Color.FromArgb(216, 146, 12) : Color.FromArgb(135, 135, 135);
                Color line = active ? Color.FromArgb(105, 66, 0) : Color.FromArgb(70, 70, 70);

                using (GraphicsPath body = BellPath())
                using (var fill = new LinearGradientBrush(new RectangleF(4, 3, 24, 24), light, dark, 55f))
                using (var solid = new SolidBrush(dark))
                using (var pen = new Pen(line, penWidth) { LineJoin = LineJoin.Round })
                {
                    g.FillEllipse(solid, 13.2f, 24.2f, 5.6f, 5.4f);
                    g.DrawEllipse(pen, 13.2f, 24.2f, 5.6f, 5.4f);
                    g.FillEllipse(fill, 13.5f, 1.4f, 5f, 5f);
                    g.DrawEllipse(pen, 13.5f, 1.4f, 5f, 5f);
                    g.FillPath(fill, body);
                    g.DrawPath(pen, body);
                    if (size >= 24)
                        using (var shine = new SolidBrush(Color.FromArgb(active ? 120 : 80, 255, 255, 255)))
                            g.FillEllipse(shine, 11.3f, 9.5f, 3.2f, 9f);
                }
                if (!active)
                {
                    using (var halo = new Pen(Color.White, Math.Max(5.5f, 3.2f / k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    using (var slash = new Pen(Color.FromArgb(215, 30, 30), Math.Max(3f, 1.8f / k)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(halo, 5, 27, 27, 5);
                        g.DrawLine(slash, 5, 27, 27, 5);
                    }
                }
            }
            return bmp;
        }

        static GraphicsPath BellPath()
        {
            var p = new GraphicsPath();
            p.AddBezier(16f, 5.5f, 10.5f, 5.5f, 9f, 10f, 9f, 15f);
            p.AddLine(9f, 15f, 9f, 18.5f);
            p.AddBezier(9f, 18.5f, 9f, 21.5f, 6.5f, 23f, 4.5f, 24f);
            p.AddLine(4.5f, 24f, 4.5f, 26f);
            p.AddLine(4.5f, 26f, 27.5f, 26f);
            p.AddLine(27.5f, 26f, 27.5f, 24f);
            p.AddBezier(27.5f, 24f, 25.5f, 23f, 23f, 21.5f, 23f, 18.5f);
            p.AddLine(23f, 18.5f, 23f, 15f);
            p.AddBezier(23f, 15f, 23f, 10f, 21.5f, 5.5f, 16f, 5.5f);
            p.CloseFigure();
            return p;
        }

        public static Icon MakeIcon(int size, bool active)
        {
            using (Bitmap bmp = Draw(size, active))
            {
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h))
                        return (Icon)tmp.Clone();
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }

        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap bmp = Draw(s, true))
                    images.Add(s >= 256 ? Png(bmp) : Dib(bmp));

            using (FileStream fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim);
                    w.Write(dim);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(images[i].Length);
                    w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
            }
        }

        static byte[] Png(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static byte[] Dib(Bitmap bmp)
        {
            int s = bmp.Width;
            int maskStride = ((s + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(40);
                w.Write(s);
                w.Write(s * 2);
                w.Write((short)1);
                w.Write((short)32);
                w.Write(0);
                w.Write(s * s * 4 + maskStride * s);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                w.Write(0);
                for (int y = s - 1; y >= 0; y--)
                    for (int x = 0; x < s; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        w.Write(c.B);
                        w.Write(c.G);
                        w.Write(c.R);
                        w.Write(c.A);
                    }
                for (int y = s - 1; y >= 0; y--)
                {
                    var row = new byte[maskStride];
                    for (int x = 0; x < s; x++)
                        if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                    w.Write(row);
                }
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
