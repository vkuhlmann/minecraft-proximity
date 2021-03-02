using System;
using System.Collections.Generic;
using System.Text;
using Serilog;

using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

namespace MinecraftProximity
{
    class Legal
    {
        public const string TERMS_VERSION = "2021-03-02T12:04Z";
        public const string PRIVACYPOLICY_VERSION = "2021-03-02T12:09Z";

        enum LegalFileType
        {
            TERMS,
            PRIVACY_POLICY
        }

        static readonly Dictionary<LegalFileType, string> versions = new Dictionary<LegalFileType, string>()
        {
            { LegalFileType.TERMS, TERMS_VERSION },
            { LegalFileType.PRIVACY_POLICY, PRIVACYPOLICY_VERSION }
        };

        static readonly Dictionary<LegalFileType, string> friendlyNames = new Dictionary<LegalFileType, string>()
        {
            { LegalFileType.TERMS, "Terms" },
            { LegalFileType.PRIVACY_POLICY, "Privacy policy" }
        };

        struct LegalFile
        {
            public string path;
            public LegalFileType type;
            public string majorVersion;
            public string minorVersion;
        }

        static bool AgreesWithLegalFile(LegalFileType type)
        {
            string requiredVersion = versions[type];

            LegalFile? legalFile = LocateLegalFile(type);
            bool isLegalFileValid = false;

            string friendlyName = friendlyNames[type];

            if (legalFile == null)
            {
                Log.Warning("[Legal] The {Type} cannot be found.", friendlyName);
                //Log.Information("[Legal] Current work directory is {Directory}", Directory.GetCurrentDirectory());
            }
            else
            {
                if (legalFile.Value.majorVersion == requiredVersion)
                    isLegalFileValid = true;
                else
                    Log.Warning("[Legal] The {Type} found is a wrong version!", friendlyName);
            }

            JToken obj = Program.configFile.Json["legalAgreed"];
            if (obj == null)
                obj = Program.configFile.Json["legalAgreed"] = new JObject();

            JToken a = obj[type.ToString()];
            if (a == null)
                a = obj[type.ToString()] = new JArray();

            if (!(a is JArray arr))
            {
                Log.Error("[Legal] Error reading config file.");
                return false;
            }

            foreach (JToken j in a)
            {
                if (j.Type == JTokenType.String && j.Value<string>() == requiredVersion)
                    return true;
            }

            if (!isLegalFileValid)
            {
                Console.WriteLine($"The {friendlyName} has been updated, but the version cannot be found in the installation directory. Please verify your installation.");
                return false;
            }

            if (a.Count() > 0)
                Console.WriteLine($"The {friendlyName} has been updated (version {legalFile.Value.majorVersion}).");


            List<string> inlineLines = new List<string>();

            bool brokenShort = false;
            int maxInlineLineCount = 15;
            bool alreadyContent = false;

            foreach (string line in File.ReadLines(legalFile.Value.path))
            {
                if (line.StartsWith("% minecraft-proximity")
                    || line.StartsWith("% MAJOR-VERSION")
                    || line.StartsWith("% MINOR-VERSION"))
                    continue;

                if (line.StartsWith("% EXTENDED"))
                {
                    brokenShort = true;
                    break;
                }

                if (string.IsNullOrWhiteSpace(line) && !alreadyContent)
                    continue;

                alreadyContent = true;

                inlineLines.Add(line);
                if (inlineLines.Count > maxInlineLineCount)
                    break;
            }

            if (inlineLines.Count == 0)
                return true;

            if (inlineLines.Count <= maxInlineLineCount)
            {
                Console.WriteLine();
                Console.WriteLine($"The {friendlyName}:");
                Console.WriteLine();
                foreach (string line in inlineLines)
                    Console.WriteLine(line);

                if (brokenShort)
                {
                    Console.WriteLine($"The extended version can be found at");
                    Console.WriteLine($"  {legalFile.Value.path}");
                }
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine($"Use of this program is subject to the {friendlyName}, which can be found at");
                Console.WriteLine($"  {legalFile.Value.path}");
            }

            //Console.WriteLine("Working with the Discord API and some dependencies involves some legal talk,");
            //Console.WriteLine("which requires your explicit consent before the program can be used.");
            //Console.WriteLine();
            //    Console.WriteLine($"Use of this program is subject to the {friendlyName}, which can be found at");
            //    Console.WriteLine($"  {legalFile.Value.path}");
            //bool hasAgreed = AskYesNoQuestion($"Do you agree with the {friendlyName}?");
            bool hasAgreed = AskYesNoQuestion($"\x1b[92mDo you agree with the {friendlyName}? (Y)es/(N)o\x1b[0m");
            if (!hasAgreed)
                return false;

            arr.Add(legalFile.Value.majorVersion);
            Program.configFile.Save();
            Log.Information("[Legal] The choice has been saved in the config file :)");
            Console.WriteLine();
            return true;
        }

