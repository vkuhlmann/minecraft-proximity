﻿using System;
using System.Linq;
using System.Threading;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using System.Collections.Generic;
using System.Collections.Concurrent;

// Use and distribution of this program requires compliance with https://dotnet.microsoft.com/en/dotnet_library_license.htm
// As per necessary by some dependencies.

namespace discordGame
{
	class Program
	{
		public static Discord.Discord discord;
		public static Discord.LobbyManager lobbyManager;
		public static ConcurrentQueue<Func<Task>> nextTasks;
		public static CoordinateReader coordinateReader;

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

		//async static Task myFunction()
		//{
		//	Console.WriteLine("---- I");
		//	await Task.Delay(2000);
		//	//Thread.Sleep(10000);

		//	Console.WriteLine("---- II");
		//	await Task.CompletedTask;
		//}

		static VoiceLobby currentLobby = null;
		static bool createLobby = true;

		static async Task createLobbyIfNone()
		{
			if (!createLobby || currentLobby != null)
			{
				Log.Information("Not creating lobby: already exists");
				return;
			}

			//currentLobby = VoiceLobby.Create().Result;
			currentLobby = await VoiceLobby.Create();
			Log.Information("Lobby has been created");
		}

		static async Task RunAsync(string[] args)
		{
			nextTasks = new ConcurrentQueue<Func<Task>>();
			var clientID = "804643036755001365";

			Console.Write("DISCORD_INSTANCE_ID: ");
			string instanceID = Console.ReadLine();

			Environment.SetEnvironmentVariable("DISCORD_INSTANCE_ID", instanceID);

			//Task a = Task.Run(async () =>
			//{
			//	Console.WriteLine("---- Start");
			//	await myFunction();

			//	//Thread.Sleep(10000);
			//	Console.WriteLine("---- Stop");
			//	await Task.CompletedTask;
			//});
			//await a;

			//Task a = myFunction();
			//Console.WriteLine("---- A");
			//Thread.Sleep(2000);
			//Console.WriteLine("---- B");

			Discord.Discord discord;
			try
			{
				discord = new Discord.Discord(Int64.Parse(clientID), (UInt64)Discord.CreateFlags.Default);
			}
			catch (Exception ex)
			{
				Log.Fatal($"Error initializing Discord hook: {ex}");
				return;
			}
			Program.discord = discord;

			discord.SetLogHook(Discord.LogLevel.Debug, (level, message) =>
			{
				if (level == Discord.LogLevel.Error)
					Log.Error($"Discord: {message}");
				else if (level == Discord.LogLevel.Warn)
					Log.Warning($"Discord: {message}");
				else
					Log.Information($"Discord ({level}): {message}");
			});

			var activityManager = discord.GetActivityManager();
			var lobbyManager = discord.GetLobbyManager();
			Program.lobbyManager = lobbyManager;
		

			// Received when someone accepts a request to join or invite.
			// Use secrets to receive back the information needed to add the user to the group/party/match
			activityManager.OnActivityJoin += secret =>
			{
				nextTasks.Enqueue(async () =>
				{
					createLobby = false;
					if (currentLobby != null)
						await currentLobby.Disconnect();
					//await currentLobby?.Disconnect();

					//Log.Information($"Joining activity {secret}");
					currentLobby = await VoiceLobby.FromSecret(secret);
				});

				//lobbyManager.ConnectLobbyWithActivitySecret(secret, (Discord.Result result, ref Discord.Lobby lobby) =>
				//{
				//	Console.WriteLine("Connected to lobby: {0}", lobby.Id);
				//	lobbyManager.ConnectNetwork(lobby.Id);
				//	lobbyManager.OpenNetworkChannel(lobby.Id, 0, true);
				//	foreach (var user in lobbyManager.GetMemberUsers(lobby.Id))
				//	{
				//		lobbyManager.SendNetworkMessage(lobby.Id, user.Id, 0,
				//			Encoding.UTF8.GetBytes(String.Format("Hello, {0}!", user.Username)));
				//	}
				//	UpdateActivity(discord, lobby);
				//});
			};

			// A join request has been received. Render the request on the UI.
			activityManager.OnActivityJoinRequest += (ref Discord.User user) =>
			{
				//Console.WriteLine("OnJoinRequest {0} {1}", user.Id, user.Username);
				Log.Information($"User {user.Username} ({user.Id}) is requesting to join!");
			};
			// An invite has been received. Consider rendering the user / activity on the UI.
			activityManager.OnActivityInvite += (Discord.ActivityActionType Type, ref Discord.User user, ref Discord.Activity activity2) =>
			{
				Log.Information($"Received invite from {user.Username} ({user.Id}) to {Type} {activity2.Name}");
				Log.Information("The invite can be accepted from within Discord");
				//Console.WriteLine("OnInvite {0} {1} {2}", Type, user.Username, activity2.Name);
				// activityManager.AcceptInvite(user.Id, result =>
				// {
				//     Console.WriteLine("AcceptInvite {0}", result);
				// });
			};

			string path = System.Reflection.Assembly.GetEntryAssembly().Location;
			path = Path.Combine(Directory.GetParent(path).FullName, "discordGame.exe");

			string launchCommand = $"\"{path}\"";
			Log.Information($"Registering launchCommand: {launchCommand}");
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
				Log.Information("Received lobby message: {0} {1}", lobbyID, Encoding.UTF8.GetString(data));
			};
			lobbyManager.OnNetworkMessage += (lobbyId, userId, channelId, data) =>
			{
				Log.Information("Received network message: {0} {1} {2} {3}", lobbyId, userId, channelId, Encoding.UTF8.GetString(data));
			};
			lobbyManager.OnSpeaking += (lobbyID, userID, speaking) =>
			{
				Log.Information("Received lobby speaking: {0} {1} {2}", lobbyID, userID, speaking);
			};

