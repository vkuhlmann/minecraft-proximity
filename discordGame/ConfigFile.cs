using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace discordGame
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
            if (File.Exists(Path))
            {
                string configFileContent = File.ReadAllText(Path);
                Json = JObject.Parse(configFileContent);
                //JToken propAgreedTermsVersion = configFile["agreedTermsVersion"];
                //if (propAgreedTermsVersion != null && propAgreedTermsVersion.Type == JTokenType.Float)
                //    agreedTermsVersion = propAgreedTermsVersion.Value<float>();
            }
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
                    LegalAgreed = new
                    {

                    }
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