        public static bool DoesUserAgree()
        {
            return AgreesWithLegalFile(LegalFileType.TERMS)
                && AgreesWithLegalFile(LegalFileType.PRIVACY_POLICY);
        }


        static string GetUpdateMessage(LegalFileType type, string version, string previousVersion)
        {
            return null;
        }

        static LegalFile? LocateLegalFile(LegalFileType type)
        {
            switch (type)
            {
                case LegalFileType.TERMS:
                    return LocateTerms();
                case LegalFileType.PRIVACY_POLICY:
                    return LocatePrivacyPolicy();
                default:
                    return null;
            }
        }

        static LegalFile? LocateTerms()
        {
            //DirectoryInfo dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            //DirectoryInfo dir = Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location);

            //string filePath = null;
            //while (dir != null)
            //{
            //    filePath = Path.Combine(dir.FullName, "LICENSES.txt");
            //    if (File.Exists(filePath))
            //        break;
            //    dir = dir.Parent;
            //}
            //if (dir == null)
            //    return null;

            if (Program.pythonDir == null)
                return null;

            string filePath = Path.Combine(Program.pythonDir.FullName, "LICENSES.txt");
            if (!File.Exists(filePath))
                return null;


            string[] lead = File.ReadLines(filePath).Take(3).ToArray();
            Match m = Regex.Match(lead[0], "% minecraft-proximity terms");
            if (!m.Success)
                return null;

            Match maj = Regex.Match(lead[1], "% MAJOR-VERSION: (?<version>.*)");
            Match min = Regex.Match(lead[1], "% MINOR-VERSION: (?<version>.*)");
            if (!maj.Success)
                return null;

            string majorVersion = maj.Groups["version"].Value;
            string minorVersion = majorVersion;
            if (min.Success)
                minorVersion = min.Groups["version"].Value;

            return new LegalFile
            {
                path = filePath,
                type = LegalFileType.TERMS,
                majorVersion = majorVersion,
                minorVersion = minorVersion
            };
        }

        static LegalFile? LocatePrivacyPolicy()
        {
            //DirectoryInfo dir = Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location);

            //string filePath = null;
            //while (dir != null)
            //{
            //    filePath = Path.Combine(dir.FullName, "privacyPolicy.txt");
            //    if (File.Exists(filePath))
            //        break;
            //    dir = dir.Parent;
            //}
            //if (dir == null)
            //    return null;

            if (Program.pythonDir == null)
                return null;

            string filePath = Path.Combine(Program.pythonDir.FullName, "privacyPolicy.txt");
            if (!File.Exists(filePath))
                return null;


            string[] lead = File.ReadLines(filePath).Take(3).ToArray();
            Match m = Regex.Match(lead[0], "% minecraft-proximity privacy policy");
            if (!m.Success)
                return null;

            Match maj = Regex.Match(lead[1], "% MAJOR-VERSION: (?<version>.*)");
            Match min = Regex.Match(lead[2], "% MINOR-VERSION: (?<version>.*)");
            if (!maj.Success)
                return null;

            string majorVersion = maj.Groups["version"].Value;
            string minorVersion = majorVersion;
            if (min.Success)
                minorVersion = min.Groups["version"].Value;

            return new LegalFile
            {
                path = filePath,
                type = LegalFileType.TERMS,
                majorVersion = majorVersion,
                minorVersion = minorVersion
            };
        }

        static bool AskYesNoQuestion(string question)
        {
            Console.Write($"{question} (Y/N) ");
            while (true)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y')
                {
                    Console.WriteLine("Y");
                    return true;
                }
                else if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N')
                {
                    Console.WriteLine("N");
                    return false;
                }
            }
        }
    }
}
