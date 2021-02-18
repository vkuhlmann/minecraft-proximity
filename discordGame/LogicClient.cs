using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Concurrent;

namespace discordGame
{
    class Player
    {
        public long userId;
        public string playerName;
        public float volume;

        public Player(long userId, string playerName, float volume = 1.0f)
        {
            this.userId = userId;
            this.playerName = playerName;
            this.volume = volume;
        }

        public void SetLocalVolume(float volume = 1.0f)
        {
            Discord.VoiceManager voiceManager = Program.discord.GetVoiceManager();
            voiceManager.SetLocalVolume(userId, (byte)(volume * 100));
            this.volume = volume;
        }
    }

    class LogicClient
    {
        VoiceLobby voiceLobby;
        Dictionary<long, Player> players;

        public ICoordinateReader coordsReader { get; protected set; }

        Coords coords;
        long serverUser;
        long ownUserId;
        public TimeSpan sendCoordsInterval { get; set; }

        CancellationTokenSource cancelTransmitCoords;
        Task transmitCoordsTask;
        //ConcurrentQueue<bool> transmitsProcessing;

        //List<Player> players;

        public LogicClient(VoiceLobby voiceLobby)
        {
            this.voiceLobby = voiceLobby;

            //coordsReader = new CoordinateReader();
            coordsReader = new CoordinateReaderSharp();
            serverUser = -1;
            ownUserId = Program.currentUserId;
            //sendCoordsInterval = TimeSpan.FromMilliseconds(240);
            sendCoordsInterval = TimeSpan.FromMilliseconds(1000 / 12);
            //transmitsProcessing = new ConcurrentQueue<bool>();

            this.voiceLobby.onMemberConnect += VoiceLobby_onMemberConnect;
            this.voiceLobby.onMemberDisconnect += VoiceLobby_onMemberDisconnect;
            this.voiceLobby.onNetworkJson += VoiceLobby_onNetworkJson;

            transmitCoordsTask = null;

            RefreshPlayers();

            cancelTransmitCoords = new CancellationTokenSource();
            transmitCoordsTask = DoSendCoordinatesLoop(cancelTransmitCoords.Token);

            //Program.nextTasks.Enqueue(async () =>
            //{
            //	await RefreshPlayers();
            //});
        }

