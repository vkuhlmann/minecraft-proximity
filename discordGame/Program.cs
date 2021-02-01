using System;
using System.Linq;
using System.Threading;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;

namespace discordGame
{
	class Program
	{
		// Based on Discord Game DSK example:
		// discord_game_sdk.zip/examples/csharp/Program.cs
		// where discord_game_sdk.zip can be obtained from
		// https://dl-game-sdk.discordapp.net/2.5.6/discord_game_sdk.zip

		// Update user's activity for your game.
		// Party and secrets are vital.
		// Read https://discordapp.com/developers/docs/rich-presence/how-to for more details.
		static void UpdateActivity(Discord.Discord discord, Discord.Lobby lobby)
		{
			var activityManager = discord.GetActivityManager();
			var lobbyManager = discord.GetLobbyManager();

			var activity = new Discord.Activity
			{
				State = "Party status",
				Details = "What am I doing?",
				Timestamps =
					{
						Start = 5,
						End = 6,
					},
				Assets =
					{
						LargeImage = "foo largeImageKey",
						LargeText = "foo largeImageText",
						SmallImage = "foo smallImageKey",
						SmallText = "foo smallImageText",
					},
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
				Console.WriteLine("Update Activity {0}", result);

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


		static void Main(string[] args)
		{
			var clientID = "804643036755001365";

			Environment.SetEnvironmentVariable("DISCORD_INSTANCE_ID", "0");
			var discord = new Discord.Discord(Int64.Parse(clientID), (UInt64)Discord.CreateFlags.Default);
			discord.SetLogHook(Discord.LogLevel.Debug, (level, message) =>
			{
				Console.WriteLine("Log[{0}] {1}", level, message);
			});

			var activityManager = discord.GetActivityManager();
			var lobbyManager = discord.GetLobbyManager();
			// Received when someone accepts a request to join or invite.
			// Use secrets to receive back the information needed to add the user to the group/party/match
			activityManager.OnActivityJoin += secret =>
			{
				Console.WriteLine("OnJoin {0}", secret);
				lobbyManager.ConnectLobbyWithActivitySecret(secret, (Discord.Result result, ref Discord.Lobby lobby) =>
				{
					Console.WriteLine("Connected to lobby: {0}", lobby.Id);
					lobbyManager.ConnectNetwork(lobby.Id);
					lobbyManager.OpenNetworkChannel(lobby.Id, 0, true);
					foreach (var user in lobbyManager.GetMemberUsers(lobby.Id))
					{
						lobbyManager.SendNetworkMessage(lobby.Id, user.Id, 0,
							Encoding.UTF8.GetBytes(String.Format("Hello, {0}!", user.Username)));
					}
					UpdateActivity(discord, lobby);
				});
			};

			// A join request has been received. Render the request on the UI.
			activityManager.OnActivityJoinRequest += (ref Discord.User user) =>
			{
				Console.WriteLine("OnJoinRequest {0} {1}", user.Id, user.Username);
			};
			// An invite has been received. Consider rendering the user / activity on the UI.
			activityManager.OnActivityInvite += (Discord.ActivityActionType Type, ref Discord.User user, ref Discord.Activity activity2) =>
			{
				Console.WriteLine("OnInvite {0} {1} {2}", Type, user.Username, activity2.Name);
				// activityManager.AcceptInvite(user.Id, result =>
				// {
				//     Console.WriteLine("AcceptInvite {0}", result);
				// });
			};

			string path = System.Reflection.Assembly.GetEntryAssembly().Location;
			path = Path.Combine(Directory.GetParent(path).FullName, "discordGame.exe");

			string launchCommand = $"\"{path}\"";
			Console.WriteLine($"Registering launchCommand: {launchCommand}");
			activityManager.RegisterCommand(launchCommand);

			var userManager = discord.GetUserManager();
			// The auth manager fires events as information about the current user changes.
			// This event will fire once on init.
			//
			// GetCurrentUser will error until this fires once.
			userManager.OnCurrentUserUpdate += () =>
			{
				var currentUser = userManager.GetCurrentUser();
				Console.WriteLine(currentUser.Username);
				Console.WriteLine(currentUser.Id);
			};


			lobbyManager.OnLobbyMessage += (lobbyID, userID, data) =>
			{
				Console.WriteLine("lobby message: {0} {1}", lobbyID, Encoding.UTF8.GetString(data));
			};
			lobbyManager.OnNetworkMessage += (lobbyId, userId, channelId, data) =>
			{
				Console.WriteLine("network message: {0} {1} {2} {3}", lobbyId, userId, channelId, Encoding.UTF8.GetString(data));
			};
			lobbyManager.OnSpeaking += (lobbyID, userID, speaking) =>
			{
				Console.WriteLine("lobby speaking: {0} {1} {2}", lobbyID, userID, speaking);
			};


			// Create a lobby.
			var transaction = lobbyManager.GetLobbyCreateTransaction();
			transaction.SetCapacity(6);
			transaction.SetType(Discord.LobbyType.Private);
			transaction.SetMetadata("a", "123");
			transaction.SetMetadata("a", "456");
			transaction.SetMetadata("b", "111");
			transaction.SetMetadata("c", "222");

			lobbyManager.CreateLobby(transaction, (Discord.Result result, ref Discord.Lobby lobby) =>
			{
				if (result != Discord.Result.Ok)
				{
					return;
				}

				// Check the lobby's configuration.
				Console.WriteLine("lobby {0} with capacity {1} and secret {2}", lobby.Id, lobby.Capacity, lobby.Secret);

				// Check lobby metadata.
				foreach (var key in new string[] { "a", "b", "c" })
				{
					Console.WriteLine("{0} = {1}", key, lobbyManager.GetLobbyMetadataValue(lobby.Id, key));
				}

				//lobbyManager.ConnectVoice(290926798626357250, (result) =>
				//{
				//    if (result == Discord.Result.Ok)
				//    {
				//        Console.WriteLine("Voice connected!");
				//    }
				//});

				// Print all the members of the lobby.
				foreach (var user in lobbyManager.GetMemberUsers(lobby.Id))
				{
					Console.WriteLine("lobby member: {0}", user.Username);
				}

				// Send everyone a message.
				lobbyManager.SendLobbyMessage(lobby.Id, "Hello from C#!", (_) =>
				{
					Console.WriteLine("sent message");
				});

				// Update lobby.
				var lobbyTransaction = lobbyManager.GetLobbyUpdateTransaction(lobby.Id);
				lobbyTransaction.SetMetadata("d", "e");
				lobbyTransaction.SetCapacity(16);
				lobbyManager.UpdateLobby(lobby.Id, lobbyTransaction, (_) =>
				{
					Console.WriteLine("lobby has been updated");
				});

				// Update a member.
				var lobbyID = lobby.Id;
				var userID = lobby.OwnerId;
				var memberTransaction = lobbyManager.GetMemberUpdateTransaction(lobbyID, userID);
				memberTransaction.SetMetadata("hello", "there");
				lobbyManager.UpdateMember(lobbyID, userID, memberTransaction, (_) =>
				{
					Console.WriteLine("lobby member has been updated: {0}", lobbyManager.GetMemberMetadataValue(lobbyID, userID, "hello"));
				});

				// Search lobbies.
				var query = lobbyManager.GetSearchQuery();
				// Filter by a metadata value.
				query.Filter("metadata.a", Discord.LobbySearchComparison.GreaterThan, Discord.LobbySearchCast.Number, "455");
				query.Sort("metadata.a", Discord.LobbySearchCast.Number, "0");
				// Only return 1 result max.
				query.Limit(1);
				lobbyManager.Search(query, (_) =>
				{
					Console.WriteLine("search returned {0} lobbies", lobbyManager.LobbyCount());
					if (lobbyManager.LobbyCount() == 1)
					{
						Console.WriteLine("first lobby secret: {0}", lobbyManager.GetLobby(lobbyManager.GetLobbyId(0)).Secret);
					}
				});

				// Connect to voice chat.
				lobbyManager.ConnectVoice(lobby.Id, (_) =>
				{
					Console.WriteLine($"Connected to voice chat! Result was: {_}");
				});

				// Setup networking.
				lobbyManager.ConnectNetwork(lobby.Id);
				lobbyManager.OpenNetworkChannel(lobby.Id, 0, true);

				// Update activity.
				UpdateActivity(discord, lobby);
			});


			var overlayManager = discord.GetOverlayManager();
			//overlayManager.OnOverlayLocked += locked =>
			//{
			//	Console.WriteLine("Overlay Locked: {0}", locked);
			//};
			//overlayManager.SetLocked(false);

			if (!overlayManager.IsEnabled())
			{
				Console.WriteLine("Overlay is not enabled. Modals will be shown in the Discord client instead");
			}

			//if (overlayManager.IsLocked())
			//{
			//	overlayManager.SetLocked(true, (res) =>
			//	{
			//		Console.WriteLine("Input in the overlay is now accessible again");
			//	});
			//}

			//overlayManager.OpenVoiceSettings((result) =>
			//{
			//	if (result == Discord.Result.Ok)
			//	{
			//		Console.WriteLine("Overlay is open to the voice settings for your application");
			//	}
			//});

			// Pump the event look to ensure all callbacks continue to get fired.
			try
			{
				Console.WriteLine("Running. Press Q to quit.");

				while (true)
				{
					discord.RunCallbacks();
					lobbyManager.FlushNetwork();

					//discord2.RunCallbacks();

					if (Console.KeyAvailable)
					{
						ConsoleKeyInfo key = Console.ReadKey(true);
						if (key.Key == ConsoleKey.Q)
							break;
					}

					Thread.Sleep(1000 / 60);
				}
			}
			finally
			{
				discord.Dispose();
			}
		}
	}
}
