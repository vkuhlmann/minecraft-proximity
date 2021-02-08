using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace discordGame
{
	class Player
	{
		long userId;
		string playerName;
		float volume;

		public Player(long userId, string playerName, float volume = 1.0f)
		{
			this.userId = userId;
			this.playerName = playerName;
			this.volume = volume;
		}

		public async Task SetLocalVolume(float volume = 1.0f)
		{
			Discord.VoiceManager voiceManager = Program.discord.GetVoiceManager();
			voiceManager.SetLocalVolume(userId, (byte)(volume * 100));
			await Task.CompletedTask;
		}
	}

	class LogicClient
	{
		VoiceLobby voiceLobby;
		Dictionary<long, Player> players;
		
		public CoordinateReader coordsReader { get; protected set; }
		
		Coords coords;
		long serverUser;
		long ownUserId;
		public TimeSpan sendCoordsInterval { get; set; }

		CancellationTokenSource cancelTransmitCoords;
		Task transmitCoordsTask;

		//List<Player> players;

		public LogicClient(VoiceLobby voiceLobby)
		{
			this.voiceLobby = voiceLobby;
			coordsReader = new CoordinateReader();
			serverUser = -1;
			ownUserId = Program.currentUserId;
			sendCoordsInterval = TimeSpan.FromSeconds(0.25);

			this.voiceLobby.onMemberConnect += VoiceLobby_onMemberConnect;
			this.voiceLobby.onMemberDisconnect += VoiceLobby_onMemberDisconnect;
			this.voiceLobby.onNetworkJson += VoiceLobby_onNetworkJson;

			Program.nextTasks.Enqueue(async () =>
			{
				await RefreshPlayers();				
			});

			cancelTransmitCoords = new CancellationTokenSource();
			transmitCoordsTask = DoSendCoordinatesLoop(cancelTransmitCoords.Token);
		}

		public void Stop()
		{
			if (cancelTransmitCoords != null)
			{
				cancelTransmitCoords.Cancel();
				try
				{
					transmitCoordsTask.Wait();
				}
				catch (TaskCanceledException) { }
				catch (Exception ex)
				{
					Log.Error("Transmit coords task encountered error: {Message}", ex.Message);
				}
			}
		}

		private void VoiceLobby_onMemberDisconnect(long lobbyId, long userId)
		{
			Program.nextTasks.Enqueue(async () =>
			{
				await RefreshPlayers();
			});
		}

		private void VoiceLobby_onMemberConnect(long lobbyId, long userId)
		{
			Program.nextTasks.Enqueue(async () =>
			{
				await RefreshPlayers();
			});
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

		public async Task RefreshPlayers()
		{
			Dictionary<long, Player> newPlayers = new Dictionary<long, Player>();
			foreach (Discord.User user in voiceLobby.GetMembers())
			{
				Player pl = new Player(user.Id, user.Username);
				newPlayers[user.Id] = pl;
			}
			players = newPlayers;
			await Task.CompletedTask;
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
					await RefreshPlayers();

				if (players.TryGetValue(userId, out Player pl))
					await pl.SetLocalVolume(volume);
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
						await RefreshPlayers();

					if (players.TryGetValue(userId, out Player pl))
						await pl.SetLocalVolume(volume);
				}
			}
			else if (action == "sendCoords")
			{
				await SendCoordinates();
			}
			else if (action == "changeServer")
			{
				long user = data["userId"].Value<long>();

				serverUser = user;
				Log.Information("Changed server to user {UserId}", serverUser);
				await RefreshPlayers();
			}
			else
			{
				Log.Warning("Unknown action \"{Action}\"", action);
			}
		}

		public async Task<bool> SendCoordinates()
		{
			Coords? coords = coordsReader.GetCoords();
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

			Program.nextTasks.Enqueue(async () =>
			{
				voiceLobby.SendNetworkJson(serverUser, 3, message);
				await Task.CompletedTask;
			});
			await Task.CompletedTask;
			return true;
		}

		public async Task DoSendCoordinatesLoop(CancellationToken ct)
		{
			while (true)
			{
				ct.ThrowIfCancellationRequested();
				await SendCoordinates();
				await Task.Delay(sendCoordsInterval, ct);
			}
		}

		//async Task SetLocalVolume(long player, float frac)
		//{
		//	Discord.VoiceManager voiceManager = Program.discord.GetVoiceManager();
		//	voiceManager.SetLocalVolume(player, (byte)(frac * 100));
		//	await Task.CompletedTask;
		//}

		async Task SetEveryone(float volume = 1.0f)
		{
			foreach (Player pl in players.Values)
				await pl.SetLocalVolume(volume);
		}
	}
}
