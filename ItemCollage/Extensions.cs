﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace ItemCollage
{
    public static class Extensions
    {
        private static IEnumerable<KeyValuePair<TVal, TMapped>> MapSortBy<TVal, TMapped>(
            this IEnumerable<TVal> source, Func<TVal, TMapped> selector)
        {
            return source.Select(o => new KeyValuePair<TVal, TMapped>(o, selector(o)))
                .OrderBy(a => a.Value);
        }

        public static TVal MaxBy<TVal, TMapped>(this IEnumerable<TVal> source,
            Func<TVal, TMapped> selector)
        {
            return source.MapSortBy(selector).Last().Key;
        }

        public static TVal MinBy<TVal, TMapped>(this IEnumerable<TVal> source,
        Func<TVal, TMapped> selector)
        {
            return source.MapSortBy(selector).First().Key;
        }

        public static bool IsBlackAt(this Bitmap b, int x, int y)
        {
            if (x < 0 || y < 0 || x >= b.Width || y >= b.Height)
                return false;

            Color c = b.GetPixel(x, y);
            return c.R == 0 && c.G == 0 && c.B == 0;
        }
    }
}