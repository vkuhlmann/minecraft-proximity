using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MinecraftProximity.DiscordAsync
{
    using User = global::Discord.User;
    using Result = global::Discord.Result;
    using IntUserManager = global::Discord.UserManager;

    public class UserManager
    {
        private readonly Discord discord;
        private readonly IntUserManager internalUserManager;

        public event IntUserManager.CurrentUserUpdateHandler OnCurrentUserUpdate;

        internal UserManager(Discord discordAsync, IntUserManager userManager)
        {
            discord = discordAsync;
            internalUserManager = userManager;

            internalUserManager.OnCurrentUserUpdate += InternalUserManager_OnCurrentUserUpdate;
        }

        private void InternalUserManager_OnCurrentUserUpdate()
        {
            try
            {
                OnCurrentUserUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnCurrentUserUpdate: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        public Task<User> GetCurrentUser()
        {
            var taskCompletionSource = new TaskCompletionSource<User>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    DebugLog.Log(new DebugLog.Entry
                    {
                        op = DebugLog.Operation.GET_CURRENT_USER
                    });
                    var ans = internalUserManager.GetCurrentUser();
                    taskCompletionSource.TrySetResult(ans);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }


    }
}
