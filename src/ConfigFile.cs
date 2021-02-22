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
            Path = path;
            Reload();
        }

        public void Reload()
        {
            if (File.Exists(Path))
            {
                string configFileContent = File.ReadAllText(Path);
                Json = JObject.Parse(configFileContent);
            } else
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
