
import numpy as np
import densitymap
import re

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

def create_server():
    return LogicServer()

class Player:
    def __init__(self, di, server):
        self.pos = np.array([di["pos"]["x"], di["pos"]["y"], di["pos"]["z"]]) \
                if di["pos"] is not None else None
        self.userId = di["userId"]
        self.username = di["username"]
        self.server = server

    def set_position(self, x, y, z):
        self.pos = np.array([x, y, z])

        #     return {
        #     "pos": np.array([dict["pos"]["x"], dict["pos"]["y"], dict["pos"]["z"]]) \
        #         if dict["pos"] is not None else None,
        #     "userId": dict["userId"],
        #     "username": dict["username"]
        # }

class LogicServer:
    def __init__(self):
        self.obscurations = [Obscuration(
            np.array([92, 56, -59]),
            np.array([93, 58, -53]),
            transmissionCoeff=0.1)]

        densitymap.densityMap.onUpdate = lambda: generateObscurations(self.obscurations, densitymap.densityMap)
        self.prevBase = None

        self.positions = {}

    def create_player(self, di):
        #return LogicServer.PlayerFromDict(di)
        return Player(di, self)
        
    def shutdown(self):
        print("Shutting down Python LogicServer")
        densitymap.isQuitRequested = True
        #densitymap.loop.call_soon_threadsafe(densitymap.loop.stop)
        densitymap.thr.join()
        densitymap.loop.call_soon_threadsafe(densitymap.loop.stop)
        print("Shut down Python LogicServer")


    def handle_command(self, cmdName, args):
        print(f"Received command {cmdName} with args {args}")
        if cmdName == "updatemap":
            print("Updating map...")
            for username, pos in self.positions.items():
                densitymap.densityMap.setPlayerPosition(username, pos[0], pos[2])
                print(f"Submitted player {username} (x={pos[0]}, z={pos[2]})")

            print("Updated map")
            return True
        elif cmdName == "topleft":
            self.HandleTopLeftCommand(args)
            return True
        return False

    def HandleTopLeftCommand(self, args):
        m = re.fullmatch(r"((?P<x>(\+|-|)\d+) (?P<z>(\+|-|)\d+))?", args)
        if m is None:
            print("Invalid syntax. Syntax is")
            print("topleft [<x> <z>]")
            return
        if m.group("x") is None:
            print(f"topleft is {densitymap.densityMap.x}, {densitymap.densityMap.z}")
            return
        x = int(m.group("x"))
        z = int(m.group("z"))
        prevX = densitymap.densityMap.x
        prevZ = densitymap.densityMap.z

        densitymap.densityMap.x = x
        densitymap.densityMap.z = z

        print(f"topleft is now {densitymap.densityMap.x}, {densitymap.densityMap.z} (was {prevX}, {prevZ})")

    @staticmethod
    def Create():
        return LogicServer()

    # @staticmethod
    # def PlayerFromDict(dict):
    #     return {
    #         "pos": np.array([dict["pos"]["x"], dict["pos"]["y"], dict["pos"]["z"]]) \
    #             if dict["pos"] is not None else None,
    #         "userId": dict["userId"],
    #         "username": dict["username"]
    #     }

    def get_volume(self, base, oth):
        basePos = base.pos
        othPos = oth.pos
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

        if base.username not in self.positions:
            self.positions[base.username] = np.array([0, 0, 0])

        if basePos is not None:
            self.positions[base.username] = basePos

        if basePos is None or othPos is None:
            return 1.0
        dist = np.linalg.norm(basePos - othPos)
        #return max(1.0 - dist / 10.0, 0.0)
        halvingDistance = 10

        factor = 1.0
        for obsc in self.obscurations:
            factor *= obsc.getFactor(othPos, basePos)

        return max(1.0 - (dist / halvingDistance)**2 / 2, 0.0) * factor
