
import numpy as np

class Obscuration:
    def __init__(self, lowCorner, highCorner, transmissionCoeff):
        self.transmissionCoeff = transmissionCoeff
        self.lowCorner = lowCorner
        self.highCorner = highCorner

    def getFactor(self, rayFrom, rayTo):
        rayFrom = np.copy(rayFrom)
        rayTo = np.copy(rayTo)

        lowRay = rayFrom
        highRay = rayTo
        if rayFrom[0] > rayTo[0]:
            lowRay = rayTo
            highRay = rayFrom

        if highRay[0] < self.lowCorner[0] or lowRay[0] > self.highCorner[0]:
            return 1.0

        if lowRay[0] < self.lowCorner[0]:
            lowRay += (self.lowCorner[0] - lowRay[0]) / (highRay[0] - lowRay[0])\
                * (highRay - lowRay)
        if highRay[0] > self.highCorner[0]:
            highRay += (self.highCorner[0] - highRay[0]) / (highRay[0] - lowRay[0])\
                * (highRay - lowRay)

        if lowRay[2] > highRay[2]:
            swap = lowRay
            lowRay = highRay
            highRay = swap

        if highRay[2] < self.lowCorner[2] or highRay[2] > self.highCorner[2]:
            return 1.0

        if lowRay[2] < self.lowCorner[2]:
            lowRay += (self.lowCorner[2] - lowRay[2]) / (highRay[2] - lowRay[2])\
                * (highRay - lowRay)
        if highRay[2] > self.highCorner[2]:
            highRay += (self.highCorner[2] - highRay[2]) / (highRay[2] - lowRay[2])\
                * (highRay - lowRay)

        highRay[1] = lowRay[1]
        dist = np.linalg.norm(highRay - lowRay)
        return np.exp(np.log(self.transmissionCoeff) * dist)

class LogicServer:
    def __init__(self):
        self.obsc = Obscuration(
            np.array([92, 56, -59]),
            np.array([93, 58, -53]),
            transmissionCoeff=0.2)

    @staticmethod
    def Create():
        return LogicServer()

    @staticmethod
    def PlayerFromDict(dict):
        return {
            "pos": np.array([dict["pos"]["x"], dict["pos"]["y"], dict["pos"]["z"]]) \
                if dict["pos"] is not None else None,
            "userId": dict["userId"],
            "username": dict["username"]
        }

    def GetVolume(self, base, oth):
        basePos = base["pos"]
        othPos = oth["pos"]

        if basePos is None or othPos is None:
            return 1.0
        dist = np.linalg.norm(basePos - othPos)
        #return max(1.0 - dist / 10.0, 0.0)
        halvingDistance = 10

        factor = self.obsc.getFactor(sender.position, self.position)

        return max(1.0 - (dist / halvingDistance)**2 / 2, 0.0) * factor
