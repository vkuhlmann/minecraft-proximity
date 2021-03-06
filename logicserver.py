
import numpy as np
import re
import json


class Obscuration:
    def __init__(self, low_corner, high_corner, transmission_coeff):
        self.transmission_coeff = transmission_coeff
        self.low_corner = low_corner
        self.high_corner = high_corner

    def get_factor(self, ray_from, ray_to):
        ray_from = np.copy(ray_from)
        ray_to = np.copy(ray_to)

        low_ray = ray_from
        high_ray = ray_to
        if ray_from[0] > ray_to[0]:
            low_ray = ray_to
            high_ray = ray_from

        if high_ray[0] < self.low_corner[0] or low_ray[0] > self.high_corner[0]:
            return 1.0

        if low_ray[0] < self.low_corner[0]:
            low_ray += (self.low_corner[0] - low_ray[0]) / (high_ray[0] - low_ray[0])\
                * (high_ray - low_ray)
        if high_ray[0] > self.high_corner[0]:
            high_ray += (self.high_corner[0] - high_ray[0]) / (high_ray[0] - low_ray[0])\
                * (high_ray - low_ray)

        if low_ray[2] > high_ray[2]:
            swap = low_ray
            low_ray = high_ray
            high_ray = swap

        if high_ray[2] < self.low_corner[2] or low_ray[2] > self.high_corner[2]:
            return 1.0

        if low_ray[2] < self.low_corner[2]:
            low_ray += (self.low_corner[2] - low_ray[2]) / (high_ray[2] - low_ray[2])\
                * (high_ray - low_ray)
        if high_ray[2] > self.high_corner[2]:
            high_ray += (self.high_corner[2] - high_ray[2]) / (high_ray[2] - low_ray[2])\
                * (high_ray - low_ray)

        high_ray[1] = low_ray[1]
        dist = np.linalg.norm(high_ray - low_ray)
        factor = np.exp(np.log(self.transmission_coeff) * dist)
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
        self.coords_rate = 0.0

    def set_position(self, x, y, z):
        self.pos = np.array([x, y, z])

    def on_position_unknown(self):
        self.pos = None

    def set_coords_rate(self, rate):
        self.coords_rate = rate


class LogicServer:
    def __init__(self, send_message_handler):
        self.send_message = send_message_handler
        self.obscurations = [Obscuration(
            np.array([92, 56, -59]),
            np.array([93, 58, -53]),
            transmission_coeff=0.1)]

        self.allow_updatemap_remote = True
        self.players = []
        self.map = None
        self.params = {
            "proximityEnabled": True,
            "halvingDistance": 10
        }

    # Return one of:
    #   False: the command was not handled (unknown command)
    #   True (or None): the command was handled
    #   a dictionary: a response
    #   a list of dictionaries: messages to respond
    #
    def on_message(self, msgType, msg, sender):
        resolve = {
            "updatemap": self.on_updatemap,
            "webui": self.on_webui,
            "sendmap": self.on_sendmap,
            "setparams": self.on_setparams
        }
        if msgType in resolve:
            return resolve[msgType](msg, sender)
        return False

    def on_webui(self, msg, sender):
        res = self.on_message(msg["data"]["type"], msg["data"], sender)
        if isinstance(res, dict):
            res = [res]
        if isinstance(res, list):
            return [{"type": "webui", "data": r} for r in res]
        return res

    def on_setparams(self, msg, sender):
        for key in msg["data"]:
            if key not in self.params:
                print(f"[Server > Python] Unknown key {key}")
                continue
            self.params[key] = msg["data"][key]

        self.broadcast({
            "type": "paramsupdated",
            "data": self.params
        })

    def on_sendmap(self, msg, sender):
        if self.map != None:
            return({
                "type": "updatemap",
                "data": self.map
            })

    def on_updatemap(self, msg, sender):
        if not self.allow_updatemap_remote and not sender["isLocal"]:
            return {
                "type": "error",
                "data": {
                    "message": "No permission to use updatemap"
                }
            }

        self.map = msg["data"]

        self.clear_obscurations()
        self.add_density_map(msg["data"])
        self.broadcast({
            "type": "updatemap",
            **msg
        })

        print(f"[Server > Python] Updated map")

    def on_coords_rates_updated(self):
        pass

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
                    transmission_coeff=coeff
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
    def handle_command(self, cmd_name, args):
        commands = {
            "hi": self.handle_hi
        }

        if cmd_name in commands:
            reply = []

            def outp(line):
                nonlocal reply
                reply += [line]

            commands[cmd_name](args, outp)
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
        if not self.params["proximityEnabled"]:
            return 1.0

        base_pos = base.pos
        oth_pos = oth.pos

        if base_pos is None or oth_pos is None:
            return 1.0
        dist = np.linalg.norm(base_pos - oth_pos)
        halving_distance = self.params["halvingDistance"]

        factor = 1.0
        for obsc in self.obscurations:
            factor *= obsc.get_factor(oth_pos, base_pos)

        return max(1.0 - (dist / halving_distance)**2 / 2, 0.0) * factor
