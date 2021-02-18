using System;
using System.Linq;
using System.Threading;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using System.Collections.Generic;
using System.Collections.Concurrent;

using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Use and distribution of this program requires compliance with https://dotnet.microsoft.com/en/dotnet_library_license.htm
// As per necessary by some dependencies.

namespace discordGame
{
    class Program
    {
        public static Discord.Discord discord;
        public static Discord.LobbyManager lobbyManager;
        public static ConcurrentQueue<Func<Task>> nextTasks;
        //public static CoordinateReader coordinateReader;
        public static long currentUserId;
        public static LogicClient client;
        public static LogicServer server;

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
        static bool isQuitRequested = false;

        static async Task createLobbyIfNone()
        {
            if (!createLobby || currentLobby != null)
            {
                Log.Information("Not creating lobby: already exists");
                return;
            }

            //currentLobby = VoiceLobby.Create().Result;
            currentLobby = await VoiceLobby.Create();
            if (currentLobby == null)
            {
                Log.Error("Lobby was null");
                return;
            }

            client?.Stop();
            client = new LogicClient(currentLobby);
            DoHost();
        }


        static async Task RunAsync(string[] args)
        {
            const float presentTermVersion = 1.0f;

            float agreedTermsVersion = 0.0f;

            JObject configFile = null;
            if (File.Exists("config.json"))
            {
                string configFileContent = File.ReadAllText("config.json");
                configFile = JObject.Parse(configFileContent);
                JToken propAgreedTermsVersion = configFile["agreedTermsVersion"];
                if (propAgreedTermsVersion != null && propAgreedTermsVersion.Type == JTokenType.Float)
                    agreedTermsVersion = propAgreedTermsVersion.Value<float>();
            }

            if (agreedTermsVersion != presentTermVersion)
            {
                if (agreedTermsVersion != 0.0f)
                    Console.WriteLine("The terms or privacy policy has been updated.");

                Console.WriteLine("Yes, legal things... *sigh*");
                Console.WriteLine("Use of this program is subject to the term set forward in LICENSES.txt and to privacyPolicy.txt");
                Console.Write("Do you agree to these terms and to the privacy policy? (Y/N) ");
                while (true)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                    if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y')
                    {
                        Console.WriteLine("Y");
                        agreedTermsVersion = presentTermVersion;
                        break;
                    }
                    else if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N')
                    {
                        Console.WriteLine("N");
                        Console.WriteLine("This program requires these conditions and has hence terminated.");
                        Console.WriteLine("Press any key to quit");
                        Console.ReadKey(true);
                        return;
                    }

                }

                if (configFile == null)
                    configFile = new JObject();
                configFile["agreedTermsVersion"] = agreedTermsVersion;
                File.WriteAllText("config.json", configFile.ToString());
                Console.WriteLine("The choice is saved in the config file");
                Console.WriteLine();
            }

            nextTasks = new ConcurrentQueue<Func<Task>>();
            client = null;
            server = null;

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
                //Console.WriteLine("Program was terminated");
                Console.WriteLine("Press any key to quit");
                Console.ReadKey(true);
                return;
            }
            Program.discord = discord;

            PythonManager.pythonSetupTask = Task.Run(() => PythonManager.SetupPython());

            var lobbyManager = discord.GetLobbyManager();
            Program.lobbyManager = lobbyManager;

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

