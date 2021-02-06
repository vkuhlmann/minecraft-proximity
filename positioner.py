from PIL import Image, ImageGrab
from screeninfo import get_monitors
import time

#import pyscreenshot as ImageGrab

XYZ_COLOR = (252, 168, 0)

def FindPosition(bbox=None):
    im = ImageGrab.grab(bbox=bbox, all_screens=True)
    pixels = im.load()
    ans = None

    sc = 4
    for mod in range(0, sc):
        ans = FindZ(pixels, im, sc, mod)
        if ans != None:
            break

    if ans == None:
        return None
    scale = ans[0]
    xLead = ans[1]
    y = ans[2]
    xTrail = ans[3]

    return (scale, xLead, y, min(xTrail + 180 * scale, im.size[0]) - xLead, 8 * scale)

def FindHorizLines(pixels, im, sc, mod):
    horizLines = []

    for y in range(mod, im.size[1], sc):
        for x in range(mod, im.size[0], sc):
            if pixels[x, y][:3] == XYZ_COLOR:
                box = expandToPlainColor(x, y, pixels, im)
                ratio = box[2] / box[3]
                if ratio >= 4 and ratio <= 6:
                    box = (*box, ratio)
                    if not box in horizLines:
                        horizLines += [box]
    return horizLines

def FindZ(pixels, im, sc, mod):
    horizLines = FindHorizLines(pixels, im, sc, mod)
    for hl in horizLines:
        x = hl[0]
        for oth in horizLines:
            if oth == hl:
                continue
            if oth[0] != x:
                continue

            frac = (oth[1] - hl[1]) / hl[3]
            if frac >= 5 and frac <= 7:
                scale = (oth[1] - hl[1]) / 6
                # if scale >= 3 and oth[3] >= 3:
                #     return (scale, x + hl[2] + 4 * scale + 1, hl[1] + 1)
                # else:
                #     return (scale, x + hl[2] + 4 * scale, hl[1])
                if scale >= 3 and oth[3] >= 3:
                    return (scale, x + 1, hl[1] + 1, x + hl[2] + 4 * scale + 1)
                else:
                    return (scale, x + 1, hl[1], x + hl[2] + 4 * scale)
    return None

def expandToLeft(x, y, height, color, pixels, minX=0):
    for u in range(x - 1, minX - 1, -1):
        for v in range(y, y + height):
            if pixels[u, v][:3] != color:
                return u + 1
    return minX

def expandToRight(x, y, height, color, pixels, maxXExcl):
    for u in range(x + 1, maxXExcl):
        for v in range(y, y + height):
            if pixels[u, v][:3] != color:
                return u
    return maxXExcl

def expandToTop(x, y, width, color, pixels, minY=0):
    for v in range(y - 1, minY - 1, -1):
        for u in range(x, x + width):
            if pixels[u, v][:3] != color:
                return v + 1
    return minY

def expandToBottom(x, y, width, color, pixels, maxYExcl):
    for v in range(y + 1, maxYExcl):
        for u in range(x, x + width):
            if pixels[u, v][:3] != color:
                return v
    return maxYExcl

def expandToPlainColor(x, y, pixels, im):
    width = 1
    height = 1
    color = pixels[x, y][:3]
    
    farLeft = expandToLeft(x, y, height, color, pixels)
    farRight = expandToRight(x, y, height, color, pixels, im.size[0])
    x = farLeft
    width = farRight - farLeft

    farTop = expandToTop(x, y, width, color, pixels)
    farBottom = expandToBottom(x, y, width, color, pixels, im.size[1])
    y = farTop
    height = farBottom - farTop
    return (x, y, width, height)

# for box in horizLines:
#     for y in range(box[1], box[1] + box[3]):
#         for x in range(box[0], box[0] + box[2]):
#             pixels[x, y] = (255, 0, 0)

# pos = FindPosition()
# print(pos)

# monitors = get_monitors()
# screenRect = (*pos[1:],)
# minX = min((mon.x for mon in monitors))
# minY = min((mon.y for mon in monitors))

# screenRect = (screenRect[0] + minX, screenRect[1] + minY, screenRect[2], screenRect[3])
# bbox = (screenRect[0], screenRect[1], 
#     screenRect[0] + screenRect[2], screenRect[1] + screenRect[3])

# im = ImageGrab.grab(bbox=bbox, all_screens=True)
# im.show()
#im.save("test10.png")

class Snippeter:
    def __init__(self):
        self.pos = None
        self.bbox = None
        self.calibrateTimeout = 10
        self.nextAllowedCalibrate = None
        self.screen = -1
        pass

    def calibrate(self):
        monitors = get_monitors()
        if monitors == None:
            raise Exception("get_monitors return None")

        print(f"Calibrating! Monitors is {monitors}")

        self.pos = None
        self.bbox = None

        bbox = None
        if self.screen >= 0 and self.screen < len(monitors):
            mon = monitors[self.screen]
            bbox = (mon.x, mon.y, mon.x + mon.width, mon.y + mon.height)

        if self.screen != -2:
            try:
                self.pos = FindPosition(bbox=bbox)
            except Exception as e:
                print(f"Error executing FindPosition: {e}")
        # else:
        #     self.pos = None

        if self.pos == None:
            self.bbox = None
            self.nextAllowedCalibrate = time.time() + self.calibrateTimeout
            return

        if bbox == None:
            minX = min((mon.x for mon in monitors))
            minY = min((mon.y for mon in monitors))
            bbox = (minX, minY)
        
        screenRect = (*self.pos[1:],)
        
        screenRect = (screenRect[0] + bbox[0], screenRect[1] + bbox[1], 
            screenRect[2], screenRect[3])

        bbox = (screenRect[0], screenRect[1], 
            screenRect[0] + screenRect[2], screenRect[1] + screenRect[3])
        self.bbox = bbox

    def snippet(self, allowCalibrate=True):
        if self.bbox == None or self.pos == None:
            if allowCalibrate and (self.nextAllowedCalibrate == None or 
                time.time() >= self.nextAllowedCalibrate):
                self.calibrate()
            if self.bbox == None:
                return None, None, None
        im = ImageGrab.grab(bbox=self.bbox, all_screens=True)
        pixels = im.load()
        scale = int(self.pos[0])
        if pixels[0, 0][:3] != XYZ_COLOR or pixels[0, 6 * scale][:3] != XYZ_COLOR:
            self.pos = None
            self.bbox = None
            return self.snippet(allowCalibrate=allowCalibrate)

        return im, pixels, scale

    def setScreen(self, scr):
        if scr == self.screen:
            return
        self.screen = scr
        self.calibrate()
        



