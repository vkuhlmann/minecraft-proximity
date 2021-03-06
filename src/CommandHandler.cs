using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.Text.RegularExpressions;

namespace MinecraftProximity
{
    class CommandHandler
    {
        readonly CancellationTokenSource cancelExecLoopSource;
        readonly CancellationToken cancelExecLoop;

        Task execLoop;

        public CommandHandler()
        {
            cancelExecLoopSource = new CancellationTokenSource();
            cancelExecLoop = cancelExecLoopSource.Token;

            execLoop = null;
        }

        public void StartLoop()
        {
            execLoop = Task.Run(() => DoHandleLoop(cancelExecLoop));
        }

        public void StopLoop()
        {
            Program.isQuitting = true;
            cancelExecLoopSource?.Cancel();

            try
            {
                execLoop?.Wait();
            }
            catch (Exception ex)
            {
                Log.Error("Execution loop ended with error: {Error}\n{Stacktrace}", ex.Message, ex.StackTrace);
            }
        }

        public static async Task DoHandleLoop(CancellationToken cancTok)
        {
            while (!Program.isQuitting)
            {
                cancTok.ThrowIfCancellationRequested();
                string line = Console.ReadLine();
                if (line == null)
                    break;
                try
                {
                    try
                    {
                        await ExecuteCommand(line);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"An error occured while executing the command: {ex}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Error executing command: {Message}", ex.Message);
                    Log.Error("StackTrace:");
                    Log.Error("{StackTrace}", ex.StackTrace);
                }
            }
            await Task.CompletedTask;
        }

        public static Dictionary<string, Func<string, Task>> commands = new Dictionary<string, Func<string, Task>>
        {
            { "quit", DoQuitCommand },
            { "createLobby", DoCreateLobbyCommand },
            { "doHost", DoDoHostCommand },
            { "broadcast", DoBroadcastCommand },
            { "screen", DoScreenCommand },
            { "overlay", DoOverlayCommand },
            { "webui", DoWebUICommand },
            { "dump", DoDumpCommand }
        };

        public static Dictionary<string, string> commandAliases = new Dictionary<string, string>
        {
            { "exit", "quit" }
        };

        static async Task DoQuitCommand(string argument)
        {
            if (argument != "")
            {
                Console.WriteLine("Quit command does not take any argument.");
                return;
            }

            Program.isQuitting = true;
            Program.instance.SignalStop();
            //Program.isQuitRequested = true;
            await Task.CompletedTask;
        }

        static Task DoDumpCommand(string argument)
        {
            if (argument != "")
            {
                Console.WriteLine("Dump command does not take any argument.");
                return Task.CompletedTask;
            }

            Program.discord.ForceDump();
            return Task.CompletedTask;
        }

        static async Task DoCreateLobbyCommand(string argument)
        {
            if (argument != "")
            {
                Console.WriteLine("Quit command does not take any argument.");
                return;
            }

            Instance instance = Program.instance;

            instance?.Queue("CreateLobby", async () =>
            {
                await instance.createLobbyIfNone();
            });
            await Task.CompletedTask;
        }

        static async Task DoDoHostCommand(string argument)
        {
            if (argument != "")
            {
                Console.WriteLine("Quit command does not take any argument.");
                return;
            }

            Instance instance = Program.instance;

            instance?.Queue("DoHost", async () =>
            {
                instance.DoHost();
                await Task.CompletedTask;
            });
            await Task.CompletedTask;
        }

        static async Task DoBroadcastCommand(string argument)
        {
            if (argument != "")
            {
                Console.WriteLine("Quit command does not take any argument.");
                return;
            }

            Instance instance = Program.instance;

            instance?.Queue("SendBroadcast", () =>
            {
                instance.currentLobby?.SendBroadcast(argument);
                return null;
                //await Task.CompletedTask;
            });
            await Task.CompletedTask;
        }

