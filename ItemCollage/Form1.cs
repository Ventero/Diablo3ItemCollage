﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;

namespace ItemCollage
{
    public partial class Form1 : Form
    {
        GlobalHotkey F1;
        List<Image> items = new List<Image>();

        public Form1()
        {
            InitializeComponent();
            F1 = new GlobalHotkey(Constants.NOMOD, Keys.F1, this);
            F1.Register();
            UpdateLabel();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            HandleF1();
        }

        private Rectangle FindFrame(Bitmap bmp, Point p, bool twoDim = true)
        {
            var black = Color.Black.ToArgb();
            try
            {
                var extentUp = p.Y;
                var extentDown = p.Y;

                if (twoDim)
                {
                    extentUp = p.Y -
                        Enumerable.Range(0, p.Y)
                            .TakeWhile(y => bmp.GetPixel(p.X, p.Y - y).ToArgb() == black)
                            .Last();
                    extentDown =
                        Enumerable.Range(p.Y, bmp.Height - p.Y)
                            .TakeWhile(y => bmp.GetPixel(p.X, y).ToArgb() == black)
                            .Last();
                }

                var extentLeft = p.X -
                    Enumerable.Range(0, p.X)
                        .TakeWhile(x => bmp.GetPixel(p.X - x, extentUp).ToArgb() == black &&
                                        bmp.GetPixel(p.X - x, extentDown).ToArgb() == black)
                        .Last();
                var extentRight =
                    Enumerable.Range(p.X, bmp.Width)
                        .TakeWhile(x => bmp.GetPixel(x, extentUp).ToArgb() == black &&
                                        bmp.GetPixel(x, extentDown).ToArgb() == black)
                        .Last();
                return new Rectangle(extentLeft, extentUp, extentRight - extentLeft, extentDown - extentUp);
            }
            catch
            {
                return new Rectangle();
            }
        }

        private IEnumerable<int> Range(int start, int end, int step = 1)
        {
            int i;
            for (i = start; i <= end; i += step)
            {
                yield return i;
            }
        }

        Image ExtractItem(Bitmap bmp, Point cursorPosition)
        {
            var searchSize = new Size(300, 300);
            var searchRect = new Rectangle(cursorPosition.X - searchSize.Width / 2,
                                           cursorPosition.Y - searchSize.Height / 2,
                                           searchSize.Width, searchSize.Height);

            // first, we have to find the inner item box
            var black = from y in Range(searchRect.Top, searchRect.Bottom, 5)
                        from x in Range(searchRect.Left, searchRect.Right, 5)
                        where Range(-5, 5).All(dx =>
                            Range(-5, 5).All(dy => bmp.IsBlackAt(x + dx, y + dy)))
                        select new Point(x, y);
            var frames = black.Select(p => FindFrame(bmp, p, false));

            // then, try to find its left border
            var inner = frames.OrderBy(f => f.Width).Last();

            // and from there find the outer frame
            var target = inner.Left - Enumerable.Range(1, inner.Left)
                .FirstOrDefault(x => bmp.IsBlackAt(inner.Left - x, inner.Top));
            // TODO: error handling
            var outer = FindFrame(bmp, new Point(target - 1, inner.Top));

            Graphics g = Graphics.FromImage(bmp);
            Pen[] colors = new Pen[] { Pens.Blue, Pens.White, Pens.Red, Pens.Green,
                                       Pens.Beige, Pens.Purple, Pens.YellowGreen };
            var i = 0;
            foreach (var frame in frames)
            {
                g.DrawRectangle(colors[i], frame.Left, frame.Top, frame.Width, frame.Height + 1);
                i = (i + 1) % colors.Length;
            }
            outer.Height += 1;
            g.DrawRectangle(Pens.Gold, outer);
            g.DrawEllipse(Pens.Gold, target - 10, inner.Top - 10, 20, 20);
            g.Dispose();

            return bmp;
        }

