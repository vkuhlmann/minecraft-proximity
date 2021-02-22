using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftProximity
{
    public interface ICoordinateReader
    {
        public Task<Coords?> GetCoords();
        public void SetScreen(int screen);
    }
}
