from PIL import Image, ImageGrab
import positioner
import coordinateparser
import re

import logging
import threading
import time

# Note: by default the program now uses a coordinate reader in C#.
# Python is a bit slow.

# pip install Pillow
# pip install screeninfo
# pip install numpy

class CoordinateReader:
    def __init__(self):
        self.snippeter = positioner.Snippeter()
        # self.snippeter.calibrate()

        self.coordinateParser = coordinateparser.CoordinateParser(
            self.snippeter)

        self.doRepeat = True
        self.repeatInterval = 2

    @staticmethod
    def Create():
        return CoordinateReader()

    def setScreen(self, scr):
        print(f"Setting screen to {scr}")
        self.snippeter.setScreen(scr)

    def getCoordinates(self):
        im, pixels, scale = self.snippeter.snippet()
        if pixels == None:
            if not self.snippeter.canCalibrate():
                return None
            for area in self.snippeter.calibrate():
                self.snippeter.setArea(area)
                coords = self.coordinateParser.getCoordinates()
                if coords != None:
                    return coords
                else:
                    self.snippeter.setArea(None)
            return None

        coords = self.coordinateParser.getCoordinates()
        if coords == None:
            self.snippeter.setArea(None)
        return coords

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
    coordinateReader.snippeter.calibrateTimeout = 20
    coordinateReader.coordinateParser.debug = True

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
