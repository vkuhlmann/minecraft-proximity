using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Threading.Tasks;
using System.Drawing.Design;
using System.Diagnostics;
using System.Text.RegularExpressions;

using Serilog;

namespace MinecraftProximity
{
    public class CoordinateReaderSharp : ICoordinateReader
    {
        Bitmap bitmap;
        Graphics graphics;
        MinecraftFontReader fontReader;
        CoordinateReadPositioner.Positioning? positioning;

        long measureStart;
        Stopwatch stopwatch;
        int requests;
        long measureEnd;
        TimeSpan measureDur;

        TimeSpan calibrateTimeout;
        long nextAllowedCalibrate;

        long nextNotCalibratedWarning;
        TimeSpan notCalibratedWarningTimeout;

        Regex coordsExtractRegex;

        int screen;
        Rectangle bounds;

        public CoordinateReaderSharp()
        {
            bitmap = new Bitmap(1920, 1080);
            graphics = Graphics.FromImage(bitmap);
            fontReader = new MinecraftFontReader();
            positioning = null;
            screen = -1;
            bounds = new Rectangle(0, 0, 1920, 1080);

            calibrateTimeout = TimeSpan.FromSeconds(10);
            nextAllowedCalibrate = Environment.TickCount64;

            nextNotCalibratedWarning = Environment.TickCount64;
            notCalibratedWarningTimeout = TimeSpan.FromSeconds(15);

            stopwatch = new Stopwatch();
            measureDur = TimeSpan.FromSeconds(5);
            measureStart = Environment.TickCount64;
            measureEnd = measureStart + (long)measureDur.TotalMilliseconds;
            requests = 0;

            coordsExtractRegex = new Regex("^\\s*(Z:)?\\s*(?<x>[+-]?\\d+(\\.\\d+)?)(\\s|\\s*/)\\s*" +
            "(?<y>[+-]?\\d+(\\.\\d+)?)(\\s|\\s*/)\\s*" +
            "(?<z>[+-]?\\d+(\\.\\d+)?).*$");
        }

        async Task RecalculateBounds()
        {
            Rectangle[] rects = await PythonManager.GetScreenRects();
            int minX = 0;
            int minY = 0;
            int maxX = 0;
            int maxY = 0;

            foreach (Rectangle r in rects)
            {
                minX = Math.Min(minX, r.X);
                minY = Math.Min(minY, r.Y);
                maxX = Math.Max(maxX, r.X + r.Width);
                maxY = Math.Max(maxY, r.Y + r.Height);
            }

            if (screen >= 0 && screen < rects.Length)
            {
                bounds = rects[screen];
                positioning = null;
                return;
            }

            //Log.Information("[CoordinateReader] Calibrating!");
            //Rectangle screenBounds = new Rectangle(0, 0, 1920, 1080);
            Rectangle screenBounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
            bounds = screenBounds;
        }

