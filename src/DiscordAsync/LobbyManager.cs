using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Diagnostics;

namespace MinecraftProximity.DiscordAsync
{
    using User = global::Discord.User;
    using Result = global::Discord.Result;
    using Lobby = global::Discord.Lobby;
    using IntLobbyManager = global::Discord.LobbyManager;

    public class LobbyManager
    {
        public delegate void ConnectVoiceHandler(Result result);

        private readonly Discord discord;
        private readonly global::Discord.LobbyManager internalLobbyManager;

        public delegate void NetworkMessageHandler(long lobbyId, long userId, byte channelId, byte[] data);
        public event NetworkMessageHandler OnNetworkMessage;

        public event IntLobbyManager.LobbyUpdateHandler OnLobbyUpdate;

        public event IntLobbyManager.LobbyDeleteHandler OnLobbyDelete;

        public event IntLobbyManager.MemberConnectHandler OnMemberConnect;

        public event IntLobbyManager.MemberUpdateHandler OnMemberUpdate;

        public event IntLobbyManager.MemberDisconnectHandler OnMemberDisconnect;

        internal LobbyManager(Discord discordAsync, IntLobbyManager lobbyManager)
        {
            discord = discordAsync;
            internalLobbyManager = lobbyManager;

            //internalLobbyManager.OnLobbyUpdate += OnLobbyUpdate;
            //internalLobbyManager.OnLobbyDelete += OnLobbyDelete;

            internalLobbyManager.OnNetworkMessage += InternalLobbyManager_OnNetworkMessage;

            internalLobbyManager.OnLobbyUpdate += InternalLobbyManager_OnLobbyUpdate;
            internalLobbyManager.OnLobbyDelete += InternalLobbyManager_OnLobbyDelete;
            internalLobbyManager.OnMemberConnect += InternalLobbyManager_OnMemberConnect;
            internalLobbyManager.OnMemberUpdate += InternalLobbyManager_OnMemberUpdate;
            internalLobbyManager.OnMemberDisconnect += InternalLobbyManager_OnMemberDisconnect;

        }

        private void InternalLobbyManager_OnNetworkMessage(long lobbyId, long userId, byte channelId, byte[] data)
        {
            try
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.RECEIVE_MESSAGE,
                    lobbyId = lobbyId,
                    userId = userId,
                    channelId = channelId
                });

