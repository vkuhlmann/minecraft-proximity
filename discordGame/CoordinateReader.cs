using System;
using System.Collections.Generic;
using System.Text;
//using IronPython;
using Serilog;
using Python.Runtime;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;


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

	class CoordinateReader : ICoordinateReader
	{
		PyScope scope;
		dynamic coordReaderPy;

		long measureStart;
		Stopwatch stopwatch;
		int requests;
		long measureEnd;
		TimeSpan measureDur;

		public CoordinateReader()
		{
			stopwatch = new Stopwatch();
			measureDur = TimeSpan.FromSeconds(5);
			measureStart = Environment.TickCount64;
			measureEnd = measureStart + (long)measureDur.TotalMilliseconds;
			requests = 0;

			Task task = Program.pythonSetupTask;
			task.Wait();
			Log.Information("[CoordinateReader] Initializing Python CoordinateReader...");
		
			using (Py.GIL())
			{
				scope = Py.CreateScope();
				IEnumerable<string> imports = new List<string> { "sys", "numpy", "coordinatereader" };
				Dictionary<string, dynamic> modules = new Dictionary<string, dynamic>();

				foreach (string import in imports)
				{
					modules[import] = scope.Import(import);
					//Console.WriteLine($"Imported {import}");
				}

				dynamic coordinateReader = modules["coordinatereader"];
				dynamic inst = coordinateReader.CoordinateReader.Create();
				coordReaderPy = inst;				
			}
			Log.Information("[CoordinateReader] Initialization done.");
		}

		public async Task<Coords?> GetCoords()
		{
			if (Environment.TickCount64 > measureEnd)
			{
				measureEnd = Environment.TickCount64;

				float durMs = (float)stopwatch.ElapsedMilliseconds / Math.Max(1, requests);
				//Log.Information("Coords getting takes {DurMs:F2} ms on average ({Req} requests completed)", durMs, requests);

				stopwatch.Reset();
				requests = 0;

				measureStart = measureEnd;
				measureEnd = measureStart + (long)measureDur.TotalMilliseconds;
			}

			Task<Coords?> t = Task.Run(new Func<Coords?>(() =>
			{
				stopwatch.Start();
				try
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
							Log.Error("Error getting coords: {Exception}", ex);
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
				finally
				{
					requests += 1;
					stopwatch.Stop();
				}
			}));
			return await t;
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
