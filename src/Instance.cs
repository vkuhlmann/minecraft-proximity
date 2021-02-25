using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Python.Runtime;

namespace MinecraftProximity
{
    public class Instance
    {
        bool isShutdownRequested;
        public ConcurrentQueue<Func<Task>> nextTasks;
        public long currentUserId;
        public LogicClient client;
        public LogicServer server;
        public WebUI webUI;
        bool createLobby;

        public VoiceLobby currentLobby;

        public ConcurrentQueue<(Task, CancellationTokenSource)> runningTasks;

        public async Task createLobbyIfNone()
        {
            if (currentLobby != null)
            {
                Log.Information("Not creating lobby: already exists");
                return;
            }

            currentLobby = await VoiceLobby.Create(this);
            if (currentLobby == null)
            {
                Log.Error("Lobby was null");
                return;
            }

            client?.Stop();
            client = new LogicClient(currentLobby, this);
            DoHost();
        }

        public void SignalStop()
        {
            isShutdownRequested = true;
        }

        public bool TryJoinLobby(string secret)
        {
            createLobby = false;
            if (currentLobby != null)
                return false;

            var lobbyManager = Program.lobbyManager;

            nextTasks.Enqueue(async () =>
            {
                //webUI?.Stop();
                //server?.Stop();
                //client?.Stop();

                //createLobby = false;
                //if (currentLobby != null)
                //{
                //    Log.Information("Disconnecting from previous lobby");
                //    await Task.Delay(500);
                //    //await currentLobby.Disconnect();
                //    currentLobby = null;
                //}

                // await Task.Delay(2000);

                //lobbyManager.ConnectLobbyWithActivitySecret(secret, (Discord.Result result, ref Discord.Lobby lobby) =>
                //{
                //    if (result == Discord.Result.Ok)
                //    {
                //        Console.WriteLine("Connected to lobby {0}!", lobby.Id);
                //    }
                //    //isJoining = false;
                //});
                //await Task.CompletedTask;
                //await Task.Delay(5000);

                //await VoiceLobby.FromSecret(secret, this);
                currentLobby = await VoiceLobby.FromSecret(secret, this);
                //return;
                //client?.Stop();
                if (currentLobby != null)
                    client = new LogicClient(currentLobby, this);
            });

            return true;
        }

