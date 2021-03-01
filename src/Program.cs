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
        public static DiscordAsync.Discord discord;
        public static DiscordAsync.LobbyManager lobbyManager;
        public static long currentUserId;

        // Based on Discord Game DSK example:
        // discord_game_sdk.zip/examples/csharp/Program.cs
        // where discord_game_sdk.zip can be obtained from
        // https://dl-game-sdk.discordapp.net/2.5.6/discord_game_sdk.zip

        public static bool isQuitting = false;

        public static ConfigFile configFile;

        public static Instance instance;
        //public static DirectoryInfo exeRoot;
        public static DirectoryInfo assemblyDir;
        public static string exeFile;
        public static DirectoryInfo pythonDir;

        public static ConcurrentQueue<Task> onDiscordThread;

        static string locateExeFile()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            DirectoryInfo dir = assemblyDir;
            while (dir != null)
            {
                string path = Path.Combine(dir.FullName, "MinecraftProximity.exe");
                if (File.Exists(path))
                    return path;
                dir = dir.Parent;
            }
            return null;
        }

        static DirectoryInfo locatePythonDir()
        {
            DirectoryInfo dir = assemblyDir;
            while (dir != null)
            {
                string path = Path.Combine(dir.FullName, "logicserver.py");
                if (File.Exists(path))
                    return dir;
                dir = dir.Parent;
            }
            return assemblyDir;
        }

        static async Task RunAsync(string[] args)
        {
            Console.Title = "Minecraft Proximity - Version 1.0.0-beta.6";
            instance = null;
            onDiscordThread = new ConcurrentQueue<Task>();

            assemblyDir = Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location);

            exeFile = locateExeFile();
            pythonDir = locatePythonDir();

            configFile = new ConfigFile("config.json");

            if (!Legal.DoesUserAgree())
            {
                Console.WriteLine("The legal requirements have not been agreed upon, and hence the program must terminate.");
                Console.WriteLine("Press any key to quit.");
                Console.ReadKey(true);
                return;
            }

            var clientID = "814073574499287102";

            if (configFile.Json["multiDiscord"]?.Value<bool>() == true)
            {
                Console.Write("DISCORD_INSTANCE_ID: ");
                string instanceID = Console.ReadLine();

                Environment.SetEnvironmentVariable("DISCORD_INSTANCE_ID", instanceID);
            }

            DiscordAsync.Discord discord;
            try
            {
                discord = new DiscordAsync.Discord(long.Parse(clientID), (long)Discord.CreateFlags.Default, false);
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

                    if (nextJoinSecret != null)
                    {
                        Console.WriteLine("A join secret is already queued! Cancelled join.");
                        return;
                    }

                    if (instance?.TryQueueJoinLobby(secret) != true)
                    {
                        nextJoinSecret = secret;
                        instance?.SignalStop();
                    }
                };

                activityManager.OnActivityJoinRequest += (ref Discord.User user) =>
                {
                    Log.Information("User {Username}#{Discriminator} is requesting to join.", user.Username, user.Discriminator);
                    Log.Information("The request can be reviewed from within Discord.");
                };

                activityManager.OnActivityInvite += (Discord.ActivityActionType Type, ref Discord.User user, ref Discord.Activity activity2) =>
                {
                    Log.Information("Received invite from {Username}#{Discriminator} to {Type} {ActivityName}.", user.Username, user.Discriminator, activity2.Type, activity2.Name);
                    Log.Information("The invite can be accepted from within Discord.");
                };

                //lobbyManager.OnLobbyMessage += (lobbyID, userID, data) =>
                //{
                //    if (lobbyID == lobby.Id)
                //        onLobbyMessage?.Invoke(lobbyID, userID, data);
                //    //Console.WriteLine("Lobby message: {0} {1}", lobbyID, Encoding.UTF8.GetString(data));
                //};
                lobbyManager.OnNetworkMessage += (lobbyId, userId, channelId, data) =>
                {
                    //Log.Information("Received message");
                    //if (lobbyId == lobby.Id)
                    //    onNetworkMessage?.Invoke(lobbyId, userId, channelId, data);
                    try
                    {
                        instance?.OnNetworkMessage(lobbyId, userId, channelId, data);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error receiving network message: {Message}", ex.Message);
                    }

                    //Console.WriteLine("Network message: {0} {1} {2} {3}", lobbyId, userId, channelId, Encoding.UTF8.GetString(data));
                };
                //lobbyManager.OnSpeaking += (lobbyID, userID, speaking) =>
                //{
                //    if (lobbyID == lobby.Id)
                //        onSpeaking?.Invoke(lobbyID, userID, speaking);

                //    //Console.WriteLine("Lobby speaking: {0} {1} {2}", lobbyID, userID, speaking);
                //};

                lobbyManager.OnMemberConnect += (lobbyID, userID) =>
                {
                    //Console.WriteLine($"Member connected to lobby {lobbyID}: {userID}");
                    //if (lobbyID == lobby.Id)
                    //    onMemberConnect?.Invoke(lobbyID, userID);
                    try
                    {
                        instance?.OnMemberConnect(lobbyID, userID);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error receiving member connect: {Message}", ex.Message);
                    }
                };

                lobbyManager.OnMemberDisconnect += (lobbyID, userID) =>
                {
                    //Console.WriteLine($"Member disconnected from lobby {lobbyID}: {userID}");
                    //if (lobbyID == lobby.Id)
                    //    onMemberDisconnect?.Invoke(lobbyID, userID);
                    try
                    {
                        instance?.OnMemberDisconnect(lobbyID, userID);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Error receiving member connect: {Message}", ex.Message);
                    }
                };

                //if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                //{
                //    string path = System.Reflection.Assembly.GetEntryAssembly().Location;
                //    path = Path.Combine(Directory.GetParent(path).FullName, "MinecraftProximity.exe");

                //    string launchCommand = $"\"{path}\"";
                //    Log.Information("Registering launchCommand: {LaunchCommand}", launchCommand);
                //    activityManager.RegisterCommand(launchCommand);
                //}

                if (exeFile != null)
                {
                    string launchCommand = $"\"{exeFile}\"";
                    Log.Information("Registering launchCommand: {LaunchCommand}", launchCommand);
                    activityManager.RegisterCommand(launchCommand);
                }

                var userManager = discord.GetUserManager();
                userManager.OnCurrentUserUpdate += async () =>
                {
                    var currentUser = await userManager.GetCurrentUser();
                    Log.Information("Current user is {Username}#{Discriminator} ({Id}).", currentUser.Username, currentUser.Discriminator, currentUser.Id);

                    currentUserId = currentUser.Id;
                };

                while (true)
                {
                    instance = new Instance();
                    string secret = nextJoinSecret;
                    nextJoinSecret = null;
                    await instance.Run(secret);

                    if (nextJoinSecret == null)
                        break;
                }
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
                Console.WriteLine("Press any key to quit.");
                Console.ReadKey();
            }
        }
    }
}
