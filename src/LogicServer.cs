using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Python.Included;
using Python.Runtime;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;

namespace MinecraftProximity
{
    using VolumesMatrix = List<(long, List<(long, float)>)>;

    public class ServerPlayer
    {
        public long userId;
        public string discordUsername;
        public string discordDiscriminator;
        public string displayName;

        public Coords? coords;
        public long coordsTimestamp;
        public long coordsRateMeasureCount;
        public float coordsRate;

        public dynamic pythonPlayer;

        public ServerPlayer(long userId, string discordUsername, string discordDiscriminator)
        {
            this.userId = userId;
            this.discordUsername = discordUsername;
            this.discordDiscriminator = discordDiscriminator;
            displayName = discordUsername;

            coords = null;
            coordsTimestamp = 0;
            coordsRate = 0.0f;
            coordsRateMeasureCount = 0;
            pythonPlayer = null;
        }
    }

    public class LogicServer
    {
        Dictionary<long, ServerPlayer> playersMap;
        //Dictionary<long, Coords?> playersCoords;

        readonly ConcurrentQueue<List<ServerPlayer>> playersStores;
        readonly ConcurrentQueue<VolumesMatrix> volumesStores;

        readonly PyScope scope;
        readonly dynamic logicServerPy;
        readonly dynamic jsonModule;

        readonly VoiceLobby voiceLobby;
        readonly Task transmitTask;
        readonly CancellationTokenSource cancelTransmitTask;
        readonly ConcurrentQueue<bool> transmitsProcessing;

        readonly RepeatProfiler calculateVolumesProfiler;
        bool isSettingMap;
        readonly Instance instance;
        delegate void DelegateSendMessageHandler(long recipient, dynamic msg);

        DelegateSendMessageHandler sendMessageHandler;

        long coordsRateMeasureStart;
        TimeSpan coordsRateMeasureDur;
        long coordsRateMeasureDurMs;

        public LogicServer(VoiceLobby voiceLobby, Instance instance)
        {
            isSettingMap = false;
            this.instance = instance;
            //coordsRateMeasureDur = TimeSpan.FromSeconds(2);
            coordsRateMeasureDur = Program.configFile.GetUpdateRate("server_coordinatesRate_measure", true).baseInterval;
            coordsRateMeasureDurMs = (long)coordsRateMeasureDur.TotalMilliseconds;

            Log.Information("[Server] Initializing server...");
            this.voiceLobby = voiceLobby;
            playersMap = new Dictionary<long, ServerPlayer>();
            transmitsProcessing = new ConcurrentQueue<bool>();
            playersStores = new ConcurrentQueue<List<ServerPlayer>>();
            volumesStores = new ConcurrentQueue<VolumesMatrix>();

            calculateVolumesProfiler = new RepeatProfiler(Program.configFile.GetUpdateRate("server_calculate_performanceStats", false).baseInterval,
                (RepeatProfiler.Result result) =>
                {
                    Log.Information("[Server] Calculate volumes takes {DurMs:F2} ms on average ({Req} requests completed)", result.durMs, result.handledCount);
                });

            sendMessageHandler = SendMessageHandler;
            //(long recipient, dynamic msg) => this.SendMessageHandler(recipient, msg);

            Task task = PythonManager.pythonSetupTask;
            task.Wait();

            Exception exception = null;
            using (Py.GIL())
            {
                try
                {
                    scope = Py.CreateScope();
                    IEnumerable<string> imports = new List<string> { "json", "logicserver" };
                    Dictionary<string, dynamic> modules = new Dictionary<string, dynamic>();

                    foreach (string import in imports)
                        modules[import] = scope.Import(import);

                    jsonModule = modules["json"];

                    dynamic mod = modules["logicserver"];
                    logicServerPy = mod.create_server(sendMessageHandler);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    Log.Error("[Server] Error initializing Python code: {Ex}", ex);
                }
            }
            if (exception != null)
                throw exception;

            this.voiceLobby.onMemberConnect += VoiceLobby_onMemberConnect;
            this.voiceLobby.onMemberDisconnect += VoiceLobby_onMemberDisconnect;

            IEnumerable<Discord.User> users = voiceLobby.GetMembers();

            instance.Queue("JoinAllMembers", () =>
            {
                foreach (Discord.User user in users)
                    OnUserJoin(user.Id);
                return null;
            });

            voiceLobby.onNetworkJson += VoiceLobby_onNetworkJson;

            cancelTransmitTask = new CancellationTokenSource();
            transmitTask = DoRecalcLoop(cancelTransmitTask.Token);
            instance.RegisterRunning("ServerTransmit", transmitTask, cancelTransmitTask);

            Log.Information("[Server] Initialization done.");
        }

