﻿using System;
using System.Collections.Generic;
using System.Text;
using Python.Included;
using Python.Runtime;
using System.Threading.Tasks;
using System.IO;
using System.Drawing;
using Serilog;

namespace discordGame
{
    class PythonManager
    {
        public static Task pythonSetupTask;

        public static async Task<Rectangle[]> GetScreenRects()
        {
            await pythonSetupTask;
            using (Py.GIL())
            {
                //PyScope scope = Py.CreateScope();
                dynamic screeninfo = Py.Import("screeninfo");

                PyList monitors = PyList.AsList(screeninfo.get_monitors());
                Rectangle[] rects = new Rectangle[monitors.Length()];

                for (int i = 0; i < rects.Length; i++)
                {
                    int x = monitors[i].GetAttr("x").As<int>();
                    int y = monitors[i].GetAttr("y").As<int>();
                    int width = monitors[i].GetAttr("width").As<int>();
                    int height = monitors[i].GetAttr("height").As<int>();

                    rects[i] = new Rectangle(x, y, width, height);
                }
                return rects;
            }
        }


        public static async Task SetupPython()
        {
            //string libPath = @"D:\Projects\minecraft-proximity";
            DirectoryInfo assemblyDir = Directory.GetParent(System.Reflection.Assembly.GetEntryAssembly().Location);
            DirectoryInfo dir = assemblyDir;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "coordinatereader.py")))
                    break;
                dir = dir.Parent;
            }

            string libPath;
            if (dir != null)
                libPath = dir.FullName;
            else
                libPath = assemblyDir.FullName;

            Log.Information("[Python] Setting up...");
            await Installer.SetupPython();

            //PythonEngine.PythonPath += ";";

            bool pipInstalled = Installer.TryInstallPip();
            if (pipInstalled)
                Log.Information($"Installed pip");

            Installer.PipInstallModule("numpy");
            //Console.WriteLine($"Installed numpy");

            Installer.PipInstallModule("pillow");
            //Console.WriteLine($"Installed pillow");

            Installer.PipInstallModule("screeninfo");
            //Console.WriteLine($"Installed screeninfo");

            PythonEngine.Initialize();

            using (Py.GIL())
            {
                dynamic sys = PythonEngine.ImportModule("sys");
                Log.Information("[Python] Setup done. Version: " + sys.version);
                sys.path.append(libPath);
                //Console.WriteLine($"Sys.path: {sys.path}");
            }
            PythonEngine.BeginAllowThreads();
        }

    }
}