using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Linq;
using MinecraftProximity.DiscordAsync;

namespace MinecraftProximity
{
    class Player
    {
        public long userId;
        public string playerName;
        public float volume;
        public byte byteVolume;

        public Player(long userId, string playerName, float volume = 1.0f)
        {
            this.userId = userId;
            this.playerName = playerName;
            this.volume = volume;
        }

        public void SetLocalVolume(float volume = 1.0f)
        {
            volume = Math.Min(Math.Max(0.0f, volume), 2.0f);
            byte byteVolume = (byte)(volume * 100.0f);
            if (byteVolume == this.byteVolume)
                return;
            VoiceManager voiceManager = Program.discord.GetVoiceManager();
            voiceManager.SetLocalVolume(userId, byteVolume);
            this.volume = volume;
            this.byteVolume = byteVolume;
        }
    }

    public class LogicClient
    {
        public VoiceLobby voiceLobby { get; protected set; }
        Dictionary<long, Player> players;

        public ICoordinateReader coordsReader { get; protected set; }

        public Coords coords { get; protected set; }
        public long serverUser { get; protected set; }
        long ownUserId;

        public UpdateRate sendCoordsRate;
        public long sendCoordsInterval;

        CancellationTokenSource cancelTransmitCoords;
        Task transmitCoordsTask;
        Instance instance;

        //ConcurrentQueue<bool> transmitsProcessing;

        //List<Player> players;

        public LogicClient(VoiceLobby voiceLobby, Instance instance)
        {
            this.voiceLobby = voiceLobby;
            this.instance = instance;

            //coordsReader = new CoordinateReader();
            coordsReader = new CoordinateReaderSharp();
            serverUser = -1;
            ownUserId = Program.currentUserId;

            sendCoordsRate = Program.configFile.GetUpdateRate("client_sendcoords", true);
            sendCoordsInterval = (long)sendCoordsRate.baseInterval.TotalMilliseconds;

            //transmitsProcessing = new ConcurrentQueue<bool>();

            this.voiceLobby.onMemberConnect += VoiceLobby_onMemberConnect;
            this.voiceLobby.onMemberDisconnect += VoiceLobby_onMemberDisconnect;
            this.voiceLobby.onNetworkJson += VoiceLobby_onNetworkJson;

            transmitCoordsTask = null;

            RefreshPlayers();

            cancelTransmitCoords = new CancellationTokenSource();
            transmitCoordsTask = DoSendCoordinatesLoop(cancelTransmitCoords.Token);
            instance.RegisterRunning("Client_SendCoordinatesLoop", transmitCoordsTask, cancelTransmitCoords);

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
                    Log.Error("[Client] Transmit coords task encountered error: {Message}", ex.Message);
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
            {
                serverUser = -1;
                return;
            }
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
                    instance.DoHost();
            }
        }