			List<(int, Func<Task>)> scheduledTasks = new List<(int, Func<Task>)>
			{
				(2,//180,
				async () =>
				{
					if (!createLobby || currentLobby != null)
					{
						Log.Information("Not creating lobby: already exists");
						return;
					}

					Log.Information("Creating new lobby");

					//currentLobby = VoiceLobby.Create().Result;
					currentLobby = await VoiceLobby.Create();
					//Log.Information("Lobby has been created");
					//Log.Information("Blocking for 10 seconds");
					//Thread.Sleep(10000);
					//Log.Information("Done blocking");
				}
				)
			};

			//Task a = Task.Run(() =>
			//{
			//	currentLobby = VoiceLobby.Create().Result;
			//	Log.Information("Lobby has been created");
			//});

			//var overlayManager = discord.GetOverlayManager();
			////overlayManager.OnOverlayLocked += locked =>
			////{
			////	Console.WriteLine("Overlay Locked: {0}", locked);
			////};
			////overlayManager.SetLocked(false);

			//if (!overlayManager.IsEnabled())
			//{
			//	Console.WriteLine("Overlay is not enabled. Modals will be shown in the Discord client instead");
			//}

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

			coordinateReader = new CoordinateReader();

			CancellationTokenSource cancelPrintCoordsSource = new CancellationTokenSource();
			CancellationToken cancelPrintCoords = cancelPrintCoordsSource.Token;
			Task printLoop = DoPrintCoordsLoop(cancelPrintCoords);


			List<Task> runningTasks = new List<Task>();

			Log.Information("Running callback loop on {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);

			// Pump the event look to ensure all callbacks continue to get fired.
			try
			{
				Console.WriteLine("Running. Press Q to quit.");
				int frame = 0;

				while (true)
				{
					frame += 1;
					int i = 0;
					while (i < scheduledTasks.Count)
					{
						if (scheduledTasks[i].Item1 > 0)
						{
							i++;
							continue;
						}

						Task a = scheduledTasks[i].Item2(); //new Task(scheduledTasks[i].Item2);
						runningTasks.Add(a);
						//a.RunSynchronously();
						
						scheduledTasks.RemoveAt(i);
					}

					if (nextTasks.TryDequeue(out Func<Task> b))
					{
						Task c = b();
						runningTasks.Add(c);
					}

					discord.RunCallbacks();
					lobbyManager.FlushNetwork();

					for (i = 0; i < scheduledTasks.Count; i++)
						scheduledTasks[i] = (scheduledTasks[i].Item1 - 1, scheduledTasks[i].Item2);

					if (Console.KeyAvailable)
					{
						ConsoleKeyInfo key = Console.ReadKey(true);
						if (key.Key == ConsoleKey.Q)
							break;
					}

					await Task.Delay(1000 / 60);
					//if ((frame % 60) == 0)
					//	Log.Information("Ping!");

				}

				cancelPrintCoordsSource.Cancel();
				try
				{
					printLoop.Wait();
				}
				catch (Exception ex) { }

				//a.Wait();
			}
			finally
			{
				discord.Dispose();
			}

			//await a;
		}

		static async Task DoPrintCoordsLoop(CancellationToken tok)
		{
			while (true)
			{
				tok.ThrowIfCancellationRequested();
				Log.Information($"Coords are {coordinateReader.GetCoords()?.ToString() ?? "null"}");
				await Task.Delay(TimeSpan.FromSeconds(5), tok);
			}
		}

		static async Task Main(string[] args)
		{
			try
			{
				Log.Logger = new LoggerConfiguration()
					.MinimumLevel.Information()
					.WriteTo.Console()
					.CreateLogger();

				await RunAsync(args);
				Log.CloseAndFlush();
			}catch (Exception ex)
			{
				Console.WriteLine("Fatal error:");
				Console.WriteLine(ex);
				Console.WriteLine(ex.StackTrace);
				Console.WriteLine();
				Console.WriteLine("Press any key to quit");
				Console.ReadKey();
			}
		}
	}
}
