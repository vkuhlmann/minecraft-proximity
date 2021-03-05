using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MinecraftProximity
{
    public class Instance
    {
        bool isShutdownRequested;
        public ConcurrentQueue<(string name, Func<Task>)> nextTasks;
        public long currentUserId;
        public LogicClient client;
        public LogicServer server;
        public WebUI webUI;
        bool createLobby;

        public VoiceLobby currentLobby;

        public ConcurrentQueue<(string name, Task task, CancellationTokenSource cts)> runningTasks;

        long errorBunchDur;
        long errorBunchEnd;
        long errorBunchMax;
        long errorBunchCount;

        public async Task createLobbyIfNone()
        {
            if (isShutdownRequested)
                return;
            if (!createLobby || currentLobby != null)
            {
                Log.Information("Not creating lobby: already exists.");
                return;
            }

            currentLobby = await VoiceLobby.Create(this);
            if (currentLobby == null)
            {
                Log.Error("Lobby was null.");
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

        public bool TryQueueJoinLobby(string secret)
        {
            createLobby = false;
            if (currentLobby != null || isShutdownRequested)
                return false;

            var lobbyManager = Program.lobbyManager;

            Queue("JoinLobby", async () =>
            {
                currentLobby = await VoiceLobby.FromSecret(secret, this);

                if (currentLobby != null)
                    client = new LogicClient(currentLobby, this);
            });

            return true;
        }

        public void OnNetworkMessage(long lobbyId, long userId, byte channelId, byte[] data)
        {
            //if (lobbyId == currentLobby?.lobbyId)
            currentLobby.ReceiveNetworkMessage(lobbyId, userId, channelId, data);
        }

        public void OnMemberConnect(long lobbyId, long userId)
        {
            currentLobby.ReceiveMemberConnect(lobbyId, userId);
        }

        public void OnMemberDisconnect(long lobbyId, long userId)
        {
            currentLobby.ReceiveMemberDisconnect(lobbyId, userId);
        }

        public void Queue(string name, Func<Task> task)
        {
            nextTasks.Enqueue((name, task));
        }

        //public async Task Execute(string name, bool pass, Func<Task> task)

        public void RegisterRunning(string name, Task task, CancellationTokenSource cts)
        {
            runningTasks.Enqueue((name, task, cts));
        }

        private void HandleTaskError(string taskName, Exception ex)
        {
            if (ex is AggregateException aggreEx)
            {
                if (aggreEx.InnerExceptions.Count == 1)
                    ex = aggreEx.InnerExceptions[0];
                else
                    throw ex;
            }

            if (ex is TaskCanceledException)
                return;

            if (++errorBunchCount <= errorBunchMax)
                Log.Warning("Task {Name} ended with error: {Msg}\n{StackTrace}", taskName, ex.Message, ex.StackTrace);
            else if (errorBunchCount == errorBunchMax + 1)
                Log.Warning("Hiding errors, max rate has been reached.", ex.Message);
        }

        public async Task Run(string activitySecret)
        {
            createLobby = true;
            isShutdownRequested = false;
            nextTasks = new ConcurrentQueue<(string, Func<Task>)>();
            client = null;
            server = null;
            webUI = null;

            errorBunchDur = (long)TimeSpan.FromSeconds(10).TotalMilliseconds;
            errorBunchEnd = Environment.TickCount64 + errorBunchDur;
            errorBunchMax = 3;
            errorBunchCount = 0;

            var activityManager = Program.discord.GetActivityManager();
            var lobbyManager = Program.discord.GetLobbyManager();

            runningTasks = new ConcurrentQueue<(string, Task, CancellationTokenSource)>();

            List<(string name, long time, Func<Task> start)> scheduledTasks = new List<(string, long, Func<Task>)>
            {
                ("CreateLobbyIfNone", Environment.TickCount64 + 3000, async () =>
                {
                    await createLobbyIfNone();
                })
            };

            CancellationTokenSource cancelPrintCoordsSource = new CancellationTokenSource();
            CancellationToken cancelPrintCoords = cancelPrintCoordsSource.Token;
            Task printLoop = DoPrintCoordsLoop(cancelPrintCoords);

            //List<Task> runningTasks = new List<Task>
            //{
            //    printLoop,
            //    execLoop
            //};
            runningTasks.Enqueue(("PrintCoordinatesLoop", printLoop, cancelPrintCoordsSource));
            //runningTasks.Enqueue((execLoop, cancelExecLoopSource));

            Task delayingTask = Task.CompletedTask;

            //TimeSpan.FromSeconds(20),
            RepeatProfiler profiler = new RepeatProfiler(Program.configFile.GetUpdateRate("instanceloop_performanceStats", false).baseInterval,
                (RepeatProfiler.Result result) =>
                {
                    Log.Information("On mainloop. Takes {DurMs:F2} ms on average. Executed {Executed} times. Rate: {Rate} per second. Occupation: {OccupationPerc:F2}%.",
                        result.durMs, result.handledCount, result.rate, result.occupation * 100.0f);
                    Log.Information("On mainloop. minDurMs: {MinDurMs}, maxDurMs: {MaxDurMs}", result.minDurMs, result.maxDurMs);
                });

            Log.Information("\x1b[92mRunning! Enter 'quit' to quit.\x1b[0m");
            int frame = 0;

            if (activitySecret != null)
                TryQueueJoinLobby(activitySecret);
            else if (currentLobby == null)
                Log.Information("Waiting about 3 seconds before creating lobby, in case of a Join event.");

            bool isShuttingDown = false;

            while (!isShuttingDown || runningTasks.Count > 0 || nextTasks.Count > 0)
            {
                //profiler.Start();
                frame += 1;
                int i = 0;
                long currentTime = Environment.TickCount64;

                while (i < scheduledTasks.Count)
                {
                    if (scheduledTasks[i].time > currentTime)
                    {
                        i++;
                        continue;
                    }

                    nextTasks.Enqueue((scheduledTasks[0].name, scheduledTasks[i].start));

                    scheduledTasks.RemoveAt(i);
                }

                if (currentTime >= errorBunchEnd)
                {
                    if (errorBunchCount > errorBunchMax)
                        Log.Information("Showing errors again. Hid {NumErrors} errors.", errorBunchCount - errorBunchMax);

                    errorBunchCount = 0;
                    errorBunchEnd = Environment.TickCount64 + errorBunchDur;
                }

                //int cycleAround = runningTasks.Count;
                runningTasks.Enqueue(("CycleAround", null, null));

                //for (i = 0; i < cycleAround; i++)
                while (true)
                {
                    (string name, Task task, CancellationTokenSource cts) res;
                    if (!runningTasks.TryDequeue(out res) || res.name == "CycleAround")
                        break;
                    //(Task t, CancellationTokenSource cancToken) = res;
                    (string name, Task task, CancellationTokenSource cts) = res;

                    //Task t = runningTasks[i];
                    if (!task.IsCompleted)
                    {
                        if (isShutdownRequested && cts != null)
                        {
                            Log.Information("Cancelling Task {Name}.", name);
                            cts.Cancel();
                            runningTasks.Enqueue((name, task, null));
                        }
                        else
                        {
                            runningTasks.Enqueue(res);
                        }
                        continue;
                    }
                    TaskStatus st = task.Status;
                    try
                    {
                        await task;
                    }
                    catch (Exception ex)
                    {
                        HandleTaskError(name, ex);
                    }
                }


                if (delayingTask.IsCompleted)
                {
                    if (profiler.IsRunning())
                        profiler.Stop();

                    (string name, Func<Task> b) nt;
                    if (nextTasks.TryDequeue(out nt))
                    {
                        Task c = null;
                        try
                        {
                            c = nt.b();
                        }
                        catch (Exception ex)
                        {
                            HandleTaskError(nt.name, ex);
                        }

                        if (c != null)
                        {
                            delayingTask = c;
                            runningTasks.Enqueue((nt.name, c, null));
                            profiler.Start();
                        }
                    }
                    else if (isShutdownRequested && !isShuttingDown)
                    {
                        isShuttingDown = true;
                        Log.Information("[Party] Shutting down...");

                        async Task termFunc()
                        {
                            webUI?.Stop();
                            server?.Stop();
                            client?.Stop();

                            VoiceLobby l = currentLobby;
                            if (l != null)
                                await l.StartDisconnect();
                            //await currentLobby?.StartDisconnect();
                        }

                        Task t = termFunc();

                        RegisterRunning("InstanceCleanup", t, null);
                    }
                }

                await Program.discord.RunCallbacks();
                if (currentLobby != null && currentLobby.isNetworkConnected)
                    lobbyManager.FlushNetwork();

                //profiler.Stop();

                //for (i = 0; i < scheduledTasks.Count; i++)
                //    scheduledTasks[i] = (scheduledTasks[i].name, scheduledTasks[i].frame - 1, scheduledTasks[i].start);

                if (nextTasks.Count == 0 && delayingTask.IsCompleted)
                    await Task.Delay(1000 / 100);
            }
            Log.Information("[Party] Shut down.");
            Console.WriteLine();
        }

        public void DoHost()
        {
            server?.Stop();
            server = new LogicServer(currentLobby, this);
            server.AdvertiseHost();
        }

        async Task DoPrintCoordsLoop(CancellationToken tok)
        {
            TimeSpan interval = Program.configFile.GetUpdateRate("coordinatesreader_print", false).baseInterval;
            if (interval.TotalSeconds <= 0)
                return;
            while (true)
            {
                tok.ThrowIfCancellationRequested();
                TaskCompletionSource<bool> cs = new TaskCompletionSource<bool>();
                Queue("PrintCoordinates", async () =>
                {
                    bool result = false;
                    try
                    {
                        Coords? a = client?.coords;
                        Log.Information("[CoordinateReader] Coords are {PrintedCoords}.", a?.ToString() ?? "null");
                        result = true;
                    }
                    finally
                    {
                        cs.TrySetResult(result);
                    }
                    await Task.CompletedTask;
                });
                await cs.Task;
                await Task.Delay(interval, tok);
            }
        }

    }
}
