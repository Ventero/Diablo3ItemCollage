﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ItemCollage
{
    public class ItemExtractor
    {
        private Bitmap bmp;
        private Point cursorPos;

        public Rectangle ItemFrame { get; private set; }

        public ItemExtractor(Bitmap bitmap, Point cursorPos)
        {
            this.bmp = bitmap;
            this.cursorPos = cursorPos;
        }

        private Rectangle FindBorder(Bitmap bmp, Point p)
        {
            if (!bmp.IsBlackAt(p.X, p.Y)) return new Rectangle();

            var left = p.X;
            var right = p.X;
            unsafe
            {
                var rect = new Rectangle(0, p.Y, bmp.Width, 1);
                var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly,
                    bmp.PixelFormat);
                var bytes = Image.GetPixelFormatSize(bmp.PixelFormat) / 8;

                try
                {
                    byte[] data = new byte[bmpData.Stride];
                    Marshal.Copy(bmpData.Scan0, data, 0, bmpData.Stride);

                    left = Helper.Range(p.X, 0, -1)
                        .TakeWhile(x => data.IsBlackAt(x, bytes))
                        .Last();

                    right = Helper.Range(p.X, bmp.Width - 1)
                        .TakeWhile(x => data.IsBlackAt(x, bytes))
                        .Last();
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }
            }

            return new Rectangle(left, p.Y, right - left + 1, 0);
        }

        private Rectangle SelectFrame(Bitmap bmp, Point p)
        {
            var skip = 0;
            const int MAX_SKIP = 2;

            if (!bmp.IsBlackAt(p.X, p.Y)) return new Rectangle();

            var top = Helper.Range(p.Y, 0, -1)
                    .TakeWhile(y => bmp.IsBlackAt(p.X, y) ||
                        skip++ < MAX_SKIP)
                    .Last(y => bmp.IsBlackAt(p.X, y));

            skip = 0;
            var bottom = Helper.Range(p.Y, bmp.Height - 1)
                    .TakeWhile(y => bmp.IsBlackAt(p.X, y) ||
                        skip++ < MAX_SKIP)
                    .Last(y => bmp.IsBlackAt(p.X, y));

            var border = FindBorder(bmp, new Point(p.X, bottom));
            var left = border.Left;
            var right = border.Right;

            // verify the left border is indeed black
            skip = 0;
            if (!Helper.Range(top, bottom).All(y => bmp.IsBlackAt(left, y) ||
                    skip++ < MAX_SKIP))
                return new Rectangle();

            return new Rectangle(left, top, border.Width, bottom - top + 1);
        }

        private Point FindOuter(Bitmap bmp, int x, int y, int step = 1, int searchWidth = 20)
        {
            var delta = step < 0 ? -1 : 1;
            var max = delta > 0 ? bmp.Width - x : x;
            if (searchWidth > max) throw new ArgumentOutOfRangeException();

            var target = Helper.Range(1, searchWidth, Math.Abs(step))
                .FirstOrDefault(dx => bmp.IsBlackAt(x + delta * dx, y));

            target = x + delta * target;

            // if possible, move slightly to the left or right to get to the
            // middle of the frame
            while (bmp.IsBlackAt(target + delta, y))
                target += delta;

            return new Point(target, y);
        }

        private List<Point> FindBlackSquares(Bitmap bmp, IEnumerable<int> horizontal,
            IEnumerable<int> vertical, int size = 5)
        {
            var black = new List<Point>();
            foreach (var y in vertical)
            {
                foreach (var x in horizontal)
                {
                    if (Helper.Range(-size, size).All(dx =>
                        Helper.Range(-size, size).All(dy => bmp.IsBlackAt(x + dx, y + dy))))
                    {
                        black.Add(new Point(x, y));
                        // since we move outwards from the cursor, we can safely
                        // break here without risking not to hit the actual item
                        // frame
                        break;
                    }
                }
            }
            return black;
        }

        public bool FindItem()
        {
            int minWidth = 150, minHeight = 90;

            // first, we have to find the inner item box
            // we do this by moving outwards from the cursor, that way we can
            // be sure to hit the actual item first, instead of a potential
            // equipped item popup
            var vertical = Helper.Range(0, bmp.Height, 5);
            var left = Helper.Range(cursorPos.X, 0, -minWidth);
            var right = Helper.Range(cursorPos.X, bmp.Width, minWidth);

            var black = new List<Point>();
            black.AddRange(FindBlackSquares(bmp, left, vertical));
            black.AddRange(FindBlackSquares(bmp, right, vertical));

            // find all left and right border points
            var frames = black.Select(p => FindBorder(bmp, p))
                .Where(f => f.Width >= minWidth);
            var leftBorders = frames.Distinct(f => f.Left);
            var rightBorders = frames.Distinct(f => f.Right);

            // from those border points, move outwards to find the outer frame
            var outerPoints = rightBorders.Select(f => FindOuter(bmp, f.Right, f.Top))
                .Concat(leftBorders.Select(f => FindOuter(bmp, f.Left, f.Bottom, -1)));

            var outerFrames = outerPoints.Distinct()
                .Select(p => SelectFrame(bmp, p))
                .Where(f => f.Width >= minWidth && f.Height >= minHeight);

            // the frame closest to the cursor position is (hopefully) the
            // item frame. if the cursor is inside the item frame, we simply
            // take the biggest frame we can find
            var itemFrame = outerFrames.OrderBy(f =>
                cursorPos.X > f.Right ? cursorPos.X - f.Right :
                cursorPos.X < f.Left ? f.Left - cursorPos.X : 0)
                .ThenByDescending(f => f.Width)
                .ThenByDescending(f => f.Height)
                .FirstOrDefault();

            if (itemFrame.Width < minWidth || itemFrame.Height < minHeight)
            {
                this.ItemFrame = new Rectangle();
                return false;
            }

            this.ItemFrame = itemFrame;
            return true;
        }

        public Image ExtractItem()
        {
            if (ItemFrame == new Rectangle() && !this.FindItem())
                return null;

            Bitmap item = new Bitmap(ItemFrame.Width, ItemFrame.Height,
                PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(item))
            {
                var targetFrame = new Rectangle(0, 0, ItemFrame.Width, ItemFrame.Height);
                g.DrawImage(bmp, targetFrame, ItemFrame, GraphicsUnit.Pixel);
            }

            return item;
        }

        public Bitmap ExtractItemName(bool removeFrame)
        {
            return ItemExtractor.ExtractItemName((Bitmap)this.ExtractItem(), removeFrame);
        }

        public static Bitmap ExtractItemName(Bitmap bmp, bool removeFrame)
        {
            // first, remove the black border to the left and right
            var left = Helper.Range(0, bmp.Width - 1).First(x =>
                !bmp.IsColumnBlack(x));
            // for the right border, we can't check from top to bottom because
            // linked items have a non-black [X] at the top right, so we only
            // check the bottom half
            var right = Helper.Range(bmp.Width - 2, 0, -1).First(x =>
                !bmp.IsColumnBlack(x, bmp.Height / 2)) + 1;

            // to separate the title from the actual item, simplify move down
            // from the first non-black row until everything is black again.
            // we don't check the full width to work around the [X] on linked
            // items again. additionally, we skip a few pixels to the left,
            // as there's sometimes some semi-black border left
            var top = Helper.Range(0, bmp.Height - 1).First(y =>
                bmp.IsRowNonBlack(y, left + 2, bmp.Width / 2)) - 1;

            // this is the first black row below the title, so the the title
            // height is given as bottom - top, not bottom - top + 1
            var bottom = Helper.Range(top + 1, bmp.Height - 1).First(y =>
                bmp.IsRowBlack(y, left, right));

            // remove any left-over semi-black border columns
            left = Helper.Range(left, bmp.Width - 1).First(x =>
                bmp.IsColumnNonBlack(x, top + 1, bottom - 1));
            right = Helper.Range(right, 0, -1).First(x =>
                bmp.IsColumnNonBlack(x, top + 1, bottom - 1)) + 1;

            if (!removeFrame)
            {
                var targetFrame = new Rectangle(left, top, right - left, bottom - top);

                var title = new Bitmap(targetFrame.Width, targetFrame.Height,
                    PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(title))
                {
                    g.DrawImage(bmp, new Rectangle(0, 0, title.Width, title.Height),
                        targetFrame, GraphicsUnit.Pixel);
                }

                return title;
            }

            // transform the image and remove 26% of the brightness to get
            // rid of the outer frame and the background color gradient
            ColorMatrix grayscale = new ColorMatrix(new float[][]
            {
                new float[] {0.30f, 0.30f, 0.30f, 0, 0},
                new float[] {0.40f, 0.40f, 0.40f, 0, 0},
                new float[] {0.30f, 0.30f, 0.30f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {-0.26f, -0.26f, -0.26f, 0, 1}
            });

            var attribs = new ImageAttributes();
            attribs.SetColorMatrix(grayscale);
            Bitmap img = new Bitmap(right - left, bottom - top,
                PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(img))
            {
                var target = new Rectangle(0, 0, img.Width, img.Height);
                g.DrawImage(bmp, target, left, top, img.Width, img.Height,
                    GraphicsUnit.Pixel, attribs);
            }

            // skip first row and column, as there's sometimes a non-
            // black pixel in there, and again don't check the full width
            // because of the close button for linked items
            // first row that contains the item name
            var innerTop = Helper.Range(1, img.Height - 1).First(y =>
                !img.IsRowBlack(y, 1, img.Width / 2));
            // again, the first row *below* the item name
            var innerBottom = Helper.Range(img.Height - 2, innerTop + 1, -1).First(y =>
                !img.IsRowBlack(y, 1)) + 1;

            // try to detect if the item is a linked one, so we can skip the X
            var xLeft = Helper.Range(1, img.Width).TakeWhile(dx =>
                !bmp.IsBlackAt(img.Width - dx, 0))
                .LastOrDefault();

            // first column that contains the text (again, skip 1 column)
            var innerLeft = Helper.Range(1, img.Width - 1).First(x =>
                !img.IsColumnBlack(x, innerTop, innerBottom));
            // the first black column behind the item text
            var innerRight = Helper.Range(img.Width - 2 - xLeft, 0, -1).First(x =>
                !img.IsColumnBlack(x, innerTop, innerBottom)) + 1;

            var nameFrame = new Rectangle(left + innerLeft, top + innerTop,
                innerRight - innerLeft, innerBottom - innerTop);

            var h = nameFrame.Height;
            var w = nameFrame.Width;
            var name = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            for (var x = 0; x < name.Width; x++)
            {
                // position in the item title frame that corresponds to x
                var innerX = x + innerLeft;
                // corresponding position in the full item frame
                var outerX = innerX + left;

                for (var y = 0; y < name.Height; y++)
                {
                    var innerY = y + innerTop;
                    var outerY = innerY + top;

                    if (!img.IsBlackAt(innerX, innerY))
                    {
                        // copy the matching and all neighboring pixels to get
                        // some kind of font anti-aliasing
                        var points = from dx in Helper.Range(-1, 1)
                                     from dy in Helper.Range(-1, 1)
                                     let fy = y + dy
                                     let fx = x + dx
                                     where fy >= 0 && fy < h && fx >= 0 && fx < w
                                     select new { dx, dy };

                        foreach (var d in points)
                            name.SetPixel(x + d.dx, y + d.dy,
                                bmp.GetPixel(outerX + d.dx, outerY + d.dy));
                    }
                }
            }

            return name;
        }
    }
}