        public void SendMessageHandler(long recipient, dynamic msg)
        {
            string msgString;
            using (Py.GIL())
            {
                msgString = jsonModule.dumps(msg);
                msg = null;
            }

            voiceLobby.SendNetworkJson(recipient, 0, JObject.Parse(msgString));
        }

        public void Stop()
        {
            if (cancelTransmitTask != null)
            {
                cancelTransmitTask.Cancel();
                try
                {
                    try
                    {
                        transmitTask.Wait();
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
                    Log.Error("[Server] Transmit task encountered error: {Message}", ex.Message);
                }
            }

            try
            {
                using (Py.GIL())
                {
                    logicServerPy.shutdown();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Server] Python raised an error trying to do Shutdown: {Message}", ex.Message);
            }
        }

        public bool HandleCommand(string cmdName, string args)
        {
            using (Py.GIL())
            {
                try
                {
                    if (logicServerPy == null)
                        return false;
                    dynamic ans = logicServerPy.handle_command(cmdName, args);
                    PyObject obj = ans;

                    if (obj != null)
                    {
                        if (PyString.IsStringType(obj))
                        {
                            Console.WriteLine(obj.As<string>());
                        }
                        else if (!obj.IsNone() && !obj.IsTrue())
                        {
                            return false;
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error("[Server] Python raised an error during handle_command: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
                    return false;
                }
            }
        }

        private void VoiceLobby_onMemberDisconnect(long lobbyId, long userId)
        {
            OnUserLeave(userId);
        }

        private void OnUserLeave(long userId)
        {
            if (!playersMap.TryGetValue(userId, out ServerPlayer pl))
                return;

            using (Py.GIL())
            {
                try
                {
                    logicServerPy.on_leave(pl.pythonPlayer);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Server] Python raised an error during on_leave: {Message}", ex.Message);
                }
            }

            playersMap.Remove(userId);
        }

        private void VoiceLobby_onMemberConnect(long lobbyId, long userId)
        {
            //instance.Queue("OnUserJoin", () =>
            //{
            //    OnUserJoin(userId);
            //    return null;
            //});
        }

        private void OnUserJoin(long userId)
        {
            if (playersMap.ContainsKey(userId))
            {
                Log.Information("Received join request from user who is already registered!");
                return;
            }

            Discord.User user = voiceLobby.GetMember(userId);
            if (user.Username == null)
                return;

            ServerPlayer pl = new ServerPlayer(userId, user.Username, user.Discriminator);
            playersMap[userId] = pl;

            AdvertiseHost();

            using (Py.GIL())
            {
                try
                {
                    PyDict dict = new PyDict();
                    dict["pos"] = PyObject.FromManagedObject(null);

                    dict["userId"] = new PyLong(userId);
                    dict["discordUsername"] = new PyString(pl.discordUsername);
                    dict["discordDiscriminator"] = new PyString(pl.discordDiscriminator);
                    dict["displayName"] = new PyString(pl.displayName);

                    dynamic player = logicServerPy.on_join(dict);
                    pl.pythonPlayer = player;
                }
                catch (Exception ex)
                {
                    Log.Warning("[Server] Python raised an error during on_join: {Message}", ex.Message);
                    playersMap.Remove(userId);
                }
            }
        }

        private void VoiceLobby_onNetworkJson(long sender, byte channel, JObject jObject)
        {
            if (channel != 2 && channel != 3)
                return;
            string type = jObject["type"].Value<string>();

            switch (type)
            {
                case "join":
                {
                    Log.Information("Received network join from {UserId}", sender);

                    instance.Queue("OnUserJoin", () =>
                    {
                        OnUserJoin(sender);
                        return null;
                    });
                }
                break;

                case "updateCoords":
                {
                    long userId = jObject["userId"].Value<long>();
                    if (playersMap.TryGetValue(userId, out ServerPlayer pl))
                    {
                        float x = jObject["x"].Value<float>();
                        float y = jObject["y"].Value<float>();
                        float z = jObject["z"].Value<float>();

                        pl.coords = new Coords(x, y, z);
                        pl.coordsTimestamp = Environment.TickCount64;
                        pl.coordsRateMeasureCount++;
                    }
                    else
                    {
                        Log.Warning("Unknown player {UserId}", userId);
                    }
                }
                break;

                //case "updatemap":
                //{
                //    SetMap(jObject["data"].Value<JObject>());
                //}
                //break;

                default:
                {
                    string msgData = jObject.ToString();
                    List<JObject> replies = new List<JObject>();

                    using (Py.GIL())
                    {
                        //PyObject s = PyObject.FromManagedObject(
                        //    new
                        //    {
                        //        isLocal = sender == Program.currentUserId,
                        //        userId = sender
                        //    });
                        PyDict s = new PyDict();

                        s["isLocal"] = (sender == Program.currentUserId) ? scope.Eval("True") : scope.Eval("False");
                        s["userId"] = new PyLong(sender);

                        try
                        {
                            dynamic data = jsonModule.loads(msgData);

                            dynamic result = logicServerPy.on_message(type, data, s);
                            //Log.Information("Reply type: {Type}", result?.GetType());

                            PyObject obj = result;
                            if (obj != null)
                            {
                                if (PySequence.IsSequenceType(obj))
                                {
                                    PyList replyList = PyList.AsList(result);
                                    foreach (dynamic msg in replyList)
                                    {
                                        string strReply = jsonModule.dumps(msg);
                                        replies.Add(JObject.Parse(strReply));
                                    }
                                }
                                else if (PyDict.IsDictType(obj))
                                {
                                    string strReply = jsonModule.dumps(result);

                                    replies.Add(JObject.Parse(strReply));
                                }
                                else if (!obj.IsNone() && !obj.IsTrue())
                                {
                                    Log.Warning("Unknown message type: {Type}.", type);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("[Server] Python raised an error during on_message: {Message}\n{Stacktrace}", ex.Message, ex.StackTrace);
                        }
                    }

                    foreach (JObject msg in replies)
                        voiceLobby.SendNetworkJson(sender, 0, msg);
                }
                break;
            }
        }

        private void SetMap(JObject data)
        {
            if (isSettingMap)
                return;
            isSettingMap = true;
            try
            {
                using (Py.GIL())
                {
                    if (logicServerPy == null)
                        return;

                    logicServerPy.clear_obscurations();
                    logicServerPy.add_density_map(data.ToString());
                }

                JObject message = JObject.FromObject(new
                {
                    type = "updatemap",
                    data = data
                });

                foreach (ServerPlayer pl in playersMap.Values)
                {
                    voiceLobby.SendNetworkJson(pl.userId, 0, message);
                }
            }
            finally
            {
                isSettingMap = false;
            }
        }

        //public void RefreshPlayers()
        //{
        //    Dictionary<long, ServerPlayer> newPlayers = new Dictionary<long, ServerPlayer>();
        //    foreach (Discord.User user in voiceLobby.GetMembers())
        //    {
        //        if (playersMap.ContainsKey(user.Id))
        //        {
        //            newPlayers[user.Id] = playersMap[user.Id];
        //        }
        //        else
        //        {
        //            ServerPlayer pl = new ServerPlayer(user.Id, user.Username);
        //            using (Py.GIL())
        //                pl.pythonPlayer = GetUser(pl);

        //            newPlayers[user.Id] = pl;
        //        }
        //    }
        //    playersMap = newPlayers;
        //}

        public void AdvertiseHost()
        {
            //RefreshPlayers();

            JObject data = JObject.FromObject(new
            {
                type = "changeServer",
                userId = Program.currentUserId
            });
            foreach (ServerPlayer pl in playersMap.Values)
                voiceLobby.SendNetworkJson(pl.userId, 0, data);

            Log.Information("[Server] Host has been advertised");
        }

        //// Run enclosed with using(Py.GIL())!
        //public dynamic GetUser(ServerPlayer pl)
        //{
        //    PyDict dict = new PyDict();
        //    dict["pos"] = PyObject.FromManagedObject(null);

        //    if (pl.coords.HasValue)
        //    {
        //        dict["pos"] = new PyDict();
        //        dict["pos"]["x"] = new PyFloat(pl.coords.Value.x);
        //        dict["pos"]["y"] = new PyFloat(pl.coords.Value.y);
        //        dict["pos"]["z"] = new PyFloat(pl.coords.Value.z);
        //    }
        //    dict["userId"] = new PyLong(pl.userId);
        //    dict["username"] = new PyString(pl.playerName);

        //    //using (Py.GIL())
        //    //{
        //    //return logicServerPy.PlayerFromDict(dict);
        //    return logicServerPy.create_player(dict);
        //    //}
        //}

        public void SetUserVolumes(ServerPlayer target, IEnumerable<(long, float)> volumes)
        {
            JArray playersData = new JArray();
            foreach ((long userId, float volume) in volumes)
            {
                playersData.Add(JObject.FromObject(new
                {
                    userId = userId,
                    volume = volume
                }));
            }

            JObject data = JObject.FromObject(new
            {
                type = "setVolumes",
                players = playersData
            });

            //transmitsProcessing.Enqueue(true);
            //Program.nextTasks.Enqueue(async () =>
            //{
            voiceLobby.SendNetworkJson(target.userId, 1, data);
            //transmitsProcessing.TryDequeue(out bool a);
            //});
        }

        public async Task<VolumesMatrix> CalculateVolumes()
        {
            bool ratesUpdated = false;

            if (Environment.TickCount64 >= coordsRateMeasureStart + coordsRateMeasureDurMs)
            {
                foreach (ServerPlayer pl in playersMap.Values)
                {
                    pl.coordsRate = pl.coordsRateMeasureCount / (coordsRateMeasureDurMs / 1000.0f);
                    pl.coordsRateMeasureCount = 0;
                }

                ratesUpdated = true;
                coordsRateMeasureStart = Environment.TickCount64;
            }

            if (ratesUpdated)
            {
                using (Py.GIL())
                {
                    foreach (ServerPlayer pl in playersMap.Values)
                    {
                        dynamic reprPl = pl.pythonPlayer;
                        if (reprPl == null)
                            continue;
                        reprPl.set_coords_rate(pl.coordsRate);
                    }
                    logicServerPy.on_coords_rates_updated();
                }
            }

            List<ServerPlayer> playersStore;
            if (!playersStores.TryDequeue(out playersStore))
                playersStore = new List<ServerPlayer>();
            playersStore.Clear();
            playersStore.AddRange(playersMap.Values);

            //foreach ((long key, ref ServerPlayer value) in playersMap)
            //{
            //	playersMapSwap[key] = value;
            //}
            //while (playersMapSwap.Keys.Except(playersMap.Keys).FirstOrDefault() != 0)
            //{

            //}

            var t = Task.Run(new Func<VolumesMatrix>(() =>
            {
                calculateVolumesProfiler.Start();

                if (!volumesStores.TryDequeue(out VolumesMatrix mat))
                    mat = new VolumesMatrix();
                int i = 0;

                using (Py.GIL())
                {
                    foreach (ServerPlayer pl in playersStore)
                    {
                        dynamic reprPl = pl.pythonPlayer;
                        if (reprPl == null)
                            continue;

                        if (pl.coords.HasValue)
                        {
                            if (pl.coordsTimestamp >= Environment.TickCount64 - 2000)
                                reprPl.set_position(pl.coords.Value.x, pl.coords.Value.y, pl.coords.Value.z);
                            else
                                reprPl.on_position_unknown();
                        }
                    }

                    //IEnumerable<ServerPlayer> players = playersMap.Values;
                    foreach (ServerPlayer pl in playersStore)
                    {
                        List<(long, float)> li;
                        if (i < mat.Count)
                        {
                            li = mat[i].Item2;
                            li.Clear();
                            mat[i] = (pl.userId, li);
                        }
                        else
                        {
                            li = new List<(long, float)>();
                            mat.Add((pl.userId, li));
                        }
                        i += 1;

                        //dynamic reprPl = GetUser(pl);
                        dynamic reprPl = pl.pythonPlayer;
                        if (reprPl == null)
                            continue;

                        //Dictionary<long, float> volumes = new Dictionary<long, float>();
                        foreach (ServerPlayer oth in playersStore)
                        {
                            if (pl == oth)
                                continue;
                            //dynamic reprOth = GetUser(oth);
                            dynamic reprOth = oth.pythonPlayer;
                            if (reprOth == null)
                                continue;

                            try
                            {
                                li.Add((oth.userId, logicServerPy.get_volume(reprPl, reprOth)));
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"Error getting volume: {ex.Message}");
                                throw ex;
                            }

                            //volumes[oth.userId] = logicServerPy.GetVolume(reprPl, reprOth);
                        }

                        //SetUserVolumes(pl, volumes);
                    }
                }
                mat.RemoveRange(i, mat.Count - i);

                calculateVolumesProfiler.Stop();
                return mat;
            }));

            var r = await t;
            playersStores.Enqueue(playersStore);
            return r;
        }

        public async Task DoRecalcLoop(CancellationToken ct)
        {
            try
            {
                Log.Information("[Server] Starting server loop");
                long ticks = Environment.TickCount64;
                //TimeSpan sendInterval = TimeSpan.FromMilliseconds(1000 / 10);
                TimeSpan sendInterval = Program.configFile.GetUpdateRate("server_calculate", false).baseInterval;
                long sendIntervalTicks = (long)sendInterval.TotalMilliseconds;

                TimeSpan minDelay = TimeSpan.FromMilliseconds(5);

                RepeatProfiler stats = new RepeatProfiler(Program.configFile.GetUpdateRate("server_calculate_performanceStats", false).baseInterval,//TimeSpan.FromSeconds(20),
                    (RepeatProfiler.Result result) =>
                    {
                        // Calculate volumes takes {DurMs:F2} ms on average ({Req} requests completed)", result.durMs, result.handledCount);

                        //Log.Information("[Server] Update rate: {Rate:F2} per second (update takes {DurMs:F2} ms on average, {OccupationPerc:F2}%)", result.rate, result.durMs, result.occupation * 100.0f);
                        //Log.Information("[Server] Update rate: {Rate:F2} per second", result.rate);
                        Log.Information("[Server] Update rate: {Rate:F2} per second. Each calculations takes {DurMs:F2} ms on average.", result.rate, result.durMs);
                    });

                TaskCompletionSource<bool> completionSource = null;
                ct.Register(() =>
                {
                    TaskCompletionSource<bool> a = completionSource;
                    if (a != null)
                        a.TrySetCanceled();
                });

                int executed = 0;
                int frame = 0;

                try
                {
                    while (true)
                    {
                        completionSource = new TaskCompletionSource<bool>();
                        ct.ThrowIfCancellationRequested();

                        //while (transmitsProcessing.Count > 0)
                        //	await Task.Delay(10, ct);

                        stats.Start();
                        var ans = await CalculateVolumes();
                        stats.Stop();

                        //Dictionary<long, float> a;
                        //SetUserVolumes(null, a.AsEnumerable().Select(it => (it.Key, it.Value)));

                        instance.Queue("Server_TickSend", async () =>
                        {
                            bool result = false;

                            try
                            {
                                //stats.Start();
                                foreach ((long userId, List<(long, float)> li) in ans)
                                {
                                    if (playersMap.TryGetValue(userId, out ServerPlayer player))
                                        SetUserVolumes(player, li);
                                }

                                frame++;
                                if (frame % 5 == 1)
                                {
                                    JArray playersData = new JArray();
                                    //foreach ((long userId, float volume) in Program.server)
                                    foreach (ServerPlayer pl in playersMap.Values)
                                    {
                                        string plStatus = "Status unknown";
                                        if (pl.coordsTimestamp < Environment.TickCount64 - 1000)
                                            plStatus = "Coords unknown";
                                        else
                                            plStatus = $"Coords: {pl.coordsRate:F2} u/s";

                                        playersData.Add(JObject.FromObject(new
                                        {
                                            name = pl.displayName,
                                            x = pl.coords?.x ?? 0.0f,
                                            y = pl.coords?.y ?? 0.0f,
                                            z = pl.coords?.z ?? 0.0f,
                                            status = plStatus
                                        }));
                                    }

                                    JObject message = JObject.FromObject(new
                                    {
                                        type = "updateplayers",
                                        data = playersData
                                    });

                                    foreach (ServerPlayer pl in playersMap.Values)
                                    {
                                        voiceLobby.SendNetworkJson(pl.userId, 0, message);
                                    }
                                    //Log.Information("Sent player position update");
                                }

                                //Program.lobbyManager.FlushNetwork();

                                executed++;
                                result = true;
                                await Task.CompletedTask;
                            }
                            finally
                            {
                                completionSource.TrySetResult(result);
                            }
                            //stats.Stop();

                            //transmitsProcessing.Enqueue(true);
                            //Program.nextTasks.Enqueue(async () =>
                            //{
                        });

                        await completionSource.Task;

                        volumesStores.Enqueue(ans);

                        //stats.Stop();

                        await Task.Delay(minDelay, ct);
                        //if (Environment.TickCount64 > nextStatsTickcount)
                        //{
                        //    long timesp = Environment.TickCount64 - statsStart;
                        //    float rate = submissions / (timesp / 1000.0f);
                        //    Log.Information("[Server] Update rate: {Rate:F2} per second", rate);

                        //    statsStart = Environment.TickCount64;
                        //    nextStatsTickcount = statsStart + (long)statsInterval.TotalMilliseconds;
                        //    submissions = 0;
                        //}

                        executed--;

                        long nowTicks = Environment.TickCount64;
                        if (nowTicks > ticks + sendIntervalTicks)
                        {
                            if (nowTicks > ticks + 2000)
                                ticks = nowTicks - sendIntervalTicks;
                            else
                                ticks += sendIntervalTicks;
                        }
                        else
                        {
                            await Task.Delay((int)(ticks + sendIntervalTicks - nowTicks), ct);
                            ticks += sendIntervalTicks;
                        }
                        //await Task.Delay(TimeSpan.FromSeconds(0.24), ct);
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
                Log.Error("Error on server loop: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

    }
}
