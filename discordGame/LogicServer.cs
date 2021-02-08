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

namespace discordGame
{
	class ServerPlayer
	{
		public long userId;
		public string playerName;
		public Coords? coords;

		public ServerPlayer(long userId, string playerName)
		{
			this.userId = userId;
			this.playerName = playerName;
			this.coords = null;
		}
	}

	class LogicServer
	{
		Dictionary<long, ServerPlayer> playersMap;

		PyScope scope;
		dynamic logicServerPy;
		VoiceLobby voiceLobby;
		Task transmitTask;
		CancellationTokenSource cancelTransmitTask;

		public LogicServer(VoiceLobby voiceLobby)
		{
			this.voiceLobby = voiceLobby;
			playersMap = new Dictionary<long, ServerPlayer>();

			this.voiceLobby.onMemberConnect += VoiceLobby_onMemberConnect;
			this.voiceLobby.onMemberDisconnect += VoiceLobby_onMemberDisconnect;

			Task task = Program.pythonSetupTask;
			task.Wait();

			using (Py.GIL())
			{
				scope = Py.CreateScope();
				IEnumerable<string> imports = new List<string> { "sys", "numpy", "logicserver" };
				Dictionary<string, dynamic> modules = new Dictionary<string, dynamic>();

				foreach (string import in imports)
				{
					modules[import] = scope.Import(import);
					Console.WriteLine($"Imported {import}");
				}

				dynamic mod = modules["logicserver"];
				dynamic inst = mod.LogicServer.Create();
				logicServerPy = inst;
			}

			voiceLobby.onNetworkJson += VoiceLobby_onNetworkJson;

			cancelTransmitTask = new CancellationTokenSource();
			transmitTask = DoRecalcLoop(cancelTransmitTask.Token);

		}

		public void Stop()
		{
			if (cancelTransmitTask != null)
			{
				cancelTransmitTask.Cancel();
				try
				{
					transmitTask.Wait();
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

		public void SetUserVolumes(ServerPlayer target, Dictionary<long, float> volumes)
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

			Program.nextTasks.Enqueue(async () =>
			{
				voiceLobby.SendNetworkJson(target.userId, 1, data);
				await Task.CompletedTask;
			});
		}

		public async Task DoRecalcLoop(CancellationToken ct)
		{
			try
			{
				Log.Information("Starting server loop");
				while (true)
				{
					ct.ThrowIfCancellationRequested();
					using (Py.GIL())
					{
						IEnumerable<ServerPlayer> players = playersMap.Values;
						foreach (ServerPlayer pl in players)
						{
							dynamic reprPl = GetUser(pl);

							Dictionary<long, float> volumes = new Dictionary<long, float>();
							foreach (ServerPlayer oth in players)
							{
								if (pl == oth)
									continue;
								dynamic reprOth = GetUser(oth);

								volumes[oth.userId] = logicServerPy.GetVolume(reprPl, reprOth);
							}

							SetUserVolumes(pl, volumes);
						}
					}
					await Task.Delay(TimeSpan.FromSeconds(0.24), ct);
				}
			}
			catch (TaskCanceledException ex)
			{
				throw ex;
			}
			catch(Exception ex)
			{
				Log.Error("Error on server loop: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
			}
		}

	}
}
