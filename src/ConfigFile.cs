using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace MinecraftProximity
{
    class ConfigFile
    {
        public string Path
        {
            get;
            protected set;
        }

        public JObject Json
        {
            get;
            protected set;
        }

        public ConfigFile(string path = "config.json")
        {
            DirectoryInfo dir;
            if (Program.exeFile != null)
            {
                dir = Directory.GetParent(Program.exeFile);
            }
            else
            {
                dir = Program.assemblyDir;
                if (dir.Name == "bin")
                    dir = dir.Parent;
            }

            Path = System.IO.Path.Combine(dir.FullName, path);
            Reload();
        }

        public void Reload()
        {
            if (File.Exists(Path))
            {
                string configFileContent = File.ReadAllText(Path);
                Json = JObject.Parse(configFileContent);
            }
            else
            {
                Json = JObject.FromObject(new
                {
                    legalAgreed = new
                    {

                    },
                    multiDiscord = false
                    //agreedTermsVersion = "None",
                    //agreedPrivacyPolicyVersion = "None"
                });
            }
        }

        public void Save()
        {
            if (Json == null)
                return;
            File.WriteAllText(Path, Json.ToString());
        }

    }
}
