using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Serilog;

namespace MinecraftProximity
{
    public struct UpdateRate
    {
        public TimeSpan baseInterval;
        public TimeSpan optimalInterval;
    }

    public class ConfigFile
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



        struct UpdateRateRaw
        {
            public string baseInterval;
            public string optimalInterval;
            public int priority;
        }

        static Regex rateRegex = new Regex("^(?<num>(\\+|-|)\\d+(\\.\\d+)?|\\.\\d+)(/(?<denom>\\d+(\\.\\d+)?))?$");

        static readonly SortedDictionary<string, UpdateRateRaw> DEFAULT_UPDATERATES = new SortedDictionary<string, UpdateRateRaw>
        {
            //{ "client_sendcoords", "1/8" },
            //{ "client_sendcoords_optimal", "1/10" }
            { "client_sendcoords", new UpdateRateRaw {
                baseInterval = "1/8",
                optimalInterval = "1/10",
                priority = 100
            }},
            { "client_sendcoords_performanceStats", new UpdateRateRaw {
                baseInterval = "20"
            }},
            { "coordinatesreader_calibrate", new UpdateRateRaw {
                baseInterval = "10"
            }},
            { "coordinatesreader_print", new UpdateRateRaw {
                baseInterval = "30"
            }},
            { "coordinatesreader_performanceStats", new UpdateRateRaw {
                baseInterval = "-5"
            }},
            { "coordinatesreader_notCalibratedWarning", new UpdateRateRaw {
                baseInterval = "15"
            }},
            {
                "instanceloop_performanceStats", new UpdateRateRaw
                {
                    baseInterval = "-20"
                }
            },
            {
                "server_calculateVolumes_performanceStats", new UpdateRateRaw
                {
                    baseInterval = "20"
                }
            },
            {
                "server_coordinatesRate_measure", new UpdateRateRaw
                {
                    baseInterval = "2"
                }
            },
            {
                "server_calculate_performanceStats", new UpdateRateRaw
                {
                    baseInterval = "20"
                }
            },
            {
                "server_calculate", new UpdateRateRaw
                {
                    baseInterval = "1/10",
                    optimalInterval = "1/10",
                    priority = 100
                }
            }
        };

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
            UpdateDefaultRates();
            Save();
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
                    multiDiscord = false,
                    updateRates = new
                    {
                        __comment1 = "A preceding underscore means: use system default. Two      ",
                        __comment2 = "preceding underscores: keep in sync with system default. If",
                        __comment3 = "you want to modify a value, remove the underscores and set ",
                        __comment4 = "your value.                                                ",
                        __comment5 = "Sometimes negative values are allowed. They mean: disable. "
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

        public void UpdateDefaultRates()
        {
            JToken updateRatesToken = Json["updateRates"];
            if (updateRatesToken == null)
                updateRatesToken = Json["updateRates"] = new JObject();

            JObject updateRates = updateRatesToken.Value<JObject>();

            FindUnrecognizedRates(updateRates, "");

            foreach (string key in DEFAULT_UPDATERATES.Keys)
            {
                string strValue = null;
                if (DEFAULT_UPDATERATES[key].optimalInterval == null)
                    strValue = DEFAULT_UPDATERATES[key].baseInterval;

                JToken tok = Locate(updateRates, key);
                if (tok == null)
                {
                    string[] split = key.Split('_');
                    string main = split[0];
                    string sub = string.Join('_', split.Skip(1));

                    if (!updateRates.TryGetValue(main, out JToken mainPart))
                        mainPart = updateRates[main] = new JObject();

                    if (strValue != null)
                        tok = mainPart[sub] = $"__";
                    else
                        tok = mainPart[sub] = new JObject();
                }

                if (UpdateLiteralValues(tok, strValue, out tok))
                    continue;

                if (tok.Type != JTokenType.Object)
                {
                    Log.Warning($"[config.json] Unrecognized type found when updating default rates. ({key})");
                    continue;
                }

                UpdateRateRaw defaultVal = DEFAULT_UPDATERATES[key];

                JObject obj = tok.Value<JObject>();
                if (obj.ContainsKey("optimal") && defaultVal.optimalInterval == null)
                    Log.Warning($"[config.json] Optimal value for {key} is not supported.");

                if (obj.ContainsKey("base"))
                {
                    if (!UpdateLiteralValues(obj["base"], defaultVal.baseInterval, out JToken newTok))
                        Log.Warning($"[config.json] Unrecognized type found when updating default rates. ({key}, base)");
                }
                else
                {
                    obj["base"] = "__" + defaultVal.baseInterval;
                }

                if (defaultVal.optimalInterval != null)
                {
                    if (obj.ContainsKey("optimal"))
                    {
                        if (!UpdateLiteralValues(obj["optimal"], defaultVal.optimalInterval, out JToken newTok))
                            Log.Warning($"[config.json] Unrecognized type found when updating default rates. ({key}, optimal)");
                    }
                    else
                    {
                        obj["optimal"] = "__" + defaultVal.optimalInterval;
                    }
                }
            }
        }

        public bool UpdateLiteralValues(JToken tok, string strValue, out JToken newTok)
        {
            newTok = tok;
            if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
                return true;

            if (tok.Type == JTokenType.String)
            {
                if (!tok.Value<string>().StartsWith("__"))
                    return true;
                if (strValue != null)
                {
                    tok.Replace($"__{strValue}");
                    return true;
                }
                else
                {
                    JObject a = new JObject();
                    tok.Replace(a);
                    newTok = a;
                    return false;
                }
            }
            return false;
        }

        public JToken Locate(JObject obj, string path)
        {
            string scopePart = path;
            string specPath = "";

            while (true)
            {
                if (obj.ContainsKey(scopePart))
                {
                    if (specPath.Length == 0)
                    {
                        return obj[scopePart];
                    }
                    else if (obj.Type == JTokenType.Object)
                    {
                        JToken val = Locate(obj[scopePart].Value<JObject>(), specPath);
                        if (val != null)
                            return val;
                    }
                }

                int split = scopePart.LastIndexOf('_');
                if (split == -1)
                    return null;

                if (specPath.Length > 0)
                    specPath = "_" + specPath;
                specPath = scopePart.Substring(split + 1) + specPath;
                scopePart = scopePart.Substring(0, split);
            }
        }

        public void FindUnrecognizedRates(JObject obj, string builtup)
        {
            if (obj.ContainsKey("base"))
            {
                if (DEFAULT_UPDATERATES.ContainsKey(builtup))
                    return;
                Log.Warning($"[config.json] UpdateRate entry {builtup} is unrecognized.");
                return;
            }

            string precede = builtup;
            if (precede.Length > 0)
                precede += "_";

            foreach (KeyValuePair<string, JToken> pair in obj)
            {
                if (pair.Value.Type == JTokenType.Object)
                {
                    FindUnrecognizedRates(pair.Value.Value<JObject>(), precede + pair.Key);
                }
                else
                {
                    string path = precede + pair.Key;
                    if ( !path.StartsWith("_") && !DEFAULT_UPDATERATES.ContainsKey(path))
                        Log.Warning($"[config.json] UpdateRate entry {path} is unrecognized.");
                }
            }
        }

        public UpdateRate GetUpdateRate(string name, bool mustBePositive)
        {
            JToken updateRates = Json["updateRates"];
            if (updateRates == null)
                updateRates = Json["updateRates"] = new JObject();

            //JToken rate = updateRates[name];
            JToken rate = Locate(updateRates.Value<JObject>(), name);
            if (rate == null)
            {
                rate = new JObject();
            }
            else if (rate.Type != JTokenType.Object)
            {
                rate = JObject.FromObject(new
                {
                    @base = rate
                });
            }

            string fallbackBase = DEFAULT_UPDATERATES[name].baseInterval;
            TimeSpan baseInterval = ParseRateToken(rate["base"], fallbackBase, mustBePositive);

            TimeSpan optimalInterval;

            string fallbackOptimal = DEFAULT_UPDATERATES[name].optimalInterval;
            if (fallbackOptimal != null)
            {
                optimalInterval = ParseRateToken(rate["optimal"], fallbackOptimal, mustBePositive);

                if (optimalInterval > baseInterval)
                {
                    baseInterval = optimalInterval;
                    Log.Warning("[config.json] Warning: base is quicker than optimal. Using optimal as base. ({Path})", name);
                }
            } else
            {
                optimalInterval = baseInterval;
            }

            return new UpdateRate
            {
                baseInterval = baseInterval,
                optimalInterval = optimalInterval
            };
        }

        public TimeSpan ParseRateToken(JToken tok, string fallback, bool mustBePositive)
        {
            string val = fallback;
            if (tok != null)
            {
                if (tok.Type == JTokenType.String)
                    val = tok.Value<string>();
                else if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float)
                    return TimeSpan.FromSeconds(tok.Value<float>());
                else
                    Log.Warning("Wrong type for rate in config.json!");
            }

            Match m = rateRegex.Match(val);
            if (!m.Success || (m.Groups["num"].Value.StartsWith("-") && mustBePositive))
                m = rateRegex.Match(fallback);

            float num = 1.0f;
            if (m.Groups["num"].Success)
                num = float.Parse(m.Groups["num"].Value);
            float denom = 1.0f;
            if (m.Groups["denom"].Success)
                denom = float.Parse(m.Groups["denom"].Value);

            if (mustBePositive)
                return TimeSpan.FromSeconds(Math.Abs(num) / denom);
            else
                return TimeSpan.FromSeconds(num / denom);
        }
    }
}