        public async Task<Coords?> GetCoords()
        {
            if (Environment.TickCount64 > measureEnd)
            {
                measureEnd = Environment.TickCount64;

                float durMs = (float)stopwatch.ElapsedMilliseconds / Math.Max(1, requests);
                //Log.Information("[CoordinateReader] Coords getting takes {DurMs:F2} ms on average ({Req} requests completed)", durMs, requests);

                stopwatch.Reset();
                requests = 0;

                measureStart = measureEnd;
                measureEnd = measureStart + (long)measureDur.TotalMilliseconds;
            }

            stopwatch.Start();

            Task<Coords?> t = Task.Run(new Func<Task<Coords?>>(async () =>
            {
                try
                {
                    if (positioning != null)
                    {
                        CoordinateReadPositioner.Positioning pos = positioning.Value;
                        graphics.CopyFromScreen(new Point(pos.bbox.X, pos.bbox.Y), new Point(0, 0), pos.bbox.Size, CopyPixelOperation.SourceCopy);

                        Coords? c = TryReadCoords(bitmap, new Point(pos.bbox.X, pos.bbox.Y));

                        if (c != null)
                            return c;
                        //return Task.FromResult(c);


                        Log.Warning("[CoordinateReader] Lost coordinates calibration");
                        nextNotCalibratedWarning = Environment.TickCount64 + (long)notCalibratedWarningTimeout.TotalMilliseconds;
                    }

                    if (Environment.TickCount64 >= nextAllowedCalibrate)
                    {
                        nextAllowedCalibrate = Environment.TickCount64 + (long)calibrateTimeout.TotalMilliseconds;

                        //Rectangle[] rects = await PythonManager.GetScreenRects();
                        //int minX = 0;
                        //int minY = 0;
                        //int maxX = 0;
                        //int maxY = 0;

                        //foreach (Rectangle r in rects)
                        //{
                        //    minX = Math.Min(minX, r.X);
                        //    minY = Math.Min(minY, r.Y);
                        //    maxX = Math.Max(maxX, r.X + r.Width);
                        //    maxY = Math.Max(maxY, r.Y + r.Height);
                        //}

                        ////Log.Information("[CoordinateReader] Calibrating!");
                        ////Rectangle screenBounds = new Rectangle(0, 0, 1920, 1080);
                        //Rectangle screenBounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
                        await RecalculateBounds();

                        Rectangle screenBounds = bounds;

                        if (bitmap.Width < screenBounds.Width || bitmap.Height < screenBounds.Height)
                        {
                            graphics.Dispose();
                            graphics = null;
                            bitmap.Dispose();
                            bitmap = null;

                            bitmap = new Bitmap(screenBounds.Width, screenBounds.Height);
                            graphics = Graphics.FromImage(bitmap);
                        }

                        graphics.CopyFromScreen(new Point(screenBounds.X, screenBounds.Y), new Point(0, 0), screenBounds.Size, CopyPixelOperation.SourceCopy);

                        //Rectangle bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                        Rectangle bitmapBounds = new Rectangle(new Point(0, 0), screenBounds.Size);

                        //bitmap.Save("testimage.png");
                        foreach (CoordinateReadPositioner.Positioning posRaw in CoordinateReadPositioner.FindPossiblePositions(bitmap,
                            bitmapBounds))
                        {
                            CoordinateReadPositioner.Positioning pos = posRaw;
                            pos.bbox.X += screenBounds.X;
                            pos.bbox.Y += screenBounds.Y;
                            positioning = pos;

                            //string s = fontReader.Read(bitmap, pos.bbox, pos.scale);
                            //Console.WriteLine($"Read [{s}]");
                            Coords? c = TryReadCoords(bitmap, new Point(screenBounds.X, screenBounds.Y));
                            if (c != null)
                            {
                                Log.Information("[CoordinateReader] Acquired coordinates calibration");
                                return c;//Task.FromResult(c);
                            }
                        }

                        nextAllowedCalibrate = Environment.TickCount64 + (long)calibrateTimeout.TotalMilliseconds;

                        if (Environment.TickCount64 > nextNotCalibratedWarning)
                        {
                            nextNotCalibratedWarning = Environment.TickCount64 + (long)notCalibratedWarningTimeout.TotalMilliseconds;
                            Log.Information("[CoordinateReader] Can't read coordinates!");
                        }
                        //bitmap.Save("testimageAnnotated.png");
                    }
                }
                finally
                {
                    requests += 1;
                    stopwatch.Stop();
                }
                return null;
            }));
            return await t;
            //return null;
            //return Task.FromResult((Coords?)null);
        }

        public Coords? TryReadCoords(Bitmap bitmap, Point bitmapTopLeft)
        {
            if (positioning == null)
                return null;

            Rectangle r = positioning.Value.bbox;
            string s = fontReader.Read(bitmap, new Rectangle(r.X - bitmapTopLeft.X, r.Y - bitmapTopLeft.Y, r.Width, r.Height), positioning.Value.scale);

            Match m = coordsExtractRegex.Match(s);
            if (!m.Success)
            {
                positioning = null;
                return null;
            }
            return new Coords
            {
                x = float.Parse(m.Groups["x"].Value),
                y = float.Parse(m.Groups["y"].Value),
                z = float.Parse(m.Groups["z"].Value)
            };
        }

        public void SetScreen(int screen)
        {
            this.screen = screen;
            this.positioning = null;
            //RecalculateBounds().Wait();
        }
    }
}
