using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace MinecraftProximity
{
    class RepeatProfiler
    {
        public struct Result
        {
            public float durMs;
            public float rate;
            public int handledCount;
            public float occupation;
            public long maxDurMs;
            public long minDurMs;
        }

        public TimeSpan interval;

        long measureBegin;
        long measureEnd;
        int handledCount;
        Stopwatch stopwatch;
        Action<Result> onResult;
        long elapsedAtStart;

        long maxDur;
        long minDur;

        public RepeatProfiler(TimeSpan interval, Action<Result> onResult)
        {
            this.interval = interval;
            measureBegin = Environment.TickCount64;
            measureEnd = measureBegin + (long)interval.TotalMilliseconds;
            handledCount = 0;
            stopwatch = new Stopwatch();
            elapsedAtStart = 0;

            this.onResult = onResult;
        }

        public bool IsRunning()
        {
            return stopwatch.IsRunning;
        }

        public void Start()
        {
            if (stopwatch.IsRunning)
            {
                handledCount--;
                return;
            }

            if (interval.TotalSeconds > 0 && Environment.TickCount64 > measureEnd)
            {
                measureEnd = Environment.TickCount64;

                float durMs = (float)stopwatch.ElapsedMilliseconds / Math.Max(1, handledCount);
                onResult(new Result
                {
                    durMs = durMs,
                    handledCount = handledCount,
                    rate = handledCount / ((measureEnd - measureBegin) / 1000.0f),
                    occupation = (float)stopwatch.ElapsedMilliseconds / (measureEnd - measureBegin),
                    maxDurMs = maxDur,
                    minDurMs = minDur
                });

                //Log.Information("[CoordinateReader] Coords getting takes {DurMs:F2} ms on average ({Req} requests completed)", durMs, requests);

                stopwatch.Reset();
                handledCount = 0;

                maxDur = 0;
                minDur = long.MaxValue;

                measureBegin = measureEnd;
                measureEnd = measureBegin + (long)interval.TotalMilliseconds;
            }

            elapsedAtStart = stopwatch.ElapsedMilliseconds;
            stopwatch.Start();
        }

        public void Stop()
        {
            stopwatch.Stop();
            long dur = stopwatch.ElapsedMilliseconds - elapsedAtStart;
            maxDur = Math.Max(maxDur, dur);
            minDur = Math.Min(minDur, dur);

            handledCount++;
        }
    }
}