        private void VoiceLobby_onNetworkJson(long sender, byte channel, JObject jObject)
        {
            if (channel != 0 && channel != 1)
                return;

            instance.Queue("Client_HandleNetworkMessage", async () =>
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
            string type = data["type"].Value<string>();

            if (type == "setVolume")
            {
                //await SetLocalVolume(data["userId"].Value<long>(), data["volume"].Value<float>());
                long userId = data["userId"].Value<long>();
                float volume = data["volume"].Value<float>();
                if (!players.ContainsKey(userId))
                    RefreshPlayers();

                if (players.TryGetValue(userId, out Player pl))
                    pl.SetLocalVolume(volume);
            }
            else if (type == "setVolumes")
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
                        pl.SetLocalVolume(volume);
                    }
                }
            }
            else if (type == "sendCoords")
            {
                Coords? coords = await coordsReader.GetCoords();
                await SendCoordinates(coords);
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
            else if (type == "broadcastReceive")
            {
                string message = data["message"].Value<string>();
                message.Replace("\r", "");
                string[] lines = message.Split('\n');

                foreach (string line in lines)
                    Log.Information("[Broadcast] {Message}", line);
            }
            else if (type == "changeServer")
            {
                long userId = data["userId"].Value<long>();

                string username = null;
                if (voiceLobby != null)
                {
                    try
                    {
                        //username = voiceLobby.GetMembers().First(user => user.Id == userId).Username;
                        username = voiceLobby.GetMember(userId).Username;
                    }
                    catch (InvalidOperationException) { }
                }

                if (userId != Program.currentUserId && instance.server != null)
                {
                    Log.Information("[Client] Stopping own server.");
                    instance.server?.Stop();
                    instance.server = null;
                }

                serverUser = userId;

                Log.Information("[Client] Changed server to user {UserId}.", username ?? userId.ToString());
                RefreshPlayers();
            }
            else if (type == "webui")
            {
                WebUI webUI = instance.webUI;
                if (webUI != null)
                    webUI.HandleMessage(data["data"].Value<JObject>());
            }
            else if (type == "updatemap" || type == "updateplayers")
            {
                WebUI webUI = instance.webUI;
                if (webUI != null)
                    webUI.HandleMessage(data);
            }
            //else if (type == "updatemap")
            //{
            //    instance.webUI?.ReceiveUpdate(data["data"].ToString());
            //}
            //else if (type == "updateplayers")
            //{
            //    instance.webUI?.UpdatePlayers(data["data"].ToString());
            //}
            else
            {
                Log.Warning("[Client] Unknown action \"{Action}\".", type);
            }
        }

        public async Task<bool> SendCoordinates(Coords? coords)
        {
            //Coords? coords = await coordsReader.GetCoords();
            if (!coords.HasValue || serverUser == -1)
                return false;
            this.coords = coords.Value;

            JObject message = JObject.FromObject(new
            {
                type = "updateCoords",
                userId = ownUserId,
                this.coords.x,
                this.coords.y,
                this.coords.z
            });

            //transmitsProcessing.Enqueue(true);
            voiceLobby.SendNetworkJson(serverUser, 3, message);
            //transmitsProcessing.TryDequeue(out bool a);

            //Program.lobbyManager.FlushNetwork();

            await Task.CompletedTask;
            return true;
        }

        public async Task DoSendCoordinatesLoop(CancellationToken ct)
        {
            try
            {
                Log.Information("[Client] Starting send coordinates loop.");
                long ticks = Environment.TickCount64;

                TimeSpan minDelay = TimeSpan.FromMilliseconds(1);

                int submissions = 0;
                int successes = 0;
                TaskCompletionSource<bool> completionSource = null;

                RepeatProfiler profiler = new RepeatProfiler(Program.configFile.GetUpdateRate("client_sendcoords_performanceStats", false).baseInterval,
                    (RepeatProfiler.Result res) =>
                    {
                        float successRate = successes / Math.Max(1.0f, submissions);

                        Log.Information("[Client] Coordinate submission attempts: {Rate:F2} per second. Max: {maxDur} ms, avg {avgDur:F2} ms. Success: {SuccessPerc:F1}%", res.rate, res.maxDurMs, res.durMs, successRate * 100.0f);
                        submissions = 0;
                        successes = 0;
                    });

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

                        profiler.Start();

                        Coords? coords = await coordsReader.GetCoords();

                        instance.Queue("Client_SendCoordinates", async () =>
                        {
                            bool result = false;
                            try
                            {
                                result = await SendCoordinates(coords);
                            }
                            finally
                            {
                                completionSource.TrySetResult(result);
                            }
                        });
                        bool success = await completionSource.Task;

                        //bool success = await SendCoordinates();

                        submissions++;
                        if (success)
                            successes++;

                        profiler.Stop();

                        await Task.Delay(minDelay, ct);
                        //if (Environment.TickCount64 > nextStatsTickcount)
                        //{
                        //    //long timesp = Environment.TickCount64 - statsStart;
                        //    //float rate = submissions / ((float)timesp / 1000.0f);
                        //    //float successRate = successes / Math.Max(1.0f, submissions);
                        //    Log.Information("[Client] Coordinate submission attempts: {Rate:F2} per second. Max: {} ms, avg {} ms. Success: {SuccessPerc:F1}%", rate,  successRate * 100.0f);

                        //    statsStart = Environment.TickCount64;
                        //    nextStatsTickcount = statsStart + (long)statsInterval.TotalMilliseconds;
                        //    submissions = 0;
                        //    successes = 0;
                        //}

                        long nowTicks = Environment.TickCount64;
                        if (nowTicks >= ticks + sendCoordsInterval)
                        {
                            if (nowTicks > ticks + sendCoordsInterval + 2000)
                                ticks = nowTicks - sendCoordsInterval;
                            else
                                ticks += sendCoordsInterval;
                        }
                        else
                        {
                            await Task.Delay((int)(ticks + sendCoordsInterval - nowTicks), ct);
                            ticks += sendCoordsInterval;
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
