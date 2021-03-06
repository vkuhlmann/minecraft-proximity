using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;
using Newtonsoft.Json.Linq;
using Serilog;

namespace MinecraftProximity
{
    static class ProgramUtils
    {
        public static DirectoryInfo LocateAssemblyDir()
        {
            return Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location);
        }

        public static string LocateExeFile()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            DirectoryInfo dir = Program.assemblyDir;
            while (dir != null)
            {
                string path = Path.Combine(dir.FullName, "MinecraftProximity.exe");
                if (File.Exists(path))
                    return path;
                dir = dir.Parent;
            }
            return null;
        }

        public static DirectoryInfo LocatePythonDir()
        {
            DirectoryInfo dir = Program.assemblyDir;
            while (dir != null)
            {
                string path = Path.Combine(dir.FullName, "logicserver.py");
                if (File.Exists(path))
                    return dir;
                dir = dir.Parent;
            }
            return Program.assemblyDir;
        }

        public static void SetupDiscord()
        {
            var clientID = "814073574499287102";

            if (Program.configFile.Json["multiDiscord"]?.Value<bool>() == true)
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

            discord.SetLogHook(Discord.LogLevel.Debug, (level, message) =>
            {
                if (level == Discord.LogLevel.Error)
                    Log.Error($"Discord: {message}");
                else if (level == Discord.LogLevel.Warn)
                    Log.Warning($"Discord: {message}");
                else
                    Log.Information($"Discord ({level}): {message}");

                if (level == Discord.LogLevel.Error || level == Discord.LogLevel.Warn)
                    DebugLog.Dump(true);
            });

            Program.lobbyManager = discord.GetLobbyManager();
            Program.activityManager = discord.GetActivityManager();

            SetupEventHandlers();
        }

        public static void SetupEventHandlers()
        {
            Program.activityManager.OnActivityJoin += secret =>
            {
                if (Program.nextJoinSecret != null)
                {
                    Console.WriteLine("A join secret is already queued! Cancelled join.");
                    return;
                }

                if (Program.instance?.TryQueueJoinLobby(secret) != true)
                {
                    Program.nextJoinSecret = secret;
                    Program.instance?.SignalStop();
                }
            };

            Program.activityManager.OnActivityJoinRequest += (ref Discord.User user) =>
            {
                Log.Information("User {Username}#{Discriminator} is requesting to join.", user.Username, user.Discriminator);
                Log.Information("The request can be reviewed from within Discord.");
            };

            Program.activityManager.OnActivityInvite += (Discord.ActivityActionType Type, ref Discord.User user, ref Discord.Activity activity2) =>
            {
                Log.Information("Received invite from {Username}#{Discriminator} to {Type} {ActivityName}.", user.Username, user.Discriminator, activity2.Type, activity2.Name);
                Log.Information("The invite can be accepted from within Discord.");
            };

            Program.lobbyManager.OnNetworkMessage += (lobbyId, userId, channelId, data) =>
            {
                try
                {
                    Program.instance?.OnNetworkMessage(lobbyId, userId, channelId, data);
                }
                catch (Exception ex)
                {
                    Log.Error("Error receiving network message: {Message}", ex.Message);
                }
            };

            Program.lobbyManager.OnMemberConnect += (lobbyID, userID) =>
            {
                try
                {
                    Program.instance?.OnMemberConnect(lobbyID, userID);
                }
                catch (Exception ex)
                {
                    Log.Error("Error receiving member connect: {Message}", ex.Message);
                }
            };

            Program.lobbyManager.OnMemberDisconnect += (lobbyID, userID) =>
            {
                try
                {
                    Program.instance?.OnMemberDisconnect(lobbyID, userID);
                }
                catch (Exception ex)
                {
                    Log.Error("Error receiving member connect: {Message}", ex.Message);
                }
            };

            var userManager = Program.discord.GetUserManager();
            userManager.OnCurrentUserUpdate += async () =>
            {
                var currentUser = await userManager.GetCurrentUser();
                Log.Information("Current user is {Username}#{Discriminator} ({Id}).", currentUser.Username, currentUser.Discriminator, currentUser.Id);

                Program.currentUserId = currentUser.Id;
            };
        }
    }
}