        public void Stop()
        {
            if (cancelTransmitCoords != null)
            {
                cancelTransmitCoords.Cancel();
                try
                {
                    try
                    {
                        transmitCoordsTask.Wait();
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
                    Log.Error("Transmit coords task encountered error: {Message}", ex.Message);
                }
            }
        }

        private void VoiceLobby_onMemberConnect(long lobbyId, long userId)
        {
            RefreshPlayers();
        }

        private void VoiceLobby_onMemberDisconnect(long lobbyId, long userId)
        {
            if (serverUser == userId)
                serverUser = -1;
            RefreshPlayers();

            if (serverUser == -1)
            {
                long smallestUserId = Program.currentUserId;
                foreach (long id in players.Keys)
                {
                    if (id == userId)
                        continue;
                    smallestUserId = Math.Min(smallestUserId, id);
                }

                if (smallestUserId == Program.currentUserId)
                    Program.DoHost();
            }
        }

        private void VoiceLobby_onNetworkJson(long sender, byte channel, JObject jObject)
        {
            if (channel != 0 && channel != 1)
                return;

            Program.nextTasks.Enqueue(async () =>
            {
                await HandleMessage(jObject);
            });
        }

        public void RefreshPlayers()
        {
            Dictionary<long, Player> newPlayers = new Dictionary<long, Player>();
            //long smallestUserId = Program.currentUserId;

            foreach (Discord.User user in voiceLobby.GetMembers())
            {
                Player pl = new Player(user.Id, user.Username);
                newPlayers[user.Id] = pl;
                //smallestUserId = Math.Min(smallestUserId, user.Id);
            }
            players = newPlayers;

            //if (serverUser == -1 && smallestUserId == Program.currentUserId)
            //    Program.DoHost();
        }

        //public async Task ReceiveNetworkMessage(byte[] message)
        //{
        //	JObject data = JObject.Parse(Encoding.UTF8.GetString(message));
        //	await HandleMessage(data);
        //}

        public async Task HandleMessage(JObject data)
        {
            string action = data["action"].Value<string>();

            if (action == "setVolume")
            {
                //await SetLocalVolume(data["userId"].Value<long>(), data["volume"].Value<float>());
                long userId = data["userId"].Value<long>();
                float volume = data["volume"].Value<float>();
                if (!players.ContainsKey(userId))
                    RefreshPlayers();

                if (players.TryGetValue(userId, out Player pl))
                    pl.SetLocalVolume(volume);
            }
            else if (action == "setVolumes")
            {
                //Log.Information("Setting volumes! {Data}", data["players"].ToString());

                foreach (JObject dat in data["players"])
                {
                    //await SetLocalVolume(data["userId"].Value<long>(), data["volume"].Value<float>());
                    long userId = dat["userId"].Value<long>();
                    float volume = dat["volume"].Value<float>();
                    if (!players.ContainsKey(userId))
                        RefreshPlayers();

                    if (players.TryGetValue(userId, out Player pl))
                    {
                        if (Math.Abs(volume - pl.volume) > 0.05f)
                            pl.SetLocalVolume(volume);
                    }
                }
            }
            else if (action == "sendCoords")
            {
                await SendCoordinates();
            }
            //else if (action == "printMessage")
            //{
            //    string sender = data["sender"].Value<string>();

            //    string message = data["message"].Value<string>();
            //    message.Replace("\r", "");
            //    string[] lines = message.Split('\n');

            //    for (string line in lines) {
            //        Log.Information("[Chat]");
            //    }
            else if (action == "broadcastReceive")
            {
                string message = data["message"].Value<string>();
                message.Replace("\r", "");
                string[] lines = message.Split('\n');

                foreach (string line in lines)
                    Log.Information("[Broadcast] {Message}", line);
            }
            else if (action == "changeServer")
            {
                long user = data["userId"].Value<long>();

                if (user != Program.currentUserId && Program.server != null)
                {
                    Log.Information("Stopping own server.");
                    Program.server?.Stop();
                    Program.server = null;
                }

                serverUser = user;


                Log.Information("Changed server to user {UserId}", serverUser);
                RefreshPlayers();
            }
            else
            {
                Log.Warning("Unknown action \"{Action}\"", action);
            }
        }

        public async Task<bool> SendCoordinates()
        {
            Coords? coords = await coordsReader.GetCoords();
            if (!coords.HasValue || serverUser == -1)
                return false;
            this.coords = coords.Value;

            JObject message = JObject.FromObject(new
            {
                action = "updateCoords",
                userId = ownUserId,
                this.coords.x,
                this.coords.y,
                this.coords.z
            });

            //transmitsProcessing.Enqueue(true);
            voiceLobby.SendNetworkJson(serverUser, 3, message);
            //transmitsProcessing.TryDequeue(out bool a);
            return true;
        }

        public async Task DoSendCoordinatesLoop(CancellationToken ct)
        {
            try
            {
                Log.Information("[Client] Starting send coordinates loop");
                long ticks = Environment.TickCount64;
                long sendIntervalTicks;

                TimeSpan minDelay = TimeSpan.FromMilliseconds(1);

                long statsStart = Environment.TickCount64;
                TimeSpan statsInterval = TimeSpan.FromSeconds(20);
                long nextStatsTickcount = Environment.TickCount64 + (long)statsInterval.TotalMilliseconds;
                int submissions = 0;
                int successes = 0;
                TaskCompletionSource<bool> completionSource = null;

                ct.Register(() =>
                {
                    TaskCompletionSource<bool> a = completionSource;
                    if (a != null)
                        a.TrySetCanceled();
                });

                try
                {
                    while (true)
                    {
                        completionSource = new TaskCompletionSource<bool>();

                        ct.ThrowIfCancellationRequested();

                        //while (transmitsProcessing.Count > 0)
                        //	await Task.Delay(10, ct);

                        Program.nextTasks.Enqueue(async () =>
                        {
                            completionSource.TrySetResult(await SendCoordinates());
                        });
                        bool success = await completionSource.Task;

                        //bool success = await SendCoordinates();

                        submissions++;
                        if (success)
                            successes++;

                        await Task.Delay(minDelay, ct);
                        if (Environment.TickCount64 > nextStatsTickcount)
                        {
                            long timesp = Environment.TickCount64 - statsStart;
                            float rate = submissions / ((float)timesp / 1000.0f);
                            float successRate = successes / Math.Max(1.0f, submissions);
                            Log.Information("[Client] Coordinate submission attempts: {Rate:F2} per second. Success: {SuccessPerc:F1}%", rate, successRate * 100.0f);

                            statsStart = Environment.TickCount64;
                            nextStatsTickcount = statsStart + (long)statsInterval.TotalMilliseconds;
                            submissions = 0;
                            successes = 0;
                        }

                        sendIntervalTicks = (long)sendCoordsInterval.TotalMilliseconds;

                        long nowTicks = Environment.TickCount64;
                        if (nowTicks >= ticks + sendIntervalTicks)
                        {
                            if (nowTicks > ticks + sendIntervalTicks * 5)
                                ticks = nowTicks - sendIntervalTicks;
                            else
                                ticks += sendIntervalTicks;
                        }
                        else
                        {
                            await Task.Delay((int)(ticks + sendIntervalTicks - nowTicks), ct);
                            ticks += sendIntervalTicks;
                        }
                        //await Task.Delay(sendCoordsInterval, ct);
                    }
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
                Log.Error("Error on send coordinates loop: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        //async Task SetLocalVolume(long player, float frac)
        //{
        //	Discord.VoiceManager voiceManager = Program.discord.GetVoiceManager();
        //	voiceManager.SetLocalVolume(player, (byte)(frac * 100));
        //	await Task.CompletedTask;
        //}

        void SetEveryone(float volume = 1.0f)
        {
            foreach (Player pl in players.Values)
                pl.SetLocalVolume(volume);
        }
    }
}
