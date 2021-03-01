using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MinecraftProximity.DiscordAsync
{
    using Activity = global::Discord.Activity;
    using Result = global::Discord.Result;
    using IntActivityManager = global::Discord.ActivityManager;


    public class ActivityManager
    {
        private readonly Discord discord;
        private readonly IntActivityManager internalActivityManager;

        public event IntActivityManager.ActivityJoinHandler OnActivityJoin;
        public event IntActivityManager.ActivitySpectateHandler OnActivitySpectate;
        public event IntActivityManager.ActivityJoinRequestHandler OnActivityJoinRequest;
        public event IntActivityManager.ActivityInviteHandler OnActivityInvite;

        internal ActivityManager(Discord discordAsync, IntActivityManager activityManager)
        {
            discord = discordAsync;
            internalActivityManager = activityManager;

            internalActivityManager.OnActivityJoin += ActivityManager_OnActivityJoin;
            internalActivityManager.OnActivitySpectate += ActivityManager_OnActivitySpectate;
            internalActivityManager.OnActivityJoinRequest += ActivityManager_OnActivityJoinRequest;
            internalActivityManager.OnActivityInvite += ActivityManager_OnActivityInvite;
        }

        private void ActivityManager_OnActivityInvite(global::Discord.ActivityActionType type, ref global::Discord.User user, ref Activity activity)
        {
            try
            {
                OnActivityInvite?.Invoke(type, ref user, ref activity);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnActivityInvite: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void ActivityManager_OnActivityJoinRequest(ref global::Discord.User user)
        {
            try
            {
                OnActivityJoinRequest?.Invoke(ref user);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnActivityJoinRequest: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void ActivityManager_OnActivitySpectate(string secret)
        {
            try
            {
                OnActivitySpectate?.Invoke(secret);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnActivitySpectate: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private void ActivityManager_OnActivityJoin(string secret)
        {
            try
            {
                OnActivityJoin?.Invoke(secret);
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnActivityJoin: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        public Task<Result> UpdateActivity(Activity activity)
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalActivityManager.UpdateActivity(activity, result =>
                    {
                        taskCompletionSource.TrySetResult(result);
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public void RegisterCommand(string command)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalActivityManager.RegisterCommand(command);
                }
            });
        }
    }
}
