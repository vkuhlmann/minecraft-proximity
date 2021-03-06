using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

using Python.Runtime;
using MinecraftProximity.DiscordAsync;

namespace MinecraftProximity
{
    static class Program
    {
        public static DiscordAsync.Discord discord;
        public static LobbyManager lobbyManager;
        public static ActivityManager activityManager;
        public static CommandHandler commandHandler;

        public static long currentUserId;

        // Based on Discord Game SDK example:
        // discord_game_sdk.zip/examples/csharp/Program.cs
        // where discord_game_sdk.zip can be obtained from
        // https://dl-game-sdk.discordapp.net/2.5.6/discord_game_sdk.zip

        public static bool isQuitting = false;

        public static ConfigFile configFile;

        public static Instance instance;
        public static DirectoryInfo assemblyDir;
        public static string exeFile;
        public static DirectoryInfo pythonDir;

        public static string nextJoinSecret;
        static async Task RunAsync(string[] args)
        {
            Console.Title = "Minecraft Proximity - Version 1.0.1 + Development";
            instance = null;

            assemblyDir = ProgramUtils.LocateAssemblyDir();
            exeFile = ProgramUtils.LocateExeFile();
            pythonDir = ProgramUtils.LocatePythonDir();

            configFile = new ConfigFile("config.json");

            if (!Legal.DoesUserAgree())
            {
                Console.WriteLine("The legal requirements have not been agreed upon, and hence the program must terminate.");
                Console.WriteLine("Press any key to quit.");
                Console.ReadKey(true);
                return;
            }

            ProgramUtils.SetupDiscord();

            commandHandler = new CommandHandler();
            commandHandler.StartLoop();

            try
            {
                PythonManager.pythonSetupTask = Task.Run(() => PythonManager.SetupPython());

                if (exeFile != null)
                {
                    string launchCommand = $"\"{exeFile}\" joining";
                    Log.Information("Registering launchCommand: {LaunchCommand}", launchCommand);
                    activityManager.RegisterCommand(launchCommand);
                }

                while (true)
                {
                    instance = new Instance();
                    string secret = nextJoinSecret;
                    nextJoinSecret = null;
                    await instance.Run(secret);

                    if (nextJoinSecret == null)
                        break;
                }

                isQuitting = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ended with error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                isQuitting = true;
                discord.Dispose();
            }

            commandHandler.StopLoop();

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
