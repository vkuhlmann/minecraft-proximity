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

namespace MinecraftProximity
{
    class Program
    {
        public static Discord.Discord discord;
        public static Discord.LobbyManager lobbyManager;
        public static ConcurrentQueue<Func<Task>> nextTasks;
        public static long currentUserId;
        public static LogicClient client;
        public static LogicServer server;
        public static WebUI webUI;

        // Based on Discord Game DSK example:
        // discord_game_sdk.zip/examples/csharp/Program.cs
        // where discord_game_sdk.zip can be obtained from
        // https://dl-game-sdk.discordapp.net/2.5.6/discord_game_sdk.zip

        public static VoiceLobby currentLobby = null;
        static bool createLobby = true;
        public static bool isQuitRequested = false;
        public static ConfigFile configFile;
        public static ConcurrentQueue<(Task, CancellationTokenSource)> runningTasks;

        public static async Task createLobbyIfNone()
        {
            if (!createLobby || currentLobby != null)
            {
                Log.Information("Not creating lobby: already exists");
                return;
            }

            currentLobby = await VoiceLobby.Create();
            if (currentLobby == null)
            {
                Log.Error("Lobby was null");
                return;
            }

            client?.Stop();
            client = new LogicClient(currentLobby);
            //DoHost();
        }

        static async Task RunAsync(string[] args)
        {
            runningTasks = new ConcurrentQueue<(Task, CancellationTokenSource)>();
            configFile = new ConfigFile("config.json");

            if (!Legal.DoesUserAgree())
            {
                Console.WriteLine("The legal requirements have not been agreed upon, and hence the program must terminate.");
                Console.WriteLine("Press any key to quit");
                Console.ReadKey(true);
                return;
            }

            nextTasks = new ConcurrentQueue<Func<Task>>();
            client = null;
            server = null;
            webUI = null;

            var clientID = "804643036755001365";

            if (configFile.Json["multiDiscord"]?.Value<bool>() == true)
            {
                Console.Write("DISCORD_INSTANCE_ID: ");
                string instanceID = Console.ReadLine();

                Environment.SetEnvironmentVariable("DISCORD_INSTANCE_ID", instanceID);
            }

            Discord.Discord discord;
            try
            {
                discord = new Discord.Discord(long.Parse(clientID), (long)Discord.CreateFlags.Default);
            }
            catch (Exception ex)
            {
                Log.Fatal($"Error initializing Discord hook: {ex}");
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

                    currentLobby = await VoiceLobby.FromSecret(secret);
                    client?.Stop();
                    client = new LogicClient(currentLobby);
                });
            };

            activityManager.OnActivityJoinRequest += (ref Discord.User user) =>
            {
                Log.Information("User {Username}#{Discriminator} is requesting to join.", user.Username, user.Discriminator);
                Log.Information("The request can be reviewed from within Discord.");
            };

            activityManager.OnActivityInvite += (Discord.ActivityActionType Type, ref Discord.User user, ref Discord.Activity activity2) =>
            {
                Log.Information("Received invite from {Username}#{Discriminator} to {Type} {activity2.Name}.", user.Username, user.Discriminator);
                Log.Information("The invite can be accepted from within Discord.");
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string path = System.Reflection.Assembly.GetEntryAssembly().Location;
                path = Path.Combine(Directory.GetParent(path).FullName, "MinecraftProximity.exe");

                string launchCommand = $"\"{path}\"";
                Log.Verbose("Registering launchCommand: {LaunchCommand}", launchCommand);
                activityManager.RegisterCommand(launchCommand);
            }

            var userManager = discord.GetUserManager();
            userManager.OnCurrentUserUpdate += () =>
            {
                var currentUser = userManager.GetCurrentUser();
                Log.Information("Current user is {Username}#{Discriminator} ({Id})", currentUser.Username, currentUser.Discriminator, currentUser.Id);

                currentUserId = currentUser.Id;
            };

            List<(int, Func<Task>)> scheduledTasks = new List<(int, Func<Task>)>
            {
                (180,
                async () =>
                {
                    await createLobbyIfNone();
                }
                )
            };

            CancellationTokenSource cancelPrintCoordsSource = new CancellationTokenSource();
            CancellationToken cancelPrintCoords = cancelPrintCoordsSource.Token;
            Task printLoop = DoPrintCoordsLoop(cancelPrintCoords);

            CancellationTokenSource cancelExecLoopSource = new CancellationTokenSource();
            CancellationToken cancelExecLoop = cancelExecLoopSource.Token;

            Task execLoop = Task.Run(() => CommandHandler.DoHandleLoop(cancelExecLoop));

            //List<Task> runningTasks = new List<Task>
            //{
            //    printLoop,
            //    execLoop
            //};
            runningTasks.Enqueue((printLoop, cancelPrintCoordsSource));
            runningTasks.Enqueue((execLoop, cancelExecLoopSource));

            Task delayingTask = Task.CompletedTask;

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

                while (!isQuitRequested || runningTasks.Count > 0)
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

                    int cycleAround = runningTasks.Count;
                    //for (i = 0; i < runningTasks.Count; i++)
                    //{
                    for (i = 0; i < cycleAround; i++)
                    {
                        (Task, CancellationTokenSource) res;
                        if (!runningTasks.TryDequeue(out res))
                            break;
                        (Task t, CancellationTokenSource cancToken) = res;

                        //Task t = runningTasks[i];
                        if (!t.IsCompleted)
                        {
                            if (isQuitRequested && cancToken != null)
                            {
                                cancToken.Cancel();
                                runningTasks.Enqueue((t, null));
                            }
                            else
                            {
                                runningTasks.Enqueue((t, cancToken));
                            }
                            continue;
                        }
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
                                Log.Warning("Task ended with error: {Msg}\n{StackTrace}", ex.Message, ex.StackTrace);
                            }
                            else if (errorBunchCount == errorBunchMax + 1)
                            {
                                Log.Warning("Hiding errors, max rate has been reached", ex.Message);
                            }
                        }
                        //runningTasks.Remove(t);
                        //i--;
                    }


                    if (delayingTask.IsCompleted)
                    {
                        if (profiler.IsRunning())
                            profiler.Stop();
                        //runningTasks.Remove(delayingTask);

                        if (nextTasks.TryDequeue(out Func<Task> b))
                        {
                            Task c = b();
                            delayingTask = c;
                            runningTasks.Enqueue((c, null));
                            profiler.Start();
                        }
                    }

                    discord.RunCallbacks();
                    lobbyManager.FlushNetwork();

                    //profiler.Stop();

                    for (i = 0; i < scheduledTasks.Count; i++)
                        scheduledTasks[i] = (scheduledTasks[i].Item1 - 1, scheduledTasks[i].Item2);

                    if (nextTasks.Count == 0 && delayingTask.IsCompleted)
                        await Task.Delay(1000 / 100);
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
        }

        public static void DoHost()
        {
            server?.Stop();
            server = new LogicServer(currentLobby);
            server.AdvertiseHost();
        }

        //static async Task ExecuteCommand(string s)
        //{

        //}

        //static async Task DoExecLoop(CancellationToken tok)
        //{
        //    while (!isQuitRequested)
        //    {
        //        tok.ThrowIfCancellationRequested();
        //        string line = Console.ReadLine();
        //        if (line == null)
        //            break;
        //        try
        //        {
        //            await ExecuteCommand(line);
        //        }
        //        catch (Exception ex)
        //        {
        //            Log.Error("Error executing command: {Message}", ex.Message);
        //            Log.Error("StackTrace:");
        //            Log.Error("{StackTrace}", ex.StackTrace);
        //        }
        //    }
        //}

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