                OnNetworkMessage?.Invoke(lobbyId, userId, channelId, data);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnNetworkMessage: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void InternalLobbyManager_OnMemberDisconnect(long lobbyId, long userId)
        {
            try
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.USER_DISCONNECT,
                    lobbyId = lobbyId,
                    userId = userId
                });
                OnMemberDisconnect?.Invoke(lobbyId, userId);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnMemberDisconnect: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void InternalLobbyManager_OnMemberUpdate(long lobbyId, long userId)
        {
            try
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.ON_MEMBER_UPDATE,
                    lobbyId = lobbyId,
                    userId = userId
                });

                OnMemberUpdate?.Invoke(lobbyId, userId);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnMemberUpdate: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void InternalLobbyManager_OnMemberConnect(long lobbyId, long userId)
        {
            try
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.USER_CONNECT,
                    lobbyId = lobbyId,
                    userId = userId
                });
                OnMemberConnect?.Invoke(lobbyId, userId);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnMemberConnect: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void InternalLobbyManager_OnLobbyDelete(long lobbyId, uint reason)
        {
            try
            {
                OnLobbyDelete?.Invoke(lobbyId, reason);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnLobbyDelete: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void InternalLobbyManager_OnLobbyUpdate(long lobbyId)
        {
            try
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.ON_LOBBY_UPDATE,
                    lobbyId = lobbyId
                });

                OnLobbyUpdate?.Invoke(lobbyId);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnLobbyUpdate: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        public void FlushNetwork()
        {
            //var taskCompletionSource = new TaskCompletionSource<bool>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.FLUSH_NETWORK
                    });
                    internalLobbyManager.FlushNetwork();
                    discord.flushNetworkCycle++;
                    //taskCompletionSource.TrySetResult(true);
                }
            });
            //return taskCompletionSource.Task;
        }

        public Task<(Result result, Lobby lobby)> ConnectLobbyWithActivitySecret(string activitySecret)
        {
            var taskCompletionSource = new TaskCompletionSource<(Result, Lobby)>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.CONNECT_LOBBY
                    });

                    internalLobbyManager.ConnectLobbyWithActivitySecret(activitySecret, (Result result, ref Lobby lobby) =>
                    {
                        DebugLog.Log(new DebugLog.Entry
                        {
                            op = DebugLog.Operation.CONNECTED_LOBBY,
                            lobbyId = lobby.Id
                        });
                        taskCompletionSource.TrySetResult((result, lobby));
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<(Result result, Lobby lobby)> ConnectLobby(long lobbyId, string secret)
        {
            var taskCompletionSource = new TaskCompletionSource<(Result, Lobby)>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.CONNECT_LOBBY,
                        lobbyId = lobbyId
                    });

                    internalLobbyManager.ConnectLobby(lobbyId, secret, (Result result, ref Lobby lobby) =>
                    {
                        DebugLog.Log(new DebugLog.Entry
                        {
                            op = DebugLog.Operation.CONNECTED_LOBBY,
                            lobbyId = lobby.Id
                        });
                        taskCompletionSource.TrySetResult((result, lobby));
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public string GetLobbyActivitySecret(long lobbyId)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.GET_LOBBY_ACTIVITY_SECRET,
                    lobbyId = lobbyId
                });
                return internalLobbyManager.GetLobbyActivitySecret(lobbyId);
            }

            var taskCompletionSource = new TaskCompletionSource<string>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.GET_LOBBY_ACTIVITY_SECRET,
                        lobbyId = lobbyId
                    });

                    string ans = internalLobbyManager.GetLobbyActivitySecret(lobbyId);
                    taskCompletionSource.TrySetResult(ans);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }


        static long lastConnectAttempt = 0;
        // ConnectNetwork has a rate limit of once per 5 seconds.
        static TimeSpan minConnectInterval = TimeSpan.FromSeconds(7);

        public void ConnectNetwork(long lobbyId)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    long timestamp = Environment.TickCount64;
                    if (timestamp < lastConnectAttempt + (long)minConnectInterval.TotalMilliseconds)
                    {
                        Log.Information("Waiting before connecting network...");

                        Thread.Sleep((int)(lastConnectAttempt + (long)minConnectInterval.TotalMilliseconds - timestamp));

                        Log.Information("Connecting network...");
                    }

                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.CONNECT_NETWORK,
                        lobbyId = lobbyId
                    });

                    internalLobbyManager.ConnectNetwork(lobbyId);
                    timestamp = Environment.TickCount64;
                    if (lastConnectAttempt != 0)
                        Log.Information("ConnectNetwork interval was roughly {Interval}", timestamp - lastConnectAttempt);

                    lastConnectAttempt = timestamp;
                }
            });
        }

        public Task<Result> ConnectVoice(long lobbyId)
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.CONNECT_VOICE,
                        lobbyId = lobbyId
                    });

                    internalLobbyManager.ConnectVoice(lobbyId, (Result result) =>
                    {
                        taskCompletionSource.TrySetResult(result);
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<Result> DisconnectVoice(long lobbyId)
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.DISCONNECT_VOICE,
                        lobbyId = lobbyId
                    });

                    internalLobbyManager.DisconnectVoice(lobbyId, (Result result) =>
                    {
                        taskCompletionSource.TrySetResult(result);
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<Result> DisconnectLobby(long lobbyId)
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.DISCONNECT_LOBBY,
                        lobbyId = lobbyId
                    });

                    internalLobbyManager.DisconnectLobby(lobbyId, (Result result) =>
                    {
                        DebugLog.Log(new DebugLog.Entry
                        {
                            op = DebugLog.Operation.DISCONNECTED_LOBBY,
                            lobbyId = lobbyId
                        });
                        taskCompletionSource.TrySetResult(result);
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public void DisconnectNetwork(long lobbyId)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.DISCONNECT_NETWORK,
                        lobbyId = lobbyId
                    });
                    internalLobbyManager.DisconnectNetwork(lobbyId);
                }
            });
        }

        public IEnumerable<User> GetMemberUsers(long lobbyId)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
                return internalLobbyManager.GetMemberUsers(lobbyId);

            var taskCompletionSource = new TaskCompletionSource<IEnumerable<User>>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    var ans = internalLobbyManager.GetMemberUsers(lobbyId);
                    taskCompletionSource.TrySetResult(ans);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }

        public User GetMemberUser(long lobbyId, long userId)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.GET_MEMBER_USER,
                    lobbyId = lobbyId,
                    userId = userId
                });
                return internalLobbyManager.GetMemberUser(lobbyId, userId);
            }

            var taskCompletionSource = new TaskCompletionSource<User>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.GET_MEMBER_USER,
                        lobbyId = lobbyId,
                        userId = userId
                    });
                    var ans = internalLobbyManager.GetMemberUser(lobbyId, userId);
                    taskCompletionSource.TrySetResult(ans);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }

        public int MemberCount(long lobbyId)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.MEMBER_COUNT,
                    lobbyId = lobbyId
                });

                return internalLobbyManager.MemberCount(lobbyId);
            }

            var taskCompletionSource = new TaskCompletionSource<int>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.MEMBER_COUNT,
                        lobbyId = lobbyId
                    });

                    var ans = internalLobbyManager.MemberCount(lobbyId);
                    taskCompletionSource.TrySetResult(ans);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }


        public string GetLobbyMetadataKey(long lobbyId, int index)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
                return internalLobbyManager.GetLobbyMetadataKey(lobbyId, index);

            var taskCompletionSource = new TaskCompletionSource<string>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    string result = internalLobbyManager.GetLobbyMetadataKey(lobbyId, index);
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }

        public string GetLobbyMetadataValue(long lobbyId, string key)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.GET_LOBBY_METADATA_VALUE,
                    lobbyId = lobbyId
                });
                return internalLobbyManager.GetLobbyMetadataValue(lobbyId, key);
            }

            var taskCompletionSource = new TaskCompletionSource<string>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.GET_LOBBY_METADATA_VALUE,
                        lobbyId = lobbyId
                    });
                    string result = internalLobbyManager.GetLobbyMetadataValue(lobbyId, key);
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }

        public int LobbyMetadataCount(long lobbyId)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
            {
                DebugLog.Log(new DebugLog.Entry
                {
                    op = DebugLog.Operation.GET_LOBBY_METADATA_COUNT,
                    lobbyId = lobbyId
                });
                return internalLobbyManager.LobbyMetadataCount(lobbyId);
            }

            var taskCompletionSource = new TaskCompletionSource<int>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.GET_LOBBY_METADATA_COUNT,
                        lobbyId = lobbyId
                    });

                    int result = internalLobbyManager.LobbyMetadataCount(lobbyId);
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }

        public global::Discord.LobbyMemberTransaction GetMemberUpdateTransaction(long lobbyId, long userId)
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
                return internalLobbyManager.GetMemberUpdateTransaction(lobbyId, userId);

            var taskCompletionSource = new TaskCompletionSource<global::Discord.LobbyMemberTransaction>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    global::Discord.LobbyMemberTransaction result = internalLobbyManager.GetMemberUpdateTransaction(lobbyId, userId);
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }


        public void OpenNetworkChannel(long lobbyId, byte channelId, bool reliable)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.OPEN_NETWORK_CHANNEL,
                        lobbyId = lobbyId,
                        channelId = channelId
                    });

                    internalLobbyManager.OpenNetworkChannel(lobbyId, channelId, reliable);
                }
            });
        }

        public void SendNetworkMessage(long lobbyId, long userId, byte channelId, byte[] data)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.SEND_MESSAGE,
                        lobbyId = lobbyId,
                        userId = userId,
                        channelId = channelId
                    });

                    internalLobbyManager.SendNetworkMessage(lobbyId, userId, channelId, data);
                }
            });
        }

        public Task<Result> UpdateMemberAsync(long lobbyId, long userId, global::Discord.LobbyMemberTransaction transaction)
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.UPDATE_MEMBER,
                        lobbyId = lobbyId
                    });

                    internalLobbyManager.UpdateMember(lobbyId, userId, transaction, (Result result) =>
                    {
                        DebugLog.Log(new DebugLog.Entry
                        {
                            op = DebugLog.Operation.UPDATED_MEMBER,
                            lobbyId = lobbyId
                        });
                        taskCompletionSource.TrySetResult(result);
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<(Result result, Lobby lobby)> CreateLobbyAsync(global::Discord.LobbyTransaction transaction)
        {
            var taskCompletionSource = new TaskCompletionSource<(Result, Lobby)>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.CREATE_LOBBY
                    });

                    internalLobbyManager.CreateLobby(transaction, (Result result, ref Lobby lobby) =>
                    {
                        DebugLog.Log(new DebugLog.Entry
                        {
                            op = DebugLog.Operation.CREATED_LOBBY,
                            lobbyId = lobby.Id
                        });

                        taskCompletionSource.TrySetResult((result, lobby));
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public global::Discord.LobbyTransaction GetLobbyCreateTransaction()
        {
            if (Thread.CurrentThread.ManagedThreadId == discord.ManagedThreadId)
                return internalLobbyManager.GetLobbyCreateTransaction();

            var taskCompletionSource = new TaskCompletionSource<global::Discord.LobbyTransaction>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.GET_LOBBY_CREATE_TRANSACTION
                    });

                    var result = internalLobbyManager.GetLobbyCreateTransaction();
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task.Result;
        }

        public void PrintPerformanceTest(long lobbyId)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    int runCount = 100000;
                    for (int i = 0; i < runCount; i++)
                    {
                        //int v = lobbyManager.MemberCount(lobby.Id);
                        var a = internalLobbyManager.GetMemberUsers(lobbyId);
                    }
                    stopwatch.Stop();
                    Log.Information("[Native] GetMemberUsers performance test: {Duration:F3} us per invocation", stopwatch.Elapsed / runCount / TimeSpan.FromMilliseconds(0.001));
                }
            });

        }

        //public Task<Result> UpdateMember(long lobbyId, long userId, global::Discord.LobbyMemberTransaction transaction)
        //{
        //    var taskCompletionSource = new TaskCompletionSource<Result>();

        //    discord.Queue(new Discord.Request
        //    {
        //        action = () =>
        //        {
        //            internalLobbyManager.UpdateMember(lobbyId, userId, transaction, (Result result) =>
        //            {
        //                taskCompletionSource.TrySetResult(result);
        //            });
        //        },
        //        sendError = ex => taskCompletionSource.TrySetException(ex)
        //    });
        //    return taskCompletionSource.Task;
        //}


    }
}

