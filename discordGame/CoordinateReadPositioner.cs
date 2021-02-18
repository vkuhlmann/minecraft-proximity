using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

namespace discordGame
{
    static class CoordinateReadPositioner
    {
        public static readonly Color ORANGE_COLOR = Color.FromArgb(252, 168, 0);
        public static readonly Color GRAY_COLOR = Color.FromArgb(221, 221, 221);

        public struct Positioning
        {
            public float scale;
            public Rectangle bbox;

            //public Color c;
            //public bool Verify(Bitmap b)
            //{
            //    return true;
            //}
        }

        public static IEnumerable<Positioning> FindPossiblePositions(Bitmap bitmap, Rectangle bounds)
        {
            HashSet<Positioning> exploredPositions = new HashSet<Positioning>();

            foreach (Color c in new Color[] { GRAY_COLOR, ORANGE_COLOR })
            {
                int scale = 4;
                for (int mod = 0; mod < scale; mod++)
                {
                    IEnumerable<Positioning> m = FindZ(bitmap, bounds, c, scale, mod);
                    foreach (Positioning ans in m)
                    {
                        Positioning cp = ans;
                        cp.bbox.X = cp.bbox.Right;
                        cp.bbox.Width = Math.Min(bounds.Right, cp.bbox.X + (int)(180 * ans.scale)) - cp.bbox.X;

                        //for (int x = cp.bbox.X; x < cp.bbox.Right; x++)
                        //    bitmap.SetPixel(x, cp.bbox.Y - 1, Color.Red);
                        //for (int y = cp.bbox.Y; y < cp.bbox.Bottom; y++)
                        //    bitmap.SetPixel(cp.bbox.X - 1, y, Color.Red);
                        //for (int y = cp.bbox.Y; y < cp.bbox.Bottom; y++)
                        //    bitmap.SetPixel(cp.bbox.Right - 1, y, Color.Red);
                        //for (int x = cp.bbox.X; x < cp.bbox.Right; x++)
                        //    bitmap.SetPixel(x, cp.bbox.Bottom - 1, Color.Red);
                        yield return cp;
                    }
                }
            }
        }

        static int ExpandToLeft(Bitmap b, int x, int y, int height, Color c, int minX = 0)
        {
            for (int u = x - 1; u >= minX; u--)
                for (int v = y; v < y + height; v++)
                    if (b.GetPixel(u, v) != c)
                        return u + 1;
            return minX;
        }

        static int ExpandToRight(Bitmap b, int x, int y, int height, Color color, int maxXExcl)
        {
            for (int u = x + 1; u < maxXExcl; u++)
                for (int v = y; v < y + height; v++)
                    if (b.GetPixel(u, v) != color)
                        return u;
            return maxXExcl;
        }

        static int ExpandToTop(Bitmap b, int x, int y, int width, Color color, int minY = 0)
        {
            for (int v = y - 1; v >= minY; v--)
                for (int u = x; u < x + width; u++)
                    if (b.GetPixel(u, v) != color)
                        return v + 1;
            return minY;
        }

        static int ExpandToBottom(Bitmap b, int x, int y, int width, Color color, int maxYExcl)
        {
            for (int v = y + 1; v < maxYExcl; v++)
                for (int u = x; u < x + width; u++)
                    if (b.GetPixel(u, v) != color)
                        return v;
            return maxYExcl;
        }

        static Rectangle ExpandToPlainColor(Bitmap b, int x, int y)
        {
            int width = 1;
            int height = 1;
            Color color = b.GetPixel(x, y);

            int farLeft = ExpandToLeft(b, x, y, height, color);
            int farRight = ExpandToRight(b, x, y, height, color, b.Width);
            x = farLeft;
            width = farRight - farLeft;

            int farTop = ExpandToTop(b, x, y, width, color);
            int farBottom = ExpandToBottom(b, x, y, width, color, b.Height);
            y = farTop;
            height = farBottom - farTop;

            return new Rectangle(x, y, width, height);
        }

        static List<Rectangle> FindHorizLines(Bitmap bitmap, Rectangle bounds, Color color, int scale, int mod)
        {
            List<Rectangle> rects = new List<Rectangle>();
            HashSet<Rectangle> rectsHash = new HashSet<Rectangle>();

            for (int y = bounds.Y + mod; y < bounds.Bottom; y += scale)
            {
                for (int x = bounds.X + mod; x < bounds.Right; x += scale)
                {
                    if (bitmap.GetPixel(x, y) == color)
                    {
                        Rectangle rect = ExpandToPlainColor(bitmap, x, y);
                        float ratio = (float)rect.Width / rect.Height;
                        if (ratio >= 4 && ratio <= 6)
                        {
                            if (!rectsHash.Contains(rect))
                            {
                                rects.Add(rect);
                                rectsHash.Add(rect);
                            }
                        }
                        x = rect.Right;
                    }
                }
            }
            return rects;
        }

        static IEnumerable<Positioning> FindZ(Bitmap bitmap, Rectangle bounds, Color color, int sc, int mod)
        {
            List<Rectangle> horizLines = FindHorizLines(bitmap, bounds, color, sc, mod);

            for (int i = 0; i < horizLines.Count; i++)
            {
                Rectangle hl = horizLines[i];

                int x = hl.X;
                for (int j = i + 1; j < horizLines.Count; j++)
                {
                    if (horizLines[j].X != x)
                        continue;
                    Rectangle oth = horizLines[j];

                    float frac = (float)(oth.Y - hl.Y) / hl.Height;
                    if (frac >= 5.0f && frac <= 7.0f)
                    {
                        float scale = (oth.Y - hl.Y) / 6.0f;
                        if (scale >= 3.0f && oth.Height >= 3)
                        {
                            yield return new Positioning
                            {
                                scale = scale,
                                bbox = new Rectangle(x + 1, hl.Y + 1, hl.Width + (int)(4 * scale) + 1, (int)(8 * scale) + 1)
                            };
                        }
                        else
                        {
                            yield return new Positioning
                            {
                                scale = scale,
                                bbox = new Rectangle(x, hl.Y, hl.Width + (int)(4 * scale), (int)(8 * scale) + 1)
                            };
                        }
                    }
                }
            }
        }

    }
}
