from PIL import Image, ImageGrab
import positioner
import re

import logging
import threading
import time

# pip install Pillow
# pip install screeninfo
# pip install PyAudio-*.whl
# pip install numpy

DECIPHER = {
    2060: "0",
    2240: "1",
    2768: "2",
    1904: "3",
    2304: "4",
    1917: "5",
    1852: "6",
    641: "7",
    1940: "8",
    1252: "9",
    248: "-",
    2243: "N",
    2101: "E",
    1912: "S",
    2543: "W",
    64: "."
}

class CoordinateReader:
    def __init__(self):
        self.snippeter = positioner.Snippeter()
        self.snippeter.calibrate()
        self.doRepeat = True
        self.repeatInterval = 2
        self.debug = False
    
    @staticmethod
    def Create():
        return CoordinateReader()

    def readCharacter(self, im, pixels, scale):
        raster = []
        nonZeroLength = 0
        spaceLength = 0
        for x in range(self.x, im.size[0] // scale):
            val = 0
            count = 0
            for y in range(0, im.size[1] // scale):
                if pixels[x * scale, y * scale][:3] == (252, 252, 252):
                    count += 1
                    val += 2**y
            if val != 0 or nonZeroLength > 0:
                raster += [val]

            if count == 0:
                spaceLength += 1
                if nonZeroLength != 0:
                    while raster[0] == 0:
                        raster = raster[1:]
                    superVal = 0
                    for u in range(len(raster)):
                        superVal += raster[u] * 2**u
                    raster = []

                    self.x = x + 1
                    if superVal in DECIPHER:
                        return DECIPHER[superVal]
                    else:
                        return f"[{superVal}]"
                    # print(nonZeroLength)
                nonZeroLength = 0
                if spaceLength > 3:
                    self.x = x
                    return " "
            else:
                nonZeroLength += 1
                spaceLength = 0
        return None

    def setScreen(self, scr):
        self.snippeter.setScreen(scr)

    def getCoordinates(self):
        im, pixels, scale = self.snippeter.snippet()
        if pixels == None:
            return None
        self.x = 0
        outp = ""
        while True:
            ch = self.readCharacter(im, pixels, scale)
            if ch == None:
                break
            outp += ch

        if self.debug:
            logging.info(f"[{outp}]")
        matcher = re.compile(
            r"\s*(?P<x>[+-]?\d+(\.\d+)?)\s+" +
            r"(?P<y>[+-]?\d+(\.\d+)?)\s+" +
            r"(?P<z>[+-]?\d+(\.\d+)?).*")
        match = matcher.fullmatch(outp)
        if match == None:
            return None
        x = float(match.group("x"))
        y = float(match.group("y"))
        z = float(match.group("z"))
        return {"x": x, "y": y, "z": z}

    def repeatPrint(self):
        logging.info("Starting repeat")
        while self.doRepeat:
            coords = self.getCoordinates()
            logging.info(f"Coords are {coords}")
            time.sleep(self.repeatInterval)
        logging.info("Finished repeat")


if __name__ == "__main__":
    # Source: https://realpython.com/intro-to-python-threading/
    logFormat = "%(asctime)s: %(message)s"
    logging.basicConfig(format=logFormat, level=logging.INFO,
                        datefmt="%H:%M:%S")
    coordinateReader = CoordinateReader()
    thr = threading.Thread(target=lambda: coordinateReader.repeatPrint())
    thr.start()
    print("Type 'quit' to stop")
    while True:
        cmd = input("> ")
        if cmd == "quit":
            break
        else:
            print("Unknown command")

    coordinateReader.doRepeat = False
    thr.join()


# im, pixels, scale = snippeter.snippet()
# if pixels != None:
#     outp = ""
#     raster = []
#     nonZeroLength = 0
#     spaceLength = 0
#     for x in range(0, im.size[0] // scale):
#         val = 0
#         count = 0
#         for y in range(0, im.size[1] // scale):
#             if pixels[x * scale, y * scale][:3] == (252, 252, 252):
#                 count += 1
#                 val += 2**y
#         raster += [val]

#         if count == 0:
#             spaceLength += 1
#             if nonZeroLength != 0:
#                 while raster[0] == 0:
#                     raster = raster[1:]
#                 superVal = 0
#                 for x in range(len(raster)):
#                     superVal += raster[x] * 2**x
#                 raster = []
#                 if superVal in decipher:
#                     outp += decipher[superVal]
#                 else:
#                     outp += f"[{superVal}]"
#                 #print(nonZeroLength)
#             nonZeroLength = 0
#         else:
#             nonZeroLength += 1
#             if spaceLength > 3:
#                 outp += " "
#             spaceLength = 0

#     print(outp)
#     matcher = re.compile(r"\s*(?P<x>[^ ]+)\s+(?P<y>[^ ]+)\s+(?P<z>[^ ]+).*")

#     match = matcher.fullmatch(outp)
#     if match != None:
#         x = float(match.group("x"))
#         y = float(match.group("y"))
#         z = float(match.group("z"))
#         print(f"x: {x}, y: {y}, z: {z}")

    # print(raster)
