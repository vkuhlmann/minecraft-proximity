using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Python.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MinecraftProximity
{
    class WebUI
    {
        PyScope scope;
        dynamic module;
        Action<string> updateDelegate;

        public void Start()
        {
            Task task = PythonManager.pythonSetupTask;
            task.Wait();
            Log.Information("[WebUI] Initializing...");

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

                module.start_webui(updateDelegate);
                //dynamic inst = coordinateReader.CoordinateReader.Create();
                //coordReaderPy = inst;
            }
            Log.Information("[WebUI] Initialization done.");
        }

        public void Stop()
        {
            using (Py.GIL())
            {
                if (module == null)
                    return;
                module.stop_webui();
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
            if (Program.client == null)
                return;

            JObject message = JObject.FromObject(new
            {
                action = "updatemap",
                data = JObject.Parse(data)
            });

            //transmitsProcessing.Enqueue(true);
            Program.client.voiceLobby.SendNetworkJson(Program.client.serverUser, 3, message);
        }
    }
}