        public async Task Run(string activitySecret)
        {
            createLobby = true;
            isShutdownRequested = false;
            nextTasks = new ConcurrentQueue<Func<Task>>();
            client = null;
            server = null;
            webUI = null;

            var activityManager = Program.discord.GetActivityManager();
            var lobbyManager = Program.discord.GetLobbyManager();

            runningTasks = new ConcurrentQueue<(Task, CancellationTokenSource)>();

            List<(int, Func<Task>)> scheduledTasks = new List<(int, Func<Task>)>
            {
                (180,
                async () =>
                {
                    await createLobbyIfNone();
                }
                )
            };

            CancellationTokenSource cancelPrintCoordsSource = new CancellationTokenSource();
            CancellationToken cancelPrintCoords = cancelPrintCoordsSource.Token;
            Task printLoop = DoPrintCoordsLoop(cancelPrintCoords);

            //List<Task> runningTasks = new List<Task>
            //{
            //    printLoop,
            //    execLoop
            //};
            runningTasks.Enqueue((printLoop, cancelPrintCoordsSource));
            //runningTasks.Enqueue((execLoop, cancelExecLoopSource));

            Task delayingTask = Task.CompletedTask;

            long errorBunchDur = (long)TimeSpan.FromSeconds(10).TotalMilliseconds;
            long errorBunchEnd = Environment.TickCount64 + errorBunchDur;
            long errorBunchMax = 3;
            long errorBunchCount = 0;

            RepeatProfiler profiler = new RepeatProfiler(TimeSpan.FromSeconds(20),
                (RepeatProfiler.Result result) =>
                {
                    //Log.Information("On mainloop. Takes {DurMs:F2} ms on average. Executed {Executed} times. Rate: {Rate} per second. Occupation: {OccupationPerc:F2}%.",
                    //    result.durMs, result.handledCount, result.rate, result.occupation * 100.0f);
                    //Log.Information("On mainloop. minDurMs: {MinDurMs}, maxDurMs: {MaxDurMs}", result.minDurMs, result.maxDurMs);
                });

            Log.Information("\x1b[92mRunning! Enter 'quit' to quit.\x1b[0m");
            int frame = 0;

            if (activitySecret != null)
                TryJoinLobby(activitySecret);

            if (currentLobby == null)
                Log.Information("Waiting about 3 seconds before creating lobby, in case of a Join event.");

            while (!isShutdownRequested || runningTasks.Count > 0)
            {
                //profiler.Start();
                frame += 1;
                int i = 0;
                while (i < scheduledTasks.Count)
                {
                    if (scheduledTasks[i].Item1 > 0)
                    {
                        i++;
                        continue;
                    }

                    nextTasks.Enqueue(scheduledTasks[i].Item2);

                    //Task a = scheduledTasks[i].Item2(); //new Task(scheduledTasks[i].Item2);
                    //runningTasks.Add(a);
                    //a.RunSynchronously();

                    scheduledTasks.RemoveAt(i);
                }

                if (Environment.TickCount64 >= errorBunchEnd)
                {
                    if (errorBunchCount > errorBunchMax)
                        Log.Information("Showing errors again. Hid {NumErrors} errors", errorBunchCount - errorBunchMax);

                    errorBunchCount = 0;
                    errorBunchEnd = Environment.TickCount64 + errorBunchDur;
                }

                int cycleAround = runningTasks.Count;
                //for (i = 0; i < runningTasks.Count; i++)
                //{
                for (i = 0; i < cycleAround; i++)
                {
                    (Task, CancellationTokenSource) res;
                    if (!runningTasks.TryDequeue(out res))
                        break;
                    (Task t, CancellationTokenSource cancToken) = res;

                    //Task t = runningTasks[i];
                    if (!t.IsCompleted)
                    {
                        if (isShutdownRequested && cancToken != null)
                        {
                            cancToken.Cancel();
                            runningTasks.Enqueue((t, null));
                        }
                        else
                        {
                            runningTasks.Enqueue((t, cancToken));
                        }
                        continue;
                    }
                    TaskStatus st = t.Status;
                    try
                    {
                        //if (st != TaskStatus.RanToCompletion && st != TaskStatus.Faulted)
                        //	throw new Exception($"TaskStatus was {st}");
                        try
                        {
                            await t;
                        }
                        catch (AggregateException ex)
                        {
                            if (ex.InnerExceptions.Count == 1)
                                throw ex.InnerExceptions[0];
                            else
                                throw ex;
                        }
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        if (++errorBunchCount <= errorBunchMax)
                        {
                            Log.Warning("Task ended with error: {Msg}\n{StackTrace}", ex.Message, ex.StackTrace);
                        }
                        else if (errorBunchCount == errorBunchMax + 1)
                        {
                            Log.Warning("Hiding errors, max rate has been reached", ex.Message);
                        }
                    }
                    //runningTasks.Remove(t);
                    //i--;
                }


                if (delayingTask.IsCompleted)
                {
                    if (profiler.IsRunning())
                        profiler.Stop();
                    //runningTasks.Remove(delayingTask);

                    if (nextTasks.TryDequeue(out Func<Task> b))
                    {
                        Task c = b();
                        delayingTask = c;
                        runningTasks.Enqueue((c, null));
                        profiler.Start();
                    }
                }

                Program.discord.RunCallbacks();
                lobbyManager.FlushNetwork();

                //profiler.Stop();

                for (i = 0; i < scheduledTasks.Count; i++)
                    scheduledTasks[i] = (scheduledTasks[i].Item1 - 1, scheduledTasks[i].Item2);

                if (nextTasks.Count == 0 && delayingTask.IsCompleted)
                    await Task.Delay(1000 / 100);
            }

            webUI?.Stop();
            server?.Stop();
            client?.Stop();

            await currentLobby?.Disconnect();

            //cancelPrintCoordsSource.Cancel();
            //try
            //{
            //    printLoop.Wait();
            //}
            //catch (Exception) { }

            //cancelExecLoopSource.Cancel();
            //try
            //{
            //    execLoop.Wait();
            //}
            //catch (Exception) { }

        }

        public void DoHost()
        {
            server?.Stop();
            server = new LogicServer(currentLobby, this);
            server.AdvertiseHost();
        }

        async Task DoPrintCoordsLoop(CancellationToken tok)
        {
            while (true)
            {
                tok.ThrowIfCancellationRequested();
                TaskCompletionSource<bool> cs = new TaskCompletionSource<bool>();
                nextTasks.Enqueue(async () =>
                {
                    //Task<Coords?> t = client?.coordsReader?.GetCoords();

                    //Log.Information("[CoordinateReader] Coords are {PrintedCoords}.", (t != null ? await t : null)?.ToString() ?? "null");
                    Coords? a = client?.coords;
                    Log.Information("[CoordinateReader] Coords are {PrintedCoords}.", a?.ToString() ?? "null");

                    cs.TrySetResult(true);
                    await Task.CompletedTask;
                });
                await cs.Task;
                await Task.Delay(TimeSpan.FromSeconds(30), tok);
            }
        }

    }
}
