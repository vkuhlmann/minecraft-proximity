using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace MinecraftProximity.DiscordAsync
{
    public class Discord : IDisposable
    {
        private global::Discord.Discord internalDiscord;
        private global::Discord.LobbyManager lobbyManager;

        private Thread thread;
        private ConcurrentQueue<Request> requests;

        private bool hasStarted;
        private Exception exception;
        private object resultLock;
        private bool isShutdownRequested;

        public bool isUseAutoFlush
        {
            get;
            private set;
        }

        private struct Request
        {
            public Action action;
            public Task task;
            public CancellationTokenSource cancel;
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


            thread = new Thread(() => DoThread(clientId, flags, out exception));
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

        private void DoThread(long clientId, ulong flags, out Exception exception)
        {
            exception = null;
            try
            {
                internalDiscord = new global::Discord.Discord(clientId, flags);
                lock (resultLock)
                {
                    hasStarted = true;
                }
                lobbyManager = internalDiscord.GetLobbyManager();

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
                            Thread.Sleep(5);
                    }

                    if (isUseAutoFlush)
                    {
                        internalDiscord.RunCallbacks();
                        lobbyManager.FlushNetwork();
                    }

                    Thread.Sleep(5);

                    nextPause += pauseInterval;
                    time = Environment.TickCount64;

                    if (nextPause + 2000 < time)
                        nextPause = time - 1000;
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
                        request.cancel?.Cancel();
                    }
                    catch (Exception) { }
                }
            }
            finally
            {
                try
                {
                    internalDiscord.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning("Error disposing Discord: {Message}", ex.Message);
                }
            }
        }

        public void RunCallbacks()
        {
            if (isUseAutoFlush)
                return;

            requests.Enqueue(new Request
            {
                action = () =>
                {
                    internalDiscord.RunCallbacks();
                }
            });
        }

        public void FlushNetwork()
        {
            if (isUseAutoFlush)
                return;

            requests.Enqueue(new Request
            {
                action = () =>
                {
                    lobbyManager.FlushNetwork();
                }
            });
        }

        void IDisposable.Dispose()
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
