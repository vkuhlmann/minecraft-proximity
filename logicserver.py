
import numpy as np
import densitymap

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

def generateObscurations(l, dens):
    l.clear()
    for v in range(len(dens.densities)):
        for u in range(len(dens.densities[v])):
            x = u + dens.x
            y = v + dens.z
            coeff = dens.densities[v][u]
            if coeff >= 0.95:
                continue

            l.append(Obscuration(
                np.array([x, 0, y]),
                np.array([x + 1, 255, y + 1]),
                transmissionCoeff=coeff
            ))
    #print(f"Obscurations are now\n{l}")

class LogicServer:
    def __init__(self):
        self.obscurations = [Obscuration(
            np.array([92, 56, -59]),
            np.array([93, 58, -53]),
            transmissionCoeff=0.1)]

        densitymap.densityMap.onUpdate = lambda: generateObscurations(self.obscurations, densitymap.densityMap)
        self.prevBase = None

        self.positions = {}
        
    def Shutdown(self):
        print("Shutting down Python LogicServer")
        densitymap.isQuitRequested = True
        densitymap.loop.call_soon_threadsafe(densitymap.loop.stop)
        densitymap.thr.join()
        print("Shut down Python LogicServer")


    def HandleCommand(self, cmdName, args):
        print(f"Received command {cmdName} with args {args}")
        if cmdName == "updatemap":
            print("Updating map...")
            for username, pos in self.positions.items():
                densitymap.densityMap.setPlayerPosition(username, pos[0], pos[2])
                print(f"Submitted player {username} (x={pos[0]}, z={pos[2]})")

            print("Updated map")
            return True
        return False

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
        #self.callId += 1

        # if base["username"] not in self.lastUpdatedTime or self.lastUpdatedTime[base["username"]] + 40 < self.callId:
        #     self.lastUpdatedTime[base["username"]] = self.callId
        #     #self.prevBase = base
        #     try:
        #         if basePos != None:
        #             densitymap.densityMap.setPlayerPosition(base["username"], basePos[0], basePos[2])
        #         else:
        #             densitymap.densityMap.setPlayerPosition(base["username"], 0, 0)
        #     except Exception as ex:
        #         print(f"Error setting player position: {ex}")

        self.positions[base["username"]] = basePos or np.array([0, 0, 0])

        if basePos is None or othPos is None:
            return 1.0
        dist = np.linalg.norm(basePos - othPos)
        #return max(1.0 - dist / 10.0, 0.0)
        halvingDistance = 10

        factor = 1.0
        for obsc in self.obscurations:
            factor *= obsc.getFactor(othPos, basePos)

        return max(1.0 - (dist / halvingDistance)**2 / 2, 0.0) * factor
