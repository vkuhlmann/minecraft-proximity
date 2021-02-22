using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;
using System.Numerics;

namespace MinecraftProximity
{
    class MinecraftFontReader
    {
        readonly Color WHITE_COLOR = Color.FromArgb(252, 252, 252);
        readonly Color GRAY_COLOR = Color.FromArgb(221, 221, 221);

        // \r\n(?<numb>[^:]+):(?<val>[^\r]+),( )*
        // \r\n{$+{numb},$+{val}},
        Dictionary<BigInteger, string> decipher = new Dictionary<BigInteger, string>{
            { 267450405182, "0"},
            { 275959988800, "1"},
            { 439316205922, "2"},
            { 233157771554, "3"},
            { 545747244056, "4"},
            { 245975303463, "5"},
            { 207387970108, "6"},
            { 30223171843, "7"},
            { 233157773622, "8"},
            { 129541687558, "9"},
            { 545595327103, "N"},
            { 280267933055, "E"},
            { 245975303458, "S"},
            { 545998774399, "W"},
            { 64, "."},
            { 4396167232, "/"},
            { 34494482440, "-"},
            { 68, ":"},
            { 34498021384, "+"},
            { 192, ","},
            { 4268572, "("},
            { 1843777, ")"},
            { 4276607, "["},
            { 8339777, "]"},
            { 1092752392, "<"},
            { 155225170980, "="},
            { 135537217, ">"},
            { 1108135182594, "~"},
            { 95, "!"},
            { 25926107394, "?"},
            { 551911719040, "_"}
        };


        int curX;
        int curY;
        Bitmap bitmap;
        Rectangle bounds;
        float scale;
        List<int> raster = new List<int>();

        public string Read(Bitmap bitmap, Rectangle bounds, float scale, string term = "     ")
        {
            curX = 0;
            curY = 0;
            this.bitmap = bitmap;
            this.bounds = bounds;
            this.scale = scale;

            StringBuilder a = new StringBuilder();
            while (true)
            {
                string c = ReadCharacter();
                if (c == null)
                    break;
                a.Append(c);
            }
            return a.ToString();
        }

        //string Read(Bitmap bitmap, Rectangle bounds, float scale, string term = "     ")
        string ReadCharacter()
        {
            raster.Clear();

            int nonZeroLength = 0;
            int spaceLength = 0;

            int width = (int)(bounds.Width / scale);
            int height = (int)(bounds.Height / scale);
            Color col;

            for (int x = curX; x < width; x++)
            {
                int val = 0;
                int count = 0;
                for (int y = 0; y < 8 && y + curY < height; y++)
                {
                    col = bitmap.GetPixel((int)(bounds.X + x * scale), (int)(bounds.Y + (y + curY) * scale));
                    if (col == WHITE_COLOR || col == GRAY_COLOR)
                    {
                        count += 1;
                        val += 1 << y;
                    }
                }
                if (val != 0 || nonZeroLength > 0)
                    raster.Add(val);

                if (count == 0)
                {
                    spaceLength += 1;
                    if (nonZeroLength != 0)
                    {
                        while (raster[0] == 0)
                            raster.RemoveAt(0);
                        string symb = decodeSymbol(raster);
                        raster.Clear();
                        curX = x + 1;
                        return symb;
                    }

                    if (spaceLength > 3)
                    {
                        curX = x;
                        return " ";
                    }
                } else
                {
                    nonZeroLength++;
                    spaceLength = 0;
                }
            }
            return null;
        }

        string decodeSymbol(List<int> raster)
        {
            BigInteger superVal = 0;
            for (int i = 0; i < raster.Count; i++)
                superVal += new BigInteger(raster[i]) << (8 * i);

            if (decipher.TryGetValue(superVal, out string value))
                return value;
            else
                return $"[{superVal}]";
        }

        //def readCharacter(self, im, pixels, scale):
        //raster = []
        //nonZeroLength = 0
        //spaceLength = 0
        //for x in range(self.x, im.size[0] // scale):
        //    val = 0
        //    count = 0
        //    for y in range(0, im.size[1] // scale):
        //        col = pixels[x * scale, y * scale][:3]
        //        if col == WHITE_COLOR or col == GRAY_COLOR:
        //            count += 1
        //            val += 2**y
        //    if val != 0 or nonZeroLength > 0:
        //        raster += [val]

        //    if count == 0:
        //        spaceLength += 1
        //        if nonZeroLength != 0:
        //            while raster[0] == 0:
        //                raster = raster[1:]
        //            symb = fontdecoder.decodeSymbol(raster)
        //            raster = []

        //            self.x = x + 1
        //            return symb
        //            # if superVal in DECIPHER:
        //            #     return DECIPHER[superVal]
        //            # else:
        //            #     return f"[{superVal}]"
        //            # print(nonZeroLength)
        //        nonZeroLength = 0
        //        if spaceLength > 3:
        //            self.x = x
        //            return " "
        //    else:
        //        nonZeroLength += 1
        //        spaceLength = 0
        //return None
    }
}
