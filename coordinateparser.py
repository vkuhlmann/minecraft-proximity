from PIL import Image, ImageGrab
import positioner
import fontdecoder
import re

import logging
import threading
import time

# pip install Pillow
# pip install screeninfo
# pip install PyAudio-*.whl
# pip install numpy

WHITE_COLOR = (252, 252, 252)
GRAY_COLOR = (221, 221, 221)


class CoordinateParser:
    def __init__(self, snippeter):
        self.snippeter = snippeter
        self.debug = False

        self.regex = re.compile(
            r"\s*(Z:)?\s*(?P<x>[+-]?\d+(\.\d+)?)(\s|\s*/)\s*" +
            r"(?P<y>[+-]?\d+(\.\d+)?)(\s|\s*/)\s*" +
            r"(?P<z>[+-]?\d+(\.\d+)?).*")

    def readCharacter(self, im, pixels, scale):
        raster = []
        nonZeroLength = 0
        spaceLength = 0
        for x in range(self.x, im.size[0] // scale):
            val = 0
            count = 0
            for y in range(0, im.size[1] // scale):
                col = pixels[x * scale, y * scale][:3]
                if col == WHITE_COLOR or col == GRAY_COLOR:
                    count += 1
                    val += 2**y
            if val != 0 or nonZeroLength > 0:
                raster += [val]

            if count == 0:
                spaceLength += 1
                if nonZeroLength != 0:
                    while raster[0] == 0:
                        raster = raster[1:]
                    symb = fontdecoder.decodeSymbol(raster)
                    raster = []

                    self.x = x + 1
                    return symb
                    # if superVal in DECIPHER:
                    #     return DECIPHER[superVal]
                    # else:
                    #     return f"[{superVal}]"
                    # print(nonZeroLength)
                nonZeroLength = 0
                if spaceLength > 3:
                    self.x = x
                    return " "
            else:
                nonZeroLength += 1
                spaceLength = 0
        return None

    def getCoordinates(self):
        im, pixels, scale = self.snippeter.snippet()
        return self.parseCoordinates(im, pixels, scale)

    def parseCoordinates(self, im, pixels, scale):
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

        match = self.regex.fullmatch(outp)
        if match != None:
            x = float(match.group("x"))
            y = float(match.group("y"))
            z = float(match.group("z"))
            return {"x": x, "y": y, "z": z}

        return None