        private void HandleF1()
        {
            Stopwatch sw = new Stopwatch();

            var cursorPos = Cursor.Position;

            sw.Start();
            this.Opacity = 0;
            var screen = TakeScreenshot(ref cursorPos);
            this.Opacity = 1;

            var item = ExtractItem(screen, cursorPos);
            sw.Stop();

            pictureBox1.Image = item;
            Clipboard.SetImage(item);

            label1.Text = sw.Elapsed.ToString();
        }

        private void UpdateLabel()
        {
            label1.Text = string.Format("{0} item(s) copied.", items.Count);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Constants.WM_HOTKEY_MSG_ID)
                HandleF1();
            base.WndProc(ref m);
        }

        private Bitmap TakeScreenshot(ref Point p)
        {
            var bounds = Screen.FromPoint(p).Bounds;
            p.Offset(-bounds.X, -bounds.Y);
            Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(bounds.Width, bounds.Height));
            return bmp;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            F1.Unregister();
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            if (items.Count == 0) return;
            items.RemoveAt(items.Count - 1);
            if (items.Count > 0)
                pictureBox1.Image = items[items.Count - 1];
            else pictureBox1.Image = null;

            UpdateLabel();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                if (items.Count == 0) return;

                int numCols = (int)Math.Min(Math.Ceiling(Math.Sqrt(items.Count)), 4);
                int w = items[0].Width;
                var colLengths = new int[numCols];

                foreach (var item in items)
                {
                    var col = Range(0, numCols).Select(i => new { i = i, h = colLengths[i] }).OrderBy(k => k.h).First().i;
                    colLengths[col] += item.Height;
                }

                Bitmap b = new Bitmap(numCols * w, colLengths.Max(), PixelFormat.Format16bppRgb555);
                Graphics g = Graphics.FromImage(b);
                //g.FillRectangle(Brushes.Black, 0, 0, b.Width, b.Height);
                colLengths = new int[numCols];

                int itemIndex = 1;

                foreach (var item in items)
                {
                    var col = Range(0, numCols).Select(i => new { i = i, h = colLengths[i] }).OrderBy(k => k.h).First().i;
                    g.DrawImageUnscaledAndClipped(item, new Rectangle(w * col, colLengths[col], w, item.Height));
                    g.DrawString(itemIndex.ToString(), new Font("Arial", 20, FontStyle.Bold), Brushes.White, col * w + 10, colLengths[col] + 10);
                    colLengths[col] += item.Height;
                    itemIndex++;
                }

                var picFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                var fileName = string.Format("items-{0:yyyy-MM-dd-HH-mm-ss}.png", DateTime.UtcNow);
                var file = Path.Combine(picFolder, fileName);
                b.Save(file);
                items.Clear();
                UpdateLabel();
            }
            catch { }
        }
    }
}

public static class Extensions
{
    /// <summary>
    ///     Yields a sequence of points that spiral around a center point within a rectangle of the given size.
    ///     Code translated from http://stackoverflow.com/a/398302/73070.
    /// </summary>
    /// <param name="center">The center point of the spiral.</param>
    /// <param name="sizeX">The width of the resulting rectangle.</param>
    /// <param name="sizeY">The height of the resulting rectangle.</param>
    /// <returns>An enumerator that visits all points in the given rectangle in a spiral.</returns>
    public static IEnumerable<Point> Spiral(this Point center, int sizeX, int sizeY)
    {
        int x = 0, y = 0;
        int dx = 0, dy = -1;

        var maxDim = Math.Max(sizeX, sizeY);

        for (int i = 0; i < maxDim * maxDim; i++)
        {
            if ((-sizeX / 2 < x && x <= sizeX / 2) && (-sizeY / 2 < y && y <= sizeY / 2))
                yield return new Point(center.X + x, center.Y + y);

            if (x == y || (x < 0 && x == -y) || (x > 0 && x == 1 - y))
            {
                var temp = dx;
                dx = -dy;
                dy = temp;
            }
            x += dx;
            y += dy;
        }
    }

    public static bool IsBlackAt(this Bitmap b, int x, int y)
    {
        if (x < 0 || y < 0 || x >= b.Width || y >= b.Height)
            return false;

        Color c = b.GetPixel(x, y);
        return c.R == 0 && c.G == 0 && c.B == 0;
    }
}