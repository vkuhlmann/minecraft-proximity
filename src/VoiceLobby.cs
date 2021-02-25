using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MinecraftProximity
{
    public class VoiceLobby
    {
        Discord.Lobby lobby;
        readonly Discord.LobbyManager lobbyManager;

        public delegate void OnNetworkJson(long sender, byte channel, JObject jObject);

        public event Discord.LobbyManager.LobbyMessageHandler onLobbyMessage;
        public event Discord.LobbyManager.NetworkMessageHandler onNetworkMessage;
        public event Discord.LobbyManager.SpeakingHandler onSpeaking;

        public event Discord.LobbyManager.MemberConnectHandler onMemberConnect;
        public event Discord.LobbyManager.MemberDisconnectHandler onMemberDisconnect;

        public event OnNetworkJson onNetworkJson;

        public Task currentTask;
        public Discord.LobbyManager.ConnectVoiceHandler connVoiceHandler;
        public Instance instance;

        VoiceLobby(Discord.Lobby lobby, Instance instance)
        {
            this.lobby = lobby;
            this.instance = instance;
            lobbyManager = Program.lobbyManager;
            currentTask = Task.CompletedTask;

            DoInit();
            Log.Information("[Party] Lobby setup finished.");
        }

        public static async Task<VoiceLobby> FromSecret(string secret, Instance instance)
        {
            Log.Information("[Party] Joining lobby from secret.");

            Discord.LobbyManager lobbyManager = Program.lobbyManager;

            TaskCompletionSource<VoiceLobby> completionSource = new TaskCompletionSource<VoiceLobby>();

            Discord.LobbyManager.ConnectLobbyWithActivitySecretHandler handler = (Discord.Result result, ref Discord.Lobby lobby) =>
            {
                if (result == Discord.Result.Ok)
                {
                    Log.Information($"Connected to lobby {lobby.Id}");
                    completionSource.SetResult(new VoiceLobby(lobby, instance));
                }
                else
                {
                    Log.Error("Failed to connect to lobby. Result was {Result}.", result);
                    if (result == Discord.Result.InvalidCommand)
                        Log.Information("If the error was caused by an 'already joined' error, please try restarting Discord.");

                    completionSource.SetResult(null);
                }
            };

            lobbyManager.ConnectLobbyWithActivitySecret(secret, handler);

            VoiceLobby result = await completionSource.Task;
            //result?.PrintMetadata();

            result.PrintUsers();
            return result;
        }

        public static async Task<VoiceLobby> Create(Instance instance)
        {
            Discord.LobbyManager lobbyManager = Program.lobbyManager;

            // Create a lobby.
            var transaction = lobbyManager.GetLobbyCreateTransaction();
            transaction.SetCapacity(10);
            transaction.SetType(Discord.LobbyType.Private);
            transaction.SetMetadata("version", "1");

            TaskCompletionSource<Discord.Lobby?> completionSource = new TaskCompletionSource<Discord.Lobby?>();

            lobbyManager.CreateLobby(transaction, (Discord.Result result, ref Discord.Lobby lobby) =>
            {
                if (result != Discord.Result.Ok)
                {
                    Log.Error("Failed to create lobby. Result was {Result}", result);
                    completionSource.SetResult(null);
                    return;
                }

                Log.Information("[Party] Created lobby with id {LobbyId}. Capacity is {Capacity}, secret is {Secret}.", lobby.Id, lobby.Capacity, lobby.Secret);

                completionSource.SetResult(lobby);
            });

            Discord.Lobby? lobby = await completionSource.Task;
            if (!lobby.HasValue)
                return null;
            return new VoiceLobby(lobby.Value, instance);
        }

        public async Task Disconnect()
        {
            Log.Information("[Party] Disconnecting from lobby {LobbyId}.", lobby.Id);
            await Task.Delay(500);

            Task a = currentTask.ContinueWith((prev) =>
            {
                instance.nextTasks.Enqueue(async () =>
                {
                    TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
                    lobbyManager.DisconnectLobby(lobby.Id, (Discord.Result result) =>
                    {
                        completionSource.TrySetResult(result == Discord.Result.Ok);
                    });
                    await completionSource.Task;
                });
            });

            currentTask = a;
            await a;
        }

        void DoInit()
        {
            connVoiceHandler = (res) =>
            {
                if (res == Discord.Result.Ok)
                    Log.Information("[Party] Voice chat is now connected.");
                else
                    Log.Error("[Party] Failed to connect to voice. Result was {Result}. (Lobby {LobbyId})", res, lobby.Id);
            };

            // Connect to voice chat.
            lobbyManager.ConnectVoice(lobby.Id, connVoiceHandler);

            //Log.Information("[Party] Connecting to network. (Lobby {LobbyId})", lobby.Id);
            lobbyManager.ConnectNetwork(lobby.Id);

            // Channel 0: reliable send to client
            lobbyManager.OpenNetworkChannel(lobby.Id, 0, true);

            // Channel 1: unreliable send to client
            lobbyManager.OpenNetworkChannel(lobby.Id, 1, false);

            // Channel 2: reliable send to server
            lobbyManager.OpenNetworkChannel(lobby.Id, 2, true);

            // Channel 3: unreliable send/receive to server
            lobbyManager.OpenNetworkChannel(lobby.Id, 3, false);


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

            onNetworkMessage += (lobbyId, userId, channelId, data) =>
            {
                onNetworkJson?.Invoke(userId, channelId, JObject.Parse(Encoding.UTF8.GetString(data)));
            };

            SetAsActivity();
        }

        private void HandleOnMemberConnect(long lobbyId, long userId)
        {
            instance.nextTasks.Enqueue(async () =>
            {
                UserResult user = await UserResult.GetUser(userId);
                if (user.result == Discord.Result.Ok)
                    Log.Information("[Party] User {Username}#{Discriminator} has connected to the lobby. (Lobby {LobbyId})", user.user.Username, user.user.Discriminator, lobbyId);
                else
                    Log.Information("[Party] User {UserId} has connected to the lobby. (Lobby {LobbyId})", userId, lobbyId);

                PrintUsers();
            });
        }

        private void HandleOnMemberDisconnect(long lobbyId, long userId)
        {
            instance.nextTasks.Enqueue(async () =>
            {
                UserResult user = await UserResult.GetUser(userId);
                if (user.result == Discord.Result.Ok)
                    Log.Information("[Party] User {Username}#{Discriminator} has disconnected from the lobby. (Lobby {LobbyId})", user.user.Username, user.user.Discriminator, lobbyId);
                else
                    Log.Information("[Party] User {UserId} has disconnected from the lobby. (Lobby {LobbyId})", userId, lobbyId);
                PrintUsers();
            });
        }

        public void SendBroadcast(string message)
        {
            foreach (var user in GetMembers())
            {
                JObject payload = JObject.FromObject(new
                {
                    action = "broadcastReceive",
                    message = message
                });

                SendNetworkJson(user.Id, 0, payload);
            }
        }

        public void PrintUsers()
        {
            List<string> li = new List<string> { "Lobby members:" };

            foreach (var user in Program.lobbyManager.GetMemberUsers(lobby.Id))
            {
                li.Add($"  {user.Username}#{user.Discriminator} ({user.Id})");
            }
            string ans = string.Join('\n', li) + "\n";
            Log.Information(ans);
        }

        public IEnumerable<Discord.User> GetMembers()
        {
            foreach (var user in Program.lobbyManager.GetMemberUsers(lobby.Id))
            {
                yield return user;
            }
        }

        public async void SendNetworkJson(long recipient, byte channel, JObject jObject)
        {
            if (recipient == Program.currentUserId)
            {
                onNetworkJson?.Invoke(Program.currentUserId, channel, (JObject)jObject.DeepClone());
                return;
            }

            lobbyManager.SendNetworkMessage(lobby.Id, recipient, channel,
                Encoding.UTF8.GetBytes(jObject.ToString(Formatting.Indented)));
            await Task.CompletedTask;
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
                    completionSource.TrySetResult(new UserResult(result, user));
                });
                UserResult userResult = await completionSource.Task;

                return await Task.FromResult(userResult);
            }
        }

        public async Task<string> GetFriendlyUsername(long userId)
        {
            UserResult user = await UserResult.GetUser(userId);
            if (user.result == Discord.Result.Ok)
                return $"{user.user.Username}#{user.user.Discriminator}";
            else
                return $"{userId}";
        }

        public async Task UpdateIsDead(long playerId, bool isDead)
        {
            Discord.LobbyMemberTransaction memberTransaction = lobbyManager.GetMemberUpdateTransaction(lobby.Id, playerId);
            memberTransaction.SetMetadata("isDead", $"{isDead}");
            lobbyManager.UpdateMember(lobby.Id, playerId, memberTransaction, (result) =>
            {
                instance.nextTasks.Enqueue(async () =>
                {
                    string playerName = await GetFriendlyUsername(playerId);
                    Log.Information($"Set {playerId} dead status to ${isDead}");
                });
            });
            await Task.CompletedTask;
        }

        public static void PrintAllLobbies()
        {
            Discord.LobbyManager lobbyManager = Program.lobbyManager;

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
                State = "#DevelopmentSquad",
                Details = "Come join the project! github.com/vkuhlmann/minecraft-proximity",
                Timestamps =
                    {
                        Start = DateTimeOffset.Now.ToUnixTimeSeconds()
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
                if (result == Discord.Result.Ok)
                    Log.Information("[Party] Activity has been updated.");
                else
                    Log.Error("[Party] Failed to set activity. Result code was {Res}.", result);
            });
        }

    }
}
