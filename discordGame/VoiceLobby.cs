using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using System.Threading;

namespace discordGame
{
	public class VoiceLobby
	{
		Discord.Lobby lobby;
		Discord.LobbyManager lobbyManager;

		public event Discord.LobbyManager.LobbyMessageHandler onLobbyMessage;
		public event Discord.LobbyManager.NetworkMessageHandler onNetworkMessage;
		public event Discord.LobbyManager.SpeakingHandler onSpeaking;

		public event Discord.LobbyManager.MemberConnectHandler onMemberConnect;
		public event Discord.LobbyManager.MemberDisconnectHandler onMemberDisconnect;

		public Task currentTask;
		public Discord.LobbyManager.ConnectVoiceHandler connVoiceHandler;

		VoiceLobby(Discord.Lobby lobby)
		{
			this.lobby = lobby;
			lobbyManager = Program.lobbyManager;
			currentTask = Task.CompletedTask;

			DoInit();
		}

		public static async Task<VoiceLobby> FromSecret(string secret)
		{
			Log.Information("From secret on thread {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);
			//Console.WriteLine("OnJoin {0}", secret);
			Discord.LobbyManager lobbyManager = Program.lobbyManager;

			TaskCompletionSource<VoiceLobby> completionSource = new TaskCompletionSource<VoiceLobby>();

			lobbyManager.ConnectLobbyWithActivitySecret(secret, (Discord.Result result, ref Discord.Lobby lobby) =>
			{
				Log.Information($"Connected to lobby {lobby.Id}");
				completionSource.SetResult(new VoiceLobby(lobby));
			});

			VoiceLobby result = await completionSource.Task;
			result.PrintMetadata();
			result.PrintUsers();
			return result;
		}

		public static async Task<VoiceLobby> Create()
		{
			Log.Information("Creating on thread {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);
			Discord.LobbyManager lobbyManager = Program.lobbyManager;

			// Create a lobby.
			var transaction = lobbyManager.GetLobbyCreateTransaction();
			transaction.SetCapacity(6);
			transaction.SetType(Discord.LobbyType.Private);
			transaction.SetMetadata("version", "1");

			TaskCompletionSource<Discord.Lobby?> completionSource = new TaskCompletionSource<Discord.Lobby?>();

			lobbyManager.CreateLobby(transaction, (Discord.Result result, ref Discord.Lobby lobby) =>
			{
				if (result != Discord.Result.Ok)
				{
					completionSource.SetResult(null);
					return;
				}

				Console.WriteLine("Created lobby {0} with capacity {1} and secret {2}", lobby.Id, lobby.Capacity, lobby.Secret);

				completionSource.SetResult(lobby);
			});

			Discord.Lobby? lobby = await completionSource.Task;
			if (!lobby.HasValue)
				return null;
			return new VoiceLobby(lobby.Value);
		}

		public async Task Disconnect()
		{
			Log.Information("Disconnecting on thread {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);
			Log.Information("Disconnecting from lobby {LobbyId}", lobby.Id);
			Task a = currentTask.ContinueWith((prev) =>
			{
				Program.nextTasks.Enqueue(async () =>
				{
					TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
					lobbyManager.DisconnectLobby(lobby.Id, (Discord.Result result) =>
					{
						completionSource.SetResult(result == Discord.Result.Ok);
					});
					await completionSource.Task;
					//return true;
				});
				//return true;
			});
			
			currentTask = a;
			await a;
		}

		void DoInit()
		{
			Log.Information("Doing init on thread {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);

			connVoiceHandler = (res) =>
			{
				//Console.WriteLine($"Connected to voice chat! Result was: {_}");
				Log.Information("Connected to voice chat! Result was {Result}. (Lobby {LobbyId})", res, lobby.Id);
			};

			//// Connect to voice chat.
			//lobbyManager.ConnectVoice(lobby.Id, (res) =>
			//{
			//	//Console.WriteLine($"Connected to voice chat! Result was: {_}");
			//	Log.Information("Connected to voice chat! Result was {Result}. (Lobby {LobbyId})", res, lobby.Id);
			//});

			// Connect to voice chat.
			lobbyManager.ConnectVoice(lobby.Id, connVoiceHandler);

			Log.Information("Connecting to network. (Lobby {LobbyId})", lobby.Id);
			lobbyManager.ConnectNetwork(lobby.Id);
			//Log.Information("Opening channel. (Lobby {LobbyId})", lobby.Id);
			//lobbyManager.OpenNetworkChannel(lobby.Id, 0, true);
			//Log.Information("Opened channel. (Lobby {LobbyId})", lobby.Id);

			//foreach (var user in lobbyManager.GetMemberUsers(lobby.Id))
			//{
			//	lobbyManager.SendNetworkMessage(lobby.Id, user.Id, 0,
			//		Encoding.UTF8.GetBytes($"Hello, {user.Username}!"));
			//}

			lobbyManager.OnLobbyMessage += (lobbyID, userID, data) =>
			{
				if (lobbyID == lobby.Id)
					onLobbyMessage?.Invoke(lobbyID, userID, data);
				//Console.WriteLine("Lobby message: {0} {1}", lobbyID, Encoding.UTF8.GetString(data));
			};
			lobbyManager.OnNetworkMessage += (lobbyId, userId, channelId, data) =>
			{
				if (lobbyId == lobby.Id)
					onNetworkMessage?.Invoke(lobbyId, userId, channelId, data);

				//Console.WriteLine("Network message: {0} {1} {2} {3}", lobbyId, userId, channelId, Encoding.UTF8.GetString(data));
			};
			lobbyManager.OnSpeaking += (lobbyID, userID, speaking) =>
			{
				if (lobbyID == lobby.Id)
					onSpeaking?.Invoke(lobbyID, userID, speaking);

				//Console.WriteLine("Lobby speaking: {0} {1} {2}", lobbyID, userID, speaking);
			};

			lobbyManager.OnMemberConnect += (lobbyID, userID) =>
			{
				//Console.WriteLine($"Member connected to lobby {lobbyID}: {userID}");
				if (lobbyID == lobby.Id)
					onMemberConnect?.Invoke(lobbyID, userID);
			};

			lobbyManager.OnMemberDisconnect += (lobbyID, userID) =>
			{
				//Console.WriteLine($"Member disconnected from lobby {lobbyID}: {userID}");
				if (lobbyID == lobby.Id)
					onMemberDisconnect?.Invoke(lobbyID, userID);
			};

			onMemberConnect += HandleOnMemberConnect;
			onMemberDisconnect += HandleOnMemberDisconnect;

			SetAsActivity();

			// Update activity.
			//UpdateActivity(discord, lobby);
		}

		private void HandleOnMemberConnect(long lobbyId, long userId)
		{
			Program.nextTasks.Enqueue(async () =>
			{
				//Log.Information("User {0} ({1}) has connected to the lobby! (Lobby {2})", await GetFriendlyUsername(userId), userId, lobby.Id);
				Log.Information("User {UserFriendlyName} ({UserId}) has connected to the lobby! (Lobby {LobbyId})", await GetFriendlyUsername(userId), userId, lobbyId);
				PrintUsers();
			});
			//Console.WriteLine($"Lobby {this.lobby.Id}: Connect from {userId} ({await GetFriendlyUsername(userId)})");
		}

		private void HandleOnMemberDisconnect(long lobbyId, long userId)
		{
			Program.nextTasks.Enqueue(async () =>
			{
				//Log.Information("User {0} ({1}) has disconnected from the lobby! (Lobby {2})", await GetFriendlyUsername(userId), userId, lobby.Id);
				Log.Information("User {UserFriendlyName} ({UserId}) has disconnected from the lobby. (Lobby {LobbyId})", await GetFriendlyUsername(userId), userId, lobbyId);
				PrintUsers();
			});
			//Console.WriteLine($"Lobby {this.lobby.Id}: Disconnect from {userId} ({await GetFriendlyUsername(userId)})");
		}

		public void PrintUsers()
		{
			Console.WriteLine("Lobby members:");
			foreach (var user in Program.lobbyManager.GetMemberUsers(lobby.Id))
			{
				//Console.WriteLine("lobby member: {0}", user.Username);

				Console.WriteLine($"  {user.Username}#{user.Discriminator} ({user.Id})");
			}
			Console.WriteLine();
		}

		public void PrintMetadata()
		{
			try
			{
				Console.WriteLine("Metadata:");
				int count = lobbyManager.LobbyMetadataCount(lobby.Id);
				for (int i = 0; i < count; i++)
				{
					string key = lobbyManager.GetLobbyMetadataKey(lobby.Id, i);
					string value = lobbyManager.GetLobbyMetadataValue(lobby.Id, key);
					Console.WriteLine($"  {key} = {value}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error printing lobby metadata: {ex.Message}");
			}
			Console.WriteLine();
		}

		public void SendLobbyMessage()
		{
			// Send everyone a message.
			lobbyManager.SendLobbyMessage(lobby.Id, "Hi there!", (result) =>
			{
				Console.WriteLine($"Lobby message has been sent ({result})");
			});
		}

		//void UpdateMember(string id, )

		struct UserResult
		{
			public Discord.Result result;
			public Discord.User user;

			UserResult(Discord.Result result, Discord.User user)
			{
				this.result = result;
				this.user = user;
			}

			public static async Task<UserResult> GetUser(long id)
			{
				TaskCompletionSource<UserResult> completionSource = new TaskCompletionSource<UserResult>();

				Program.discord.GetUserManager().GetUser(id, (Discord.Result result, ref Discord.User user) =>
				{
					completionSource.SetResult(new UserResult(result, user));
				});
				UserResult userResult = await completionSource.Task;

				return await Task.FromResult(userResult);
			}
		}

		public async Task<string> GetFriendlyUsername(long playerId)
		{
			Log.Information("Getting Friendly Username on thread {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);
			Log.Information("Blocking for 5 seconds");
			Thread.Sleep(5000);
			Log.Information("Done blocking");

			UserResult res = await UserResult.GetUser(playerId);
			if (res.result == Discord.Result.Ok)
			{
				return res.user.Username;
			}
			else
			{
				return $"{playerId} (Couldn't obtain username: {res.result})";
			}
			//Task<UserResult> usernameGet = UserResult.GetUser(playerId);
			//UserResult res = usernameGet.Result;
			//if (res.result == Discord.Result.Ok)
			//{
			//	return res.user.Username;
			//}
			//else
			//{
			//	return $"{playerId}";
			//}
		}

		public async Task UpdateIsDead(long playerId, bool isDead)
		{
			Discord.LobbyMemberTransaction memberTransaction = lobbyManager.GetMemberUpdateTransaction(lobby.Id, playerId);
			memberTransaction.SetMetadata("isDead", $"{isDead}");
			lobbyManager.UpdateMember(lobby.Id, playerId, memberTransaction, (result) =>
			{
				Program.nextTasks.Enqueue(async () =>
				{
					//Console.WriteLine("lobby member has been updated: {0}", lobbyManager.GetMemberMetadataValue(lobbyID, userID, "hello"));
					string playerName = await GetFriendlyUsername(playerId);
					Console.WriteLine($"Set {playerId} dead status to ${isDead}");
				});
			});
			await Task.CompletedTask;
		}

		public static void PrintAllLobbies()
		{
			Discord.LobbyManager lobbyManager = Program.lobbyManager;

			//// Search lobbies.
			//var query = lobbyManager.GetSearchQuery();
			//// Filter by a metadata value.
			////query.Filter("metadata.a", Discord.LobbySearchComparison.GreaterThan, Discord.LobbySearchCast.Number, "455");
			////query.Sort("metadata.a", Discord.LobbySearchCast.Number, "0");
			//// Only return 1 result max.
			////query.Limit(1);

			//lobbyManager.Search(query, (_) =>
			//{
			//	Console.WriteLine("Lobbies:");


			//	Console.WriteLine("search returned {0} lobbies", lobbyManager.LobbyCount());
			//	if (lobbyManager.LobbyCount() == 1)
			//	{
			//		Console.WriteLine("first lobby secret: {0}", lobbyManager.GetLobby(lobbyManager.GetLobbyId(0)).Secret);
			//	}
			//});

			Console.WriteLine("Lobbies:");

			int lobbyCount = lobbyManager.LobbyCount();
			for (int i = 0; i < lobbyCount; i++)
			{
				long lobbyId = lobbyManager.GetLobbyId(i);
				Console.WriteLine($"  {i + 1:2}: {lobbyId}");
			}
			Console.WriteLine();
		}

		public void SetAsActivity()
		{
			var activityManager = Program.discord.GetActivityManager();
			var lobbyManager = Program.discord.GetLobbyManager();

			var activity = new Discord.Activity
			{
				State = "Testing",
				Details = "Mysterious development",
				Timestamps =
					{
						Start = 5,
						End = 6,
					},
				//Assets =
				//	{
				//		LargeImage = "foo largeImageKey",
				//		LargeText = "foo largeImageText",
				//		SmallImage = "foo smallImageKey",
				//		SmallText = "foo smallImageText",
				//	},
				Party =
					{
						Id = lobby.Id.ToString(),
						Size =
							{
								CurrentSize = lobbyManager.MemberCount(lobby.Id),
								MaxSize = (int)lobby.Capacity,
							},
					},
				Secrets = {
					Join = lobbyManager.GetLobbyActivitySecret(lobby.Id),
				},
				Instance = true,
			};

			activityManager.UpdateActivity(activity, result =>
			{
				Console.WriteLine("Updated activity {0}", result);

				// Send an invite to another user for this activity.
				// Receiver should see an invite in their DM.
				// Use a relationship user's ID for this.
				// activityManager
				//   .SendInvite(
				//       364843917537050624,
				//       Discord.ActivityActionType.Join,
				//       "",
				//       inviteResult =>
				//       {
				//           Console.WriteLine("Invite {0}", inviteResult);
				//       }
				//   );
			});
		}

	}
}
