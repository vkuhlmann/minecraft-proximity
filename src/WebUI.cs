using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Python.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace MinecraftProximity
{
    public class WebUI
    {
        PyScope scope;
        dynamic module;
        Action<string> updateDelegate;
        Instance instance;

        public WebUI(Instance instance)
        {
            this.instance = instance;
            scope = null;
            updateDelegate = null;
        }

        public void Start()
        {
            Task task = PythonManager.pythonSetupTask;
            task.Wait();
            Log.Information("[WebUI] Initializing...");

            string libPath = Program.pythonDir.FullName;
            if (libPath == null)
            {
                //Log.Information("[WebUI] LibPath is null. Cancelling start.");
                throw new Exception("LibPath is null! Can't start WebUI.");
            }

            updateDelegate = (string data) =>
            {
                SendUpdate(data);
            };

            using (Py.GIL())
            {
                scope = Py.CreateScope();
                IEnumerable<string> imports = new List<string> { "sys", "webui" };
                Dictionary<string, dynamic> modules = new Dictionary<string, dynamic>();

                foreach (string import in imports)
                {
                    modules[import] = scope.Import(import);
                    //Console.WriteLine($"Imported {import}");
                }

                module = modules["webui"];

                module.start_webui(libPath, updateDelegate);
                //dynamic inst = coordinateReader.CoordinateReader.Create();
                //coordReaderPy = inst;
            }
            Log.Information("[WebUI] Initialization done.");
        }

        public void Stop()
        {
            Log.Information("[WebUI] Signaling shut down.");
            using (Py.GIL())
            {
                if (module == null)
                    return;
                module.stop_webui();

                module = null;
                scope = null;
            }
            Log.Information("[WebUI] Shut down.");
        }

        public void ReceiveUpdate(string data)
        {
            using (Py.GIL())
            {
                if (module == null)
                    return;
                module.put_data(data);
            }
        }

        public void SendUpdate(string data)
        {
            if (instance.client == null)
                return;

            JObject message = JObject.FromObject(new
            {
                type = "updatemap",
                data = JObject.Parse(data)
            });


            //transmitsProcessing.Enqueue(true);
            long? dest = instance.client?.serverUser;
            if (dest > 0)
            {
                instance.client.voiceLobby.SendNetworkJson(dest.Value, 2, message);
            } else
            {
                Log.Warning("Can't send update: no server user!");
            }
        }

        public bool PythonHandleCommand(string subcommand, string args)
        {
            try
            {
                using (Py.GIL())
                {
                    if (module == null)
                        return false;
                    return module.handle_command(subcommand, args);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Python raised an error trying to do HandleCommand: {Message}\n{StackTrace}", ex.Message, ex.StackTrace);
                return false;
            }
        }

        public void UpdatePlayers(string data)
        {
            using (Py.GIL())
            {
                if (module == null)
                    return;
                module.set_players(data);
            }
        }

        //public void HandleXZCommand(string args)
        //{
        //    if (module == null)
        //    {
        //        Console.WriteLine("Module is null! Try reinstantiating the webui.");
        //        return;
        //    }

        //    Match m = Regex.Match(args, "^((?<x>(\\+|-|)\\d+) (?<z>(\\+|-|)\\d+))?$");
        //    if (!m.Success)
        //    {
        //        Console.WriteLine("Invalid command syntax!");
        //        Console.WriteLine("Syntax is \x1b[91mwebui xz [<newX> <newZ>]\x1b[0m");
        //    }

        //    if (!m.Groups["x"].Success)
        //    {
        //        Console.WriteLine();
        //    }

        //}
    }
}
