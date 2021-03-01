using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MinecraftProximity.DiscordAsync
{
    using Result = global::Discord.Result;
    using IntVoiceManager = global::Discord.VoiceManager;

    public class VoiceManager
    {
        private readonly Discord discord;
        private readonly IntVoiceManager internalVoiceManager;

        public event IntVoiceManager.SettingsUpdateHandler OnSettingsUpdate;

        internal VoiceManager(Discord discordAsync, IntVoiceManager voiceManager)
        {
            discord = discordAsync;
            internalVoiceManager = voiceManager;

            internalVoiceManager.OnSettingsUpdate += VoiceManager_OnSettingsUpdate;
        }

        private void VoiceManager_OnSettingsUpdate()
        {
            try
            {
                OnSettingsUpdate?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Error occured during OnSettingsUpdate: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        public void SetLocalVolume(long userId, byte volume)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalVoiceManager.SetLocalVolume(userId, volume);
                }
            });
        }

        public Task<global::Discord.InputMode> GetInputMode()
        {
            var taskCompletionSource = new TaskCompletionSource<global::Discord.InputMode>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    var result = internalVoiceManager.GetInputMode();
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<byte> GetLocalVolume(long userId)
        {
            var taskCompletionSource = new TaskCompletionSource<byte>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    var result = internalVoiceManager.GetLocalVolume(userId);
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<bool> IsSelfDeaf()
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    var result = internalVoiceManager.IsSelfDeaf();
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<bool> IsSelfMute()
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    var result = internalVoiceManager.IsSelfMute();
                    taskCompletionSource.TrySetResult(result);
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public Task<Result> SetInputMode(global::Discord.InputMode inputMode)
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalVoiceManager.SetInputMode(
                        inputMode, result =>
                        {
                            taskCompletionSource.TrySetResult(result);
                        }
                     );
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }

        public void IsSelfMute(bool mute)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalVoiceManager.SetSelfMute(mute);
                }
            });
        }

        public void IsSelfDeaf(bool deaf)
        {
            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalVoiceManager.SetSelfDeaf(deaf);
                }
            });
        }
    }
}
