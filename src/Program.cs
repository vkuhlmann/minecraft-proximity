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
using Python.Runtime;

namespace MinecraftProximity
{
    class Program
    {
        public static Discord.Discord discord;
        public static Discord.LobbyManager lobbyManager;
        //public static ConcurrentQueue<Func<Task>> nextTasks;
        public static long currentUserId;
        //public static LogicClient client;
        //public static LogicServer server;
        //public static WebUI webUI;

        // Based on Discord Game DSK example:
        // discord_game_sdk.zip/examples/csharp/Program.cs
        // where discord_game_sdk.zip can be obtained from
        // https://dl-game-sdk.discordapp.net/2.5.6/discord_game_sdk.zip

        //public static VoiceLobby currentLobby = null;
        //static bool createLobby = true;
        //public static bool isQuitRequested = false;
        public static bool isQuitting = false;

        public static ConfigFile configFile;
        //public static ConcurrentQueue<(Task, CancellationTokenSource)> runningTasks;

        public static Instance instance;

        static async Task RunAsync(string[] args)
        {
            Console.Title = "Proximity chat for Minecraft Beta 1.0.1";

            //runningTasks = new ConcurrentQueue<(Task, CancellationTokenSource)>();
            configFile = new ConfigFile("config.json");

            if (!Legal.DoesUserAgree())
            {
                Console.WriteLine("The legal requirements have not been agreed upon, and hence the program must terminate.");
                Console.WriteLine("Press any key to quit");
                Console.ReadKey(true);
                return;
            }

            instance = new Instance();
            var clientID = "814073574499287102";

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

            CancellationTokenSource cancelExecLoopSource = new CancellationTokenSource();
            CancellationToken cancelExecLoop = cancelExecLoopSource.Token;

            Task execLoop = null;

            try
            {
                execLoop = Task.Run(() => CommandHandler.DoHandleLoop(cancelExecLoop));

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

                string nextJoinSecret = null;
                //bool isJoining = false;
                activityManager.OnActivityJoin += secret =>
                {
                    Console.WriteLine($"Received OnActivityJoin with secret {secret}");

                    //if (isJoining)
                    if (nextJoinSecret != null)
                    {
                        Console.WriteLine("A join secret is already queued! Cancelled join.");
                        return;
                    }

                    //isJoining = true;

                    if (instance?.TryJoinLobby(secret) != true)
                    {
                        nextJoinSecret = secret;
                        instance?.SignalStop();
                    }

                    //instance = new Instance();
                    //instance.Run(secret);

                    //nextTasks.Enqueue(async () =>
                    //{
                    //    //webUI?.Stop();
                    //    //server?.Stop();
                    //    //client?.Stop();

                    //    createLobby = false;
                    //    if (currentLobby != null)
                    //    {
                    //        Log.Information("Disconnecting from previous lobby");
                    //        await Task.Delay(500);
                    //        //await currentLobby.Disconnect();
                    //        currentLobby = null;
                    //    }

                    //    //                    await Task.Delay(2000);

                    //    lobbyManager.ConnectLobbyWithActivitySecret(secret, (Discord.Result result, ref Discord.Lobby lobby) =>
                    //    {
                    //        if (result == Discord.Result.Ok)
                    //        {
                    //            Console.WriteLine("Connected to lobby {0}!", lobby.Id);
                    //        }
                    //        isJoining = false;
                    //    });
                    //    await Task.CompletedTask;
                    // await Task.Delay(5000);

                    //await VoiceLobby.FromSecret(secret);
                    ////currentLobby = await VoiceLobby.FromSecret(secret);
                    //return;
                    //client?.Stop();
                    //client = new LogicClient(currentLobby);
                    //});
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

                while (true)
                {
                    string secret = nextJoinSecret;
                    nextJoinSecret = null;
                    await instance.Run(secret);

                    if (nextJoinSecret == null)
                        break;
                }
                //isQuitting = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ended with error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                discord.Dispose();
                isQuitting = true;

            }

            cancelExecLoopSource.Cancel();

            try
            {
                execLoop?.Wait();
            }
            catch (Exception ex)
            {
                Log.Error("Execution loop ended with error: {Error}\n{Stacktrace}", ex.Message, ex.StackTrace);
            }

            using (Py.GIL())
                PythonManager.screeninfo = null;

            Console.WriteLine("The program has terminated. Press any key to quit.");
            Console.ReadKey();
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
