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
        public static async Task DoHandleLoop(CancellationToken cancTok)
        {
            while (!Program.isQuitRequested)
            {
                cancTok.ThrowIfCancellationRequested();
                string line = Console.ReadLine();
                if (line == null)
                    break;
                try
                {
                    //Program.nextTasks.Enqueue(async () =>
                    //{
                    try
                    {
                        await ExecuteCommand(line);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"An error occured while executing the command: {ex}");
                    }
                    //                    });
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
            { "webui", DoWebUICommand }
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

            Program.isQuitRequested = true;
            await Task.CompletedTask;
        }

        static async Task DoCreateLobbyCommand(string argument)
        {
            if (argument != "")
            {
                Console.WriteLine("Quit command does not take any argument.");
                return;
            }

            Program.nextTasks.Enqueue(async () =>
            {
                await Program.createLobbyIfNone();
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

            Program.nextTasks.Enqueue(async () =>
            {
                Program.DoHost();
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

            Program.nextTasks.Enqueue(async () =>
            {
                Program.currentLobby?.SendBroadcast(argument);
                await Task.CompletedTask;
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

            Program.nextTasks.Enqueue(async () =>
            {
                if (Program.client == null)
                {
                    Console.WriteLine("Client is null");
                    return;
                }

                Program.client.coordsReader.SetScreen(int.Parse(argument));
            });
            await Task.CompletedTask;
        }

        static async Task DoOverlayCommand(string argument)
        {
            Program.nextTasks.Enqueue(async () =>
            {
                var overlayManager = Program.discord.GetOverlayManager();
                overlayManager.OpenVoiceSettings((result) =>
                {
                    if (result == Discord.Result.Ok)
                    {
                        Console.WriteLine("The overlay has been opened in Discord");
                    }
                });
                await Task.CompletedTask;
            });
            await Task.CompletedTask;
        }

        static async Task DoWebUICommand(string argument)
        {
            Program.nextTasks.Enqueue(async () =>
            {
                if (argument == "start")
                {
                    if (Program.webUI != null)
                    {
                        Console.WriteLine("WebUI already exists!");
                        return;
                    }
                    Program.webUI = new WebUI();
                    Program.webUI.Start();
                }
                else if (argument == "stop")
                {
                    Program.webUI?.Stop();
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

            if (commands.TryGetValue(m.Groups["cmdName"].Value, out Func<string, Task> value))
            {
                await value(m.Groups["args"].Value);
            }
            else if (Program.server != null && Program.server.HandleCommand(cmdName, m.Groups["args"].Value))
            {

            }
            else
            {
                Console.WriteLine("Unknown command!");
            }
        }

    }
}
