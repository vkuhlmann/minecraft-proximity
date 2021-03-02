using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftProximity.DiscordAsync
{
    using IntOverlayManager = global::Discord.OverlayManager;
    using Result = global::Discord.Result;

    public class OverlayManager
    {
        private readonly Discord discord;
        private readonly IntOverlayManager internalOverlayManager;


        internal OverlayManager(Discord discordAsync, IntOverlayManager overlayManager)
        {
            discord = discordAsync;
            internalOverlayManager = overlayManager;
        }

        public Task<Result> OpenVoiceSettings()
        {
            var taskCompletionSource = new TaskCompletionSource<Result>();

            discord.Queue(new Discord.Request
            {
                action = () =>
                {
                    internalOverlayManager.OpenVoiceSettings((Result result) =>
                    {
                        taskCompletionSource.TrySetResult(result);
                    });
                },
                sendError = ex => taskCompletionSource.TrySetException(ex)
            });
            return taskCompletionSource.Task;
        }


    }
}
