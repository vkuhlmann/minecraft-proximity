using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;

namespace MinecraftProximity
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
            BitmapData pixels = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            // Source: https://docs.microsoft.com/en-us/dotnet/api/system.drawing.bitmap.lockbits?view=dotnet-plat-ext-5.0
            int bytes = Math.Abs(pixels.Stride) * bitmap.Height;
            byte[] rgbaValues = new byte[bytes];

            System.Runtime.InteropServices.Marshal.Copy(pixels.Scan0, rgbaValues, 0, bytes);

            bitmap.UnlockBits(pixels);

            HashSet<Positioning> exploredPositions = new HashSet<Positioning>();

            foreach (Color c in new Color[] { GRAY_COLOR, ORANGE_COLOR })
            {
                int scale = 4;
                for (int mod = 0; mod < scale; mod++)
                {
                    IEnumerable<Positioning> m = FindZ(rgbaValues, pixels.Width, bounds, c, scale, mod);
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

        static int ExpandToLeft(byte[] bitmapData, int bitmapWidth, int x, int y, int height, Color color, int minX = 0)
        {
            for (int u = x - 1; u >= minX; u--)
                for (int v = y; v < y + height; v++)
                {
                    int pos = (y * bitmapWidth + x) * 4;
                    if (bitmapData[pos + 1] != color.R || bitmapData[pos + 2] != color.G || bitmapData[pos + 3] != color.B)
                        //b.GetPixel(u, v) != c)
                        return u + 1;
                }
            return minX;
        }

        static int ExpandToRight(byte[] bitmapData, int bitmapWidth, int x, int y, int height, Color color, int maxXExcl)
        {
            for (int u = x + 1; u < maxXExcl; u++)
                for (int v = y; v < y + height; v++)
                {
                    int pos = (y * bitmapWidth + x) * 4;
                    if (bitmapData[pos + 1] != color.R || bitmapData[pos + 2] != color.G || bitmapData[pos + 3] != color.B)
                        return u;
                }
            return maxXExcl;
        }

        static int ExpandToTop(byte[] bitmapData, int bitmapWidth, int x, int y, int width, Color color, int minY = 0)
        {
            for (int v = y - 1; v >= minY; v--)
                for (int u = x; u < x + width; u++)
                {
                    int pos = (y * bitmapWidth + x) * 4;
                    if (bitmapData[pos + 1] != color.R || bitmapData[pos + 2] != color.G || bitmapData[pos + 3] != color.B)
                        return v + 1;
                }
            return minY;
        }

        static int ExpandToBottom(byte[] bitmapData, int bitmapWidth, int x, int y, int width, Color color, int maxYExcl)
        {
            for (int v = y + 1; v < maxYExcl; v++)
                for (int u = x; u < x + width; u++)
                {
                    int pos = (y * bitmapWidth + x) * 4;
                    if (bitmapData[pos + 1] != color.R || bitmapData[pos + 2] != color.G || bitmapData[pos + 3] != color.B)
                        return v;
                }
            return maxYExcl;
        }

        static Rectangle ExpandToPlainColor(byte[] bitmapData, int bitmapWidth, int x, int y)
        {
            int width = 1;
            int height = 1;
            //Color color = b.GetPixel(x, y);

            int pos = (y * bitmapWidth + x) * 4;
            Color color = Color.FromArgb(bitmapData[pos + 1], bitmapData[pos + 2], bitmapData[pos + 3]);

            int farLeft = ExpandToLeft(bitmapData, bitmapWidth, x, y, height, color);
            int farRight = ExpandToRight(bitmapData, bitmapWidth, x, y, height, color, bitmapWidth);
            x = farLeft;
            width = farRight - farLeft;

            int farTop = ExpandToTop(bitmapData, bitmapWidth, x, y, width, color);
            int farBottom = ExpandToBottom(bitmapData, bitmapWidth, x, y, width, color, bitmapData.Length / (4 * bitmapWidth));
            y = farTop;
            height = farBottom - farTop;

            return new Rectangle(x, y, width, height);
        }

        static List<Rectangle> FindHorizLines(byte[] bitmapData, int bitmapWidth, Rectangle bounds, Color color, int scale, int mod)
        {
            List<Rectangle> rects = new List<Rectangle>();
            HashSet<Rectangle> rectsHash = new HashSet<Rectangle>();

            for (int y = bounds.Y + mod; y < bounds.Bottom; y += scale)
            {
                for (int x = bounds.X + mod; x < bounds.Right; x += scale)
                {
                    int pos = (y * bitmapWidth + x) * 4;
                    if (bitmapData[pos + 1] == color.R && bitmapData[pos + 2] == color.G && bitmapData[pos + 3] == color.B)
                        //bitmap.GetPixel(x, y) == color)
                    {
                        Rectangle rect = ExpandToPlainColor(bitmapData, bitmapWidth, x, y);
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

        static IEnumerable<Positioning> FindZ(byte[] bitmapData, int bitmapWidth, Rectangle bounds, Color color, int sc, int mod)
        {
            List<Rectangle> horizLines = FindHorizLines(bitmapData, bitmapWidth, bounds, color, sc, mod);

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
