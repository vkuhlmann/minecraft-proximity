using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Collections.Concurrent;

namespace MinecraftProximity.DiscordAsync
{
    using IntDiscord = global::Discord.Discord;

    public sealed class Discord : IDisposable
    {
        private IntDiscord internalDiscord;
        //private global::Discord.LobbyManager lobbyManager;

        private Thread thread;
        private ConcurrentQueue<Request> requests;

        private bool hasStarted;
        private Exception exception;
        private object resultLock;
        private bool isShutdownRequested;
        public long startedTimestamp { get; private set; }
        public int callbackCycle { get; private set; }
        public int flushNetworkCycle { get; set; }

        public LobbyManager LobbyManager { get; private set; }
        public VoiceManager VoiceManager { get; private set; }
        public ActivityManager ActivityManager { get; private set; }
        public UserManager UserManager { get; private set; }
        public OverlayManager OverlayManager { get; private set; }
        public int ManagedThreadId { get; private set; }

        public bool isUseAutoFlush
        {
            get;
            private set;
        }

        public struct Request
        {
            public Action action;
            public Action<Exception> sendError;
        }

        public Discord(long clientId, ulong flags, bool isUseAutoFlush)
        {
            internalDiscord = null;
            requests = new ConcurrentQueue<Request>();

            hasStarted = false;
            exception = null;
            resultLock = new object();
            isShutdownRequested = false;

            this.isUseAutoFlush = isUseAutoFlush;

            callbackCycle = 0;
            flushNetworkCycle = 0;


            thread = new Thread(() => DoThread(clientId, flags, out exception));
            ManagedThreadId = thread.ManagedThreadId;
            thread.Start();

            int joinMs = 200;
            int timeout = 5000;
            bool doTimeout = true;

            for (int t = 0; t < timeout || !doTimeout; t += joinMs)
            {
                if (hasStarted || thread.Join(joinMs))
                    break;
            }

            lock (resultLock)
            {
                if (hasStarted)
                    return;

                if (exception != null)
                {
                    throw exception;
                }
                else
                {
                    try
                    {
                        thread.Abort();
                    }
                    catch (Exception) { }

                    throw new Exception("Discord didn't start.");
                }
            }
        }


        public LobbyManager GetLobbyManager() => LobbyManager;
        public VoiceManager GetVoiceManager() => VoiceManager;
        public ActivityManager GetActivityManager() => ActivityManager;
        public UserManager GetUserManager() => UserManager;
        public OverlayManager GetOverlayManager() => OverlayManager;

        public void SetLogHook(global::Discord.LogLevel level, IntDiscord.SetLogHookHandler handler)
        {
            requests.Enqueue(new Request
            {
                action = () =>
                {
                    internalDiscord.SetLogHook(level, handler);
                }
            });
        }

        private void DoThread(long clientId, ulong flags, out Exception exception)
        {
            exception = null;
            try
            {
                internalDiscord = new IntDiscord(clientId, flags);
                lock (resultLock)
                {
                    hasStarted = true;
                }

                LobbyManager = new LobbyManager(this, internalDiscord.GetLobbyManager());
                VoiceManager = new VoiceManager(this, internalDiscord.GetVoiceManager());
                ActivityManager = new ActivityManager(this, internalDiscord.GetActivityManager());
                UserManager = new UserManager(this, internalDiscord.GetUserManager());
                OverlayManager = new OverlayManager(this, internalDiscord.GetOverlayManager());

                startedTimestamp = Environment.TickCount64;
            }
            catch (Exception ex)
            {
                lock (resultLock)
                {
                    exception = ex;
                }
                return;
            }
            try
            {
                long time = Environment.TickCount64;
                long pauseInterval = 1000 / 60;

                long nextPause = time + pauseInterval;

                while (!isShutdownRequested)
                {
                    while (Environment.TickCount64 < nextPause)
                    {
                        if (requests.TryDequeue(out Request request))
                            request.action();
                        else
                            Thread.Sleep(1);
                    }

                    if (isUseAutoFlush)
                    {
                        internalDiscord.RunCallbacks();
                        LobbyManager.FlushNetwork();
                    }

                    time = Environment.TickCount64;
                    nextPause = time + pauseInterval;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error on Discord thread. Stopping. Error was: {Message}\n{Stacktrace}", ex.Message, ex.StackTrace);
                exception = ex;

                while (requests.TryDequeue(out Request request))
                {
                    try
                    {
                        request.sendError(new Exception("Discord thread crashed."));
                    }
                    catch (Exception) { }
                }
            }
            finally
            {
                try
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.DISPOSE
                    });

                    internalDiscord.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning("Error disposing Discord: {Message}", ex.Message);
                }
            }
        }

        public Task RunCallbacks()
        {
            if (isUseAutoFlush)
                return Task.CompletedTask;

            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

            Queue(new Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.RUN_CALLBACKS
                    });
                    internalDiscord.RunCallbacks();
                    callbackCycle++;
                    taskCompletionSource.SetResult(true);
                },
                sendError = e => taskCompletionSource.SetException(e)
            });
            return taskCompletionSource.Task;
        }

        public void ForceDump()
        {
            Queue(new Request
            {
                action = () =>
                {
                    DebugLog.Dump(false);
                }
            });
        }

        public void Queue(Request request)
        {
            if (exception != null)
                throw new Exception("Discord thread crashed.");
            requests.Enqueue(request);
        }

        public Task FlushNetwork()
        {
            if (isUseAutoFlush)
                return Task.CompletedTask;

            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

            Queue(new Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.FLUSH_NETWORK
                    });
                    LobbyManager.FlushNetwork();
                    flushNetworkCycle++;
                    taskCompletionSource.TrySetResult(true);
                },
                sendError = ex =>
                {
                    taskCompletionSource.TrySetException(ex);
                }
            });
            return taskCompletionSource.Task;
        }

        public Task FlushRequests()
        {
            TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();

            Queue(new Request
            {
                action = () =>
                {
                    taskCompletionSource.TrySetResult(true);
                },
                sendError = ex =>
                {
                    taskCompletionSource.TrySetException(ex);
                }
            });
            return taskCompletionSource.Task;
        }

        public void Dispose()
        {
            isShutdownRequested = true;
            if (thread == null)
                return;

            int joinMs = 200;
            int timeout = 5000;
            bool doTimeout = true;

            for (int t = 0; t < timeout || !doTimeout; t += joinMs)
            {
                if (thread.Join(joinMs))
                    break;
            }
        }

    }
}
