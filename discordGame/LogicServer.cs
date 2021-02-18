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

namespace discordGame
{
    using VolumesMatrix = List<(long, List<(long, float)>)>;

    class ServerPlayer
    {
        public long userId;
        public string playerName;
        public Coords? coords;

        public ServerPlayer(long userId, string playerName)
        {
            this.userId = userId;
            this.playerName = playerName;
            coords = null;
        }
    }

    class LogicServer
    {
        Dictionary<long, ServerPlayer> playersMap;
        ConcurrentQueue<List<ServerPlayer>> playersStores;

        ConcurrentQueue<VolumesMatrix> volumesStores;

        PyScope scope;
        dynamic logicServerPy;
        VoiceLobby voiceLobby;
        Task transmitTask;
        CancellationTokenSource cancelTransmitTask;
        ConcurrentQueue<bool> transmitsProcessing;

        public LogicServer(VoiceLobby voiceLobby)
        {
            Log.Information("[Server] Initializing server...");
            this.voiceLobby = voiceLobby;
            playersMap = new Dictionary<long, ServerPlayer>();
            transmitsProcessing = new ConcurrentQueue<bool>();
            playersStores = new ConcurrentQueue<List<ServerPlayer>>();
            volumesStores = new ConcurrentQueue<VolumesMatrix>();

            this.voiceLobby.onMemberConnect += VoiceLobby_onMemberConnect;
            this.voiceLobby.onMemberDisconnect += VoiceLobby_onMemberDisconnect;

            Task task = PythonManager.pythonSetupTask;
            task.Wait();

            using (Py.GIL())
            {
                scope = Py.CreateScope();
                IEnumerable<string> imports = new List<string> { "sys", "numpy", "logicserver" };
                Dictionary<string, dynamic> modules = new Dictionary<string, dynamic>();

                foreach (string import in imports)
                {
                    modules[import] = scope.Import(import);
                    //Console.WriteLine($"Imported {import}");
                }

                dynamic mod = modules["logicserver"];
                dynamic inst = mod.LogicServer.Create();
                logicServerPy = inst;
            }

            voiceLobby.onNetworkJson += VoiceLobby_onNetworkJson;

            cancelTransmitTask = new CancellationTokenSource();
            transmitTask = DoRecalcLoop(cancelTransmitTask.Token);
            Log.Information("[Server] Initialization done.");
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
                    Log.Error("Transmit task encountered error: {Message}", ex.Message);
                }
            }
        }

        private void VoiceLobby_onMemberDisconnect(long lobbyId, long userId)
        {
            RefreshPlayers();

            //Program.nextTasks.Enqueue(async () =>
            //{

            //});
        }

        private void VoiceLobby_onMemberConnect(long lobbyId, long userId)
        {
            //RefreshPlayers();
            AdvertiseHost();
        }

        private void VoiceLobby_onNetworkJson(long sender, byte channel, JObject jObject)
        {
            if (channel != 2 && channel != 3)
                return;
            switch (jObject["action"].Value<string>())
            {
                case "updateCoords":
                {
                    long userId = jObject["userId"].Value<long>();
                    if (playersMap.TryGetValue(userId, out ServerPlayer pl))
                    {
                        float x = jObject["x"].Value<float>();
                        float y = jObject["y"].Value<float>();
                        float z = jObject["z"].Value<float>();

                        pl.coords = new Coords(x, y, z);
                    }
                    else
                    {
                        Log.Warning("Unknown player {UserId}", userId);
                    }
                }
                break;

                default:
                    break;
            }
        }

        public void RefreshPlayers()
        {
            Dictionary<long, ServerPlayer> newPlayers = new Dictionary<long, ServerPlayer>();
            foreach (Discord.User user in voiceLobby.GetMembers())
            {
                ServerPlayer pl = new ServerPlayer(user.Id, user.Username);
                newPlayers[user.Id] = pl;
            }
            playersMap = newPlayers;
        }

        public void AdvertiseHost()
        {
            RefreshPlayers();

            JObject data = JObject.FromObject(new
            {
                action = "changeServer",
                userId = Program.currentUserId
            });
            foreach (ServerPlayer pl in playersMap.Values)
            {
                voiceLobby.SendNetworkJson(pl.userId, 0, data);
            }
            Log.Information("Host has been advertised");
        }

        // Run enclosed with using(Py.GIL())!
        public dynamic GetUser(ServerPlayer pl)
        {
            PyDict dict = new PyDict();
            dict["pos"] = PyObject.FromManagedObject(null);

            if (pl.coords.HasValue)
            {
                dict["pos"] = new PyDict();
                dict["pos"]["x"] = new PyFloat(pl.coords.Value.x);
                dict["pos"]["y"] = new PyFloat(pl.coords.Value.y);
                dict["pos"]["z"] = new PyFloat(pl.coords.Value.z);
            }
            dict["userId"] = new PyInt(pl.userId);
            dict["username"] = new PyString(pl.playerName);

            using (Py.GIL())
            {
                return logicServerPy.PlayerFromDict(dict);
            }
        }

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
                action = "setVolumes",
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
                if (!volumesStores.TryDequeue(out VolumesMatrix mat))
                    mat = new VolumesMatrix();
                int i = 0;

                using (Py.GIL())
                {
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

                        dynamic reprPl = GetUser(pl);

                        //Dictionary<long, float> volumes = new Dictionary<long, float>();
                        foreach (ServerPlayer oth in playersStore)
                        {
                            if (pl == oth)
                                continue;
                            dynamic reprOth = GetUser(oth);

                            li.Add((oth.userId, logicServerPy.GetVolume(reprPl, reprOth)));
                            //volumes[oth.userId] = logicServerPy.GetVolume(reprPl, reprOth);
                        }

                        //SetUserVolumes(pl, volumes);
                    }
                }
                mat.RemoveRange(i, mat.Count - i);
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
                Log.Information("Starting server loop");
                long ticks = Environment.TickCount64;
                TimeSpan sendInterval = TimeSpan.FromMilliseconds(1000 / 8);
                long sendIntervalTicks = (long)sendInterval.TotalMilliseconds;

                TimeSpan minDelay = TimeSpan.FromMilliseconds(5);

                long statsStart = Environment.TickCount64;
                TimeSpan statsInterval = TimeSpan.FromSeconds(20);
                long nextStatsTickcount = Environment.TickCount64 + (long)statsInterval.TotalMilliseconds;
                int submissions = 0;

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
                        var ans = await CalculateVolumes();

                        //Dictionary<long, float> a;
                        //SetUserVolumes(null, a.AsEnumerable().Select(it => (it.Key, it.Value)));

                        Program.nextTasks.Enqueue(async () =>
                        {
                            foreach ((long userId, List<(long, float)> li) in ans)
                                SetUserVolumes(playersMap[userId], li);
                            completionSource.TrySetResult(true);
                            await Task.CompletedTask;
                        });
                        await completionSource.Task;

                        volumesStores.Enqueue(ans);

                        submissions++;

                        await Task.Delay(minDelay, ct);
                        if (Environment.TickCount64 > nextStatsTickcount)
                        {
                            long timesp = Environment.TickCount64 - statsStart;
                            float rate = submissions / (timesp / 1000.0f);
                            Log.Information("[Server] Update rate: {Rate:F2} per second", rate);

                            statsStart = Environment.TickCount64;
                            nextStatsTickcount = statsStart + (long)statsInterval.TotalMilliseconds;
                            submissions = 0;
                        }

                        long nowTicks = Environment.TickCount64;
                        if (nowTicks > ticks + sendIntervalTicks)
                        {
                            if (nowTicks > ticks + sendIntervalTicks * 3)
                                ticks = nowTicks;
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
