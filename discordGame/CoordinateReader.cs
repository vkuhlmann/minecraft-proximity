using System;
using System.Collections.Generic;
using System.Text;
//using IronPython;
using Serilog;
using Python.Included;
using Python.Runtime;
using System.Threading.Tasks;
using System.IO;


namespace discordGame
{
	class CoordinateReader
	{
		PyScope scope;
		dynamic coordReaderPy;

		public struct Coord
		{
			float x;
			float y;
			float z;

			public Coord(float x, float y, float z)
			{
				this.x = x;
				this.y = y;
				this.z = z;
			}

			public override string ToString()
			{
				return $"{x}, {y}, {z}";
			}
		}

		public CoordinateReader()
		{
			//var engine = IronPython.Hosting.Python.CreateEngine();

			//ICollection<string> searchPaths = engine.GetSearchPaths();
			//searchPaths.Add(@"D:\Programs\Python39\Lib");
			//searchPaths.Add(@"D:\Programs\Python39\Lib\site-packages");
			//engine.SetSearchPaths(searchPaths);

			//var scope = engine.CreateScope();
			////var source = engine.CreateScriptSourceFromFile(@"D:\Projects\minecraft-proximity\coordinateReader.py");
			//var source = engine.CreateScriptSourceFromFile(@"D:\Projects\minecraft-proximity\test.py");
			//object ans = source.Execute(scope);
			//Log.Information("Script execute ans was {Ans}", ans);

			//IEnumerable<string> variableNames = scope.GetVariableNames();
			//Console.WriteLine("Found Python variables:");
			//foreach (string s in variableNames)
			//{
			//	Console.WriteLine($"  {s}");
			//}
			//Console.WriteLine();
			//self.coordReader = coordinateReader.CoordinateReader()

			string libPath = @"D:\Projects\minecraft-proximity";

			//string orig = Environment.GetEnvironmentVariable("PYTHONPATH") ?? PythonEngine.PythonPath;
			//Environment.SetEnvironmentVariable("PYTHONPATH", orig + $";{libPath}", EnvironmentVariableTarget.Process);

			//string orig = Environment.GetEnvironmentVariable("PATH") ?? "";
			//Environment.SetEnvironmentVariable("PATH", orig + $";{libPath};", EnvironmentVariableTarget.Process);

			//Environment.SetEnvironmentVariable("PYTHONPATH", $"{libPath};", EnvironmentVariableTarget.Process);

			Func<Task> setupPython = async () =>
			{
				await Installer.SetupPython();

				//PythonEngine.PythonPath += ";";

				bool pipInstalled = Installer.TryInstallPip();
				Console.WriteLine($"Install pip returned {pipInstalled}");

				Installer.PipInstallModule("numpy");
				Console.WriteLine($"Installed numpy");

				Installer.PipInstallModule("pillow");
				Console.WriteLine($"Installed pillow");

				Installer.PipInstallModule("screeninfo");
				Console.WriteLine($"Installed screeninfo");

				//Console.WriteLine($"PYTHONPATH was {PythonEngine.PythonPath}");
				//Console.WriteLine($"Win PYTHONPATH was {Environment.GetEnvironmentVariable("PYTHONPATH")}");

				//PythonEngine.PythonPath += $";\"{libPath}\"";


				//PythonEngine.

				PythonEngine.Initialize();
				//Console.WriteLine($"PYTHONPATH was {PythonEngine.PythonPath}");
				//Console.WriteLine($"Win PYTHONPATH was {Environment.GetEnvironmentVariable("PYTHONPATH")}");

				//string libPath = @"D:\Projects\minecraft-proximity";
				//PythonEngine.PythonPath += $";\"{libPath}\"";
				//PythonEngine.PythonPath += $";{libPath}";

				//Console.WriteLine($"PYTHONPATH is now {PythonEngine.PythonPath}");

				//Console.WriteLine($"PYTHONPATH is now {PythonEngine.PythonPath}\n");
				//Console.WriteLine($"Win PYTHONPATH is now {Environment.GetEnvironmentVariable("PYTHONPATH")}\n");
				//Console.WriteLine($"Win PATH is now {Environment.GetEnvironmentVariable("PATH")}\n");

				dynamic sys = PythonEngine.ImportModule("sys");
				Console.WriteLine("Python version: " + sys.version);
				sys.path.append(libPath);
				Console.WriteLine($"Sys.path: {sys.path}");
			};

			Task task = setupPython();
			task.Wait();

		
			using (Py.GIL())
			{
				scope = Py.CreateScope();
				IEnumerable<string> imports = new List<string> { "sys", "numpy", "coordinateReader" };
				Dictionary<string, dynamic> modules = new Dictionary<string, dynamic>();

				foreach (string import in imports)
				{
					modules[import] = scope.Import(import);
					Console.WriteLine($"Imported {import}");
				}

				dynamic coordinateReader = modules["coordinateReader"];
				dynamic inst = coordinateReader.CoordinateReader.Create();
				coordReaderPy = inst;

				//Console.WriteLine();

				//scope.Import("numpy");

				//dynamic numpy = Py.Import("numpy");
				//Console.WriteLine("Numpy version: " + numpy.__version__);


				//Console.WriteLine($"Sys.path: {sys.path}");
				//sys.path.append(libPath);
				//Console.WriteLine($"Sys.path: {sys.path}");


				//PythonEngine.Exec("doStuff()");
				//dynamic np = Py.Import("numpy");
				//Console.WriteLine(np.cos(np.pi * 2));

				//dynamic sin = np.sin;
				//Console.WriteLine(sin(5));

				//double c = np.cos(5) + sin(5);
				//Console.WriteLine(c);

				//dynamic a = np.array(new List<float> { 1, 2, 3 });
				//Console.WriteLine(a.dtype);

				//dynamic b = np.array(new List<float> { 6, 5, 4 }, dtype: np.int32);
				//Console.WriteLine(b.dtype);

				//Console.WriteLine(a * b);

				//Console.WriteLine();



				//PyScope p = Py.CreateScope();
				//p.Import()
				//p.
				//dynamic coordinateReader = Py.Import(Path.Combine(libPath, "coordinateReader.py"));

				//dynamic coordinateReader = Py.Import("coordinateReader");
				//Console.WriteLine($"coordinateReader: {coordinateReader}");

				//dynamic cl = coordinateReader.CoordinateReader;
				//Console.WriteLine($"coordinateReader.CoordinateReader: {cl}");

				////dynamic instance = cl.__init__();
				////Console.WriteLine($"instance: {instance}");

				//dynamic instance = cl.Create();
				//Console.WriteLine($"instance: {instance}");

				
				
			}
		}

		public Coord? GetCoords()
		{
			dynamic retValue;
			try
			{
				retValue = coordReaderPy.getCoordinates();

			}catch(Exception ex)
			{
				Console.WriteLine($"Error getting coords: {ex}");
				return null;
			}
			if (retValue == null)
				return null;
			float x = retValue["x"];
			float y = retValue["y"];
			float z = retValue["z"];
			return new Coord(x, y, z);
		}
	}
}
