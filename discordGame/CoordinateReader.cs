using System;
using System.Collections.Generic;
using System.Text;
//using IronPython;
using Serilog;
using Python.Runtime;
using System.Threading.Tasks;
using System.IO;


namespace discordGame
{
	public struct Coords
	{
		public float x;
		public float y;
		public float z;

		public Coords(float x, float y, float z)
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

	class CoordinateReader
	{
		PyScope scope;
		dynamic coordReaderPy;

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

			//string orig = Environment.GetEnvironmentVariable("PYTHONPATH") ?? PythonEngine.PythonPath;
			//Environment.SetEnvironmentVariable("PYTHONPATH", orig + $";{libPath}", EnvironmentVariableTarget.Process);

			//string orig = Environment.GetEnvironmentVariable("PATH") ?? "";
			//Environment.SetEnvironmentVariable("PATH", orig + $";{libPath};", EnvironmentVariableTarget.Process);

			//Environment.SetEnvironmentVariable("PYTHONPATH", $"{libPath};", EnvironmentVariableTarget.Process);

			//Func<Task> setupPython = async () =>
			//{

			//};

			//Task task = setupPython();
			//task.Wait();

			Task task = Program.pythonSetupTask;
			task.Wait();
			Log.Information("Initializing CoordinateReader");
		
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

		public Coords? GetCoords()
		{
			using (Py.GIL())
			{
				dynamic retValue;
				try
				{
					retValue = coordReaderPy.getCoordinates();

				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error getting coords: {ex}");
					return null;
				}
				if (retValue == null)
					return null;
				float x = retValue["x"];
				float y = retValue["y"];
				float z = retValue["z"];

				return new Coords(x, y, z);
			}
		}

		public void SetScreen(int screen)
		{
			using (Py.GIL())
			{
				coordReaderPy.setScreen(screen);
			}
		}
	}
}