        static async Task DoScreenCommand(string argument)
        {
            if (!Regex.Match(argument, "^[+-]?[\\d+]$").Success)
            {
                Console.WriteLine("Invalid syntax. Syntax is");
                Console.WriteLine("\x1b[91mscreen <\"-1\" | screenNum>\x1b[0m");
            }

            // Regex.Match(s, "screen (?<screenNum>)")

            Instance instance = Program.instance;

            instance?.Queue("SetScreen", async () =>
            {
                if (instance.client == null)
                {
                    Console.WriteLine("Client is null");
                    return;
                }

                instance.client.coordsReader.SetScreen(int.Parse(argument));
                await Task.CompletedTask;
            });
            await Task.CompletedTask;
        }

        static async Task DoOverlayCommand(string argument)
        {
            Instance instance = Program.instance;

            instance?.Queue("OpenOverlay", async () =>
            {
                var overlayManager = Program.discord.GetOverlayManager();
                //overlayManager.OpenVoiceSettings((result) =>
                //{
                //    if (result == Discord.Result.Ok)
                //    {
                //        Console.WriteLine("The overlay has been opened in Discord.");
                //    }
                //});
                //await Task.CompletedTask;
                Discord.Result result = await overlayManager.OpenVoiceSettings();
                if (result == Discord.Result.Ok)
                {
                    Log.Information("The overlay has been opened in Discord.");
                } else
                {
                    Log.Warning("Failed to open overlay. Result was {Result}", result);
                }
            });
            await Task.CompletedTask;
        }

        static async Task DoWebUICommand(string argument)
        {
            Match m = Regex.Match(argument, "^(?<subcommand>[a-zA-Z0-9]+)( (?<args>.*))?$");
            if (!m.Success)
            {
                Console.WriteLine("Invalid command syntax!");
                Console.WriteLine("Syntax is \x1b[91mwebui <subcommand> [args ...]\x1b[0m");
            }

            string subcommand = m.Groups["subcommand"].Value;
            string args = m.Groups["args"].Value;

            Instance instance = Program.instance;

            instance?.Queue("HandleWebUICommand", async () =>
            {
                if (subcommand == "start")
                {
                    if (args != "")
                    {
                        Console.WriteLine("Subcommand start does not take any arguments. Cancelling.");
                        return;
                    }

                    if (instance.webUI != null)
                    {
                        Console.WriteLine("WebUI already exists!");
                        return;
                    }
                    instance.webUI = new WebUI(instance);
                    instance.webUI.Start();
                }
                else if (subcommand == "stop")
                {
                    if (args != "")
                    {
                        Console.WriteLine("Subcommand stop does not take any arguments. Cancelling.");
                        return;
                    }
                    instance.webUI?.Stop();
                    instance.webUI = null;
                }
                else if (instance.webUI != null && instance.webUI.PythonHandleCommand(subcommand, args))
                {
                    //else if (subcommand == "xz")
                    //{
                    ///Program.webUI?.HandleXZCommand(args);
                    //}
                }
                else
                {
                    Console.WriteLine("Unknown subcommand");
                }
                await Task.CompletedTask;
            });
            await Task.CompletedTask;
        }

        public static async Task ExecuteCommand(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            Match m = Regex.Match(line, "^(?<cmdName>[a-zA-Z0-9]+)( (?<args>.*))?$");
            if (!m.Success)
            {
                Console.WriteLine("Invalid command syntax!");
                Console.WriteLine("Syntax is \x1b[91m<cmdName> [args ...]\x1b[0m");
            }

            string cmdName = m.Groups["cmdName"].Value;
            if (commandAliases.TryGetValue(cmdName, out string actualCmd))
                cmdName = actualCmd;

            Instance instance = Program.instance;

            if (commands.TryGetValue(cmdName, out Func<string, Task> value))
            {
                await value(m.Groups["args"].Value);
            }
            else if (instance?.server?.HandleCommand(cmdName, m.Groups["args"].Value) == true)
            {

            }
            else
            {
                Console.WriteLine("Unknown command!");
            }
        }

    }
}
