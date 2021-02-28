
import numpy as np
import re
import json


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

        if highRay[2] < self.lowCorner[2] or lowRay[2] > self.highCorner[2]:
            return 1.0

        if lowRay[2] < self.lowCorner[2]:
            lowRay += (self.lowCorner[2] - lowRay[2]) / (highRay[2] - lowRay[2])\
                * (highRay - lowRay)
        if highRay[2] > self.highCorner[2]:
            highRay += (self.highCorner[2] - highRay[2]) / (highRay[2] - lowRay[2])\
                * (highRay - lowRay)

        highRay[1] = lowRay[1]
        dist = np.linalg.norm(highRay - lowRay)
        factor = np.exp(np.log(self.transmissionCoeff) * dist)
        return factor

# Do the setup. The returned object needs to implement a number of methods.
# See the implementation of LogicServer for which they are.
def create_server(send_message_handler):
    return LogicServer(send_message_handler)

class Player:
    def __init__(self, di, server):
        self.pos = np.array([di["pos"]["x"], di["pos"]["y"], di["pos"]["z"]]) \
            if di["pos"] is not None else None
        self.userId = di["userId"]
        self.discordUsername = di["discordUsername"]
        self.discordDiscriminator = di["discordDiscriminator"]
        self.displayName = di["displayName"]
        self.server = server

    def set_position(self, x, y, z):
        self.pos = np.array([x, y, z])


class LogicServer:
    def __init__(self, send_message_handler):
        self.send_message = send_message_handler
        self.obscurations = [Obscuration(
            np.array([92, 56, -59]),
            np.array([93, 58, -53]),
            transmissionCoeff=0.1)]

        self.prevBase = None
        self.allow_updatemap_remote = True
        self.players = []

        self.positions = {}

    # Return one of:
    #   False: the command was not handled (unknown command)
    #   True (or None): the command was handled
    #   a dictionary: a response
    #   a list of dictionaries: messages to respond
    #
    def on_message(self, msgType, msg, sender):
        resolve = {
            "updatemap": self.on_updatemap
        }
        if msgType in resolve:
            return resolve[msgType](msg, sender)
        return False

    def on_updatemap(self, msg, sender):
        if not self.allow_updatemap_remote and not sender["isLocal"]:
            return {
                "type": "error",
                "data": {
                    "message": "No permission to use updatemap"
                }
            }

        self.clear_obscurations()
        self.add_density_map(msg["data"])
        self.broadcast({
            "type": "updatemap",
            **msg
        })

        print(f"[Server > Python] Updated map")

    def broadcast(self, msg):
        for pl in self.players:
            self.send_message(pl.userId, msg)

    def clear_obscurations(self):
        self.obscurations = []

    def add_density_map(self, data):
        for v in range(len(data["pixelart"])):
            for u in range(len(data["pixelart"][v])):
                x = u + data["x"]
                y = v + data["z"]
                coeff = data["toCoefficient"][str(data["pixelart"][v][u])]
                if coeff >= 0.95:
                    continue

                self.obscurations.append(Obscuration(
                    np.array([x, 0, y]),
                    np.array([x + 1, 255, y + 1]),
                    transmissionCoeff=coeff
                ))
        # for obsc in self.obscurations:
        #     print(f"{obsc.lowCorner}, {obsc.highCorner}, {obsc.transmissionCoeff}")


    # Return: something which has a set_position method
    def on_join(self, di):
        pl = Player(di, self)
        self.players += [pl]
        print(f"[Server > Python] Joined: {pl.displayName}")
        return pl

    def on_leave(self, pl):
        # Remove the player from the players list.
        for i in range(len(self.players)):
            if self.players[i] == pl:
                del self.players[i]
                print(f"Left: {pl.displayName}")
                return
        print("[Server > Python] Warning: leaving player was not registered")

    def shutdown(self):
        print("[Server > Python] Shut down.")

    # Return one of:
    #   False: the command was not handled (unknown command)
    #   True (or None): the command was handled
    #   a str: output to print
    #
    def handle_command(self, cmdName, args):
        commands = {
            "hi": self.handle_hi
        }

        if cmdName in commands:
            reply = []
            def outp(line):
                nonlocal reply
                reply += [line]

            commands[cmdName](args, outp)
            if len(reply) > 0:
                ans = "\n".join(reply)
                return ans
            else:
                return True
        return False

    def handle_hi(self, args, outp):
        outp("Hi there!")
        outp("This is how you respond.")
        if len(args) > 0:
            outp(f"You supplied an argument: {args}")
        else:
            outp(f"You didn't supply an argument.")

    def get_volume(self, base, oth):
        basePos = base.pos
        othPos = oth.pos

        if base.discordUsername not in self.positions:
            self.positions[base.discordUsername] = np.array([0, 0, 0])

        if basePos is not None:
            self.positions[base.discordUsername] = basePos

        if basePos is None or othPos is None:
            return 1.0
        dist = np.linalg.norm(basePos - othPos)
        halvingDistance = 10

        factor = 1.0
        for obsc in self.obscurations:
            factor *= obsc.getFactor(othPos, basePos)

        return max(1.0 - (dist / halvingDistance)**2 / 2, 0.0) * factor