            // Received when someone accepts a request to join or invite.
            // Use secrets to receive back the information needed to add the user to the group/party/match
            activityManager.OnActivityJoin += secret =>
            {
                nextTasks.Enqueue(async () =>
                {
                    createLobby = false;
                    if (currentLobby != null)
                    {
                        Log.Information("Disconnecting from previous lobby");
                        await currentLobby.Disconnect();
                    }
                    //await currentLobby?.Disconnect();

                    //Log.Information($"Joining activity {secret}");
                    currentLobby = await VoiceLobby.FromSecret(secret);
                    client?.Stop();
                    client = new LogicClient(currentLobby);
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
                //Log.Information($"User {user.Username} ({user.Id}) is requesting to join!");
                Log.Information("User {Username}#{Discriminator} is requesting to join.", user.Username, user.Discriminator);
                Log.Information("The request can be reviewed from within Discord.");
            };
            // An invite has been received. Consider rendering the user / activity on the UI.
            activityManager.OnActivityInvite += (Discord.ActivityActionType Type, ref Discord.User user, ref Discord.Activity activity2) =>
            {
                Log.Information("Received invite from {Username}#{Discriminator} to {Type} {activity2.Name}.", user.Username, user.Discriminator);
                Log.Information("The invite can be accepted from within Discord.");
                //Console.WriteLine("OnInvite {0} {1} {2}", Type, user.Username, activity2.Name);
                // activityManager.AcceptInvite(user.Id, result =>
                // {
                //     Console.WriteLine("AcceptInvite {0}", result);
                // });
            };

            string path = System.Reflection.Assembly.GetEntryAssembly().Location;
            path = Path.Combine(Directory.GetParent(path).FullName, "discordGame.exe");

            string launchCommand = $"\"{path}\"";
            Log.Verbose("Registering launchCommand: {LaunchCommand}", launchCommand);
            activityManager.RegisterCommand(launchCommand);

            var userManager = discord.GetUserManager();
            // The auth manager fires events as information about the current user changes.
            // This event will fire once on init.
            //
            // GetCurrentUser will error until this fires once.
            userManager.OnCurrentUserUpdate += () =>
            {
                var currentUser = userManager.GetCurrentUser();
                Log.Information("Current user is {Username}#{Discriminator} ({Id})", currentUser.Username, currentUser.Discriminator, currentUser.Id);

                //Console.WriteLine(currentUser.Username);
                //Console.WriteLine(currentUser.Id);
                currentUserId = currentUser.Id;
            };


            lobbyManager.OnLobbyMessage += (lobbyID, userID, data) =>
            {
                Log.Information("Received lobby message: {1}", Encoding.UTF8.GetString(data));
            };
            //lobbyManager.OnNetworkMessage += (lobbyId, userId, channelId, data) =>
            //{
            //	Log.Information("Received network message: {0} {1} {2} {3}", lobbyId, userId, channelId, Encoding.UTF8.GetString(data));
            //};
            //lobbyManager.OnSpeaking += (lobbyID, userID, speaking) =>
            //{
            //	Log.Information("Received lobby speaking: {0} {1} {2}", lobbyID, userID, speaking);
            //};

            List<(int, Func<Task>)> scheduledTasks = new List<(int, Func<Task>)>
            {
                (180,
                async () =>
                {
                    await createLobbyIfNone();

					//if (!createLobby || currentLobby != null)
					//{
					//	Log.Information("Not creating lobby: already exists");
					//	return;
					//}

					//Log.Information("Creating new lobby");

					////currentLobby = VoiceLobby.Create().Result;
					//currentLobby = await VoiceLobby.Create();
					////Log.Information("Lobby has been created");
					////Log.Information("Blocking for 10 seconds");
					////Thread.Sleep(10000);
					////Log.Information("Done blocking");

					//client = new LogicClient(currentLobby);
				}
                )
            };

            //Task a = Task.Run(() =>
            //{
            //	currentLobby = VoiceLobby.Create().Result;
            //	Log.Information("Lobby has been created");
            //});

            var overlayManager = discord.GetOverlayManager();
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
            //		Console.WriteLine("Voice settings overlay has been opened in Discord");
            //	}
            //});

            //coordinateReader = new CoordinateReader();

            CancellationTokenSource cancelPrintCoordsSource = new CancellationTokenSource();
            CancellationToken cancelPrintCoords = cancelPrintCoordsSource.Token;
            Task printLoop = DoPrintCoordsLoop(cancelPrintCoords);

            List<Task> runningTasks = new List<Task>();
            Task delayingTask = Task.CompletedTask;

            //Log.Information("Running callback loop on {ThreadName} ({ThreadId}).", Thread.CurrentThread.Name, Thread.CurrentThread.ManagedThreadId);

            CancellationTokenSource cancelExecLoopSource = new CancellationTokenSource();
            CancellationToken cancelExecLoop = cancelExecLoopSource.Token;

            Task execLoop = Task.Run(() => DoExecLoop(cancelExecLoop));

            long errorBunchDur = (long)TimeSpan.FromSeconds(10).TotalMilliseconds;
            long errorBunchEnd = Environment.TickCount64 + errorBunchDur;
            long errorBunchMax = 3;
            long errorBunchCount = 0;

            RepeatProfiler profiler = new RepeatProfiler(TimeSpan.FromSeconds(20),
                (RepeatProfiler.Result result) =>
                {
                    //Log.Information("On mainloop. Takes {DurMs:F2} ms on average. Executed {Executed} times. Rate: {Rate} per second. Occupation: {OccupationPerc:F2}%.",
                    //    result.durMs, result.handledCount, result.rate, result.occupation * 100.0f);
                    //Log.Information("On mainloop. minDurMs: {MinDurMs}, maxDurMs: {MaxDurMs}", result.minDurMs, result.maxDurMs);
                });

            try
            {
                Log.Information("\x1b[92mRunning! Enter 'quit' to quit.\x1b[0m");
                int frame = 0;

                while (!isQuitRequested)
                {
                    //profiler.Start();
                    frame += 1;
                    int i = 0;
                    while (i < scheduledTasks.Count)
                    {
                        if (scheduledTasks[i].Item1 > 0)
                        {
                            i++;
                            continue;
                        }

                        nextTasks.Enqueue(scheduledTasks[i].Item2);

                        //Task a = scheduledTasks[i].Item2(); //new Task(scheduledTasks[i].Item2);
                        //runningTasks.Add(a);
                        //a.RunSynchronously();

                        scheduledTasks.RemoveAt(i);
                    }

                    if (Environment.TickCount64 >= errorBunchEnd)
                    {
                        if (errorBunchCount > errorBunchMax)
                            Log.Information("Showing errors again. Hid {NumErrors} errors", errorBunchCount - errorBunchMax);

                        errorBunchCount = 0;
                        errorBunchEnd = Environment.TickCount64 + errorBunchDur;
                    }

                    for (i = 0; i < runningTasks.Count; i++)
                    {
                        Task t = runningTasks[i];
                        if (!t.IsCompleted)
                            continue;
                        TaskStatus st = t.Status;
                        try
                        {
                            //if (st != TaskStatus.RanToCompletion && st != TaskStatus.Faulted)
                            //	throw new Exception($"TaskStatus was {st}");
                            try
                            {
                                await t;
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
                            if (++errorBunchCount <= errorBunchMax)
                            {
                                Log.Warning("Task ended with error: {Msg}", ex.Message);
                            }
                            else if (errorBunchCount == errorBunchMax + 1)
                            {
                                Log.Warning("Hiding errors, max rate has been reached", ex.Message);
                            }
                        }
                        runningTasks.Remove(t);
                        i--;
                    }

                    if (delayingTask.IsCompleted)
                    {
                        if (profiler.IsRunning())
                            profiler.Stop();
                        runningTasks.Remove(delayingTask);

                        if (nextTasks.TryDequeue(out Func<Task> b))
                        {
                            Task c = b();
                            //c.RunSynchronously();
                            delayingTask = c;
                            runningTasks.Add(c);
                            profiler.Start();
                        }
                    }

                    discord.RunCallbacks();
                    lobbyManager.FlushNetwork();

                    //profiler.Stop();

                    for (i = 0; i < scheduledTasks.Count; i++)
                        scheduledTasks[i] = (scheduledTasks[i].Item1 - 1, scheduledTasks[i].Item2);

                    //if (Console.KeyAvailable)
                    //{
                    //	ConsoleKeyInfo key = Console.ReadKey(true);
                    //	if (key.Key == ConsoleKey.Q)
                    //		break;
                    //}

                    if (nextTasks.Count == 0 && delayingTask.IsCompleted)
                        await Task.Delay(1000 / 100);
                    //if ((frame % 60) == 0)
                    //	Log.Information("Ping!");

                }

                server?.Stop();
                client?.Stop();

                cancelPrintCoordsSource.Cancel();
                try
                {
                    printLoop.Wait();
                }
                catch (Exception) { }

                cancelExecLoopSource.Cancel();
                try
                {
                    execLoop.Wait();
                }
                catch (Exception) { }

                //a.Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ended with error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                discord.Dispose();
            }

            Console.WriteLine("The program has terminated. Press any key to quit.");
            Console.ReadKey();
            //await a;
        }

        public static void DoHost()
        {
            server?.Stop();
            server = new LogicServer(currentLobby);
            server.AdvertiseHost();
        }

        static async Task ExecuteCommand(string s)
        {
            Match m;

            if (s == "quit" || s == "exit")
            {
                isQuitRequested = true;
            }
            else if (s == "createLobby")
            {
                nextTasks.Enqueue(async () =>
                {
                    await createLobbyIfNone();
                });
            }
            else if (s == "doHost")
            {
                nextTasks.Enqueue(async () =>
                {
                    DoHost();
                    await Task.CompletedTask;
                });
            }
            else if ((m = Regex.Match(s, "^broadcast (?<message>.*)$")).Success)
            {
                nextTasks.Enqueue(async () =>
                {
                    currentLobby?.SendBroadcast(m.Groups["message"].Value);
                    await Task.CompletedTask;
                });
            }
            else if ((m = Regex.Match(s, "screen (?<screenNum>[+-]?[\\d+])")).Success)
            {
                if (client == null)
                {
                    Console.WriteLine("Client is null");
                    return;
                }
                client.coordsReader.SetScreen(int.Parse(m.Groups["screenNum"].Value));

            }
            else if (s == "overlay")
            {
                var overlayManager = discord.GetOverlayManager();
                overlayManager.OpenVoiceSettings((result) =>
                {
                    if (result == Discord.Result.Ok)
                    {
                        Console.WriteLine("The overlay has been opened in Discord");
                    }
                });
            }
            else
            {
                Console.WriteLine($"Unrecognized command");
            }
            await Task.CompletedTask;
        }

        static async Task DoExecLoop(CancellationToken tok)
        {
            while (!isQuitRequested)
            {
                tok.ThrowIfCancellationRequested();
                string line = Console.ReadLine();
                if (line == null)
                    break;
                try
                {
                    await ExecuteCommand(line);
                }
                catch (Exception ex)
                {
                    Log.Error("Error executing command: {Message}", ex.Message);
                    Log.Error("StackTrace:");
                    Log.Error("{StackTrace}", ex.StackTrace);
                }
            }
        }

        static async Task DoPrintCoordsLoop(CancellationToken tok)
        {
            while (true)
            {
                tok.ThrowIfCancellationRequested();
                TaskCompletionSource<bool> cs = new TaskCompletionSource<bool>();
                nextTasks.Enqueue(async () =>
                {
                    Task<Coords?> t = client?.coordsReader?.GetCoords();

                    Log.Information("[CoordinateReader] Coords are {PrintedCoords}.", (t != null ? await t : null)?.ToString() ?? "null");
                    cs.TrySetResult(true);
                });
                await cs.Task;
                await Task.Delay(TimeSpan.FromSeconds(30), tok);
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
            }
            catch (Exception ex)
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
