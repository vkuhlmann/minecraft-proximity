
import numpy as np

class LogicServer:
    def __init__(self):
        pass

    @staticmethod
    def Create():
        return LogicServer()

    @staticmethod
    def PlayerFromDict(dict):
        return {
            "pos": np.array(dict["pos"]["x"], dict["pos"]["y"], dict["pos"]["z"]) \
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

        factor = 1.0 #self.server.obsc.getFactor(sender.position, self.position)

        return max(1.0 - (dist / halvingDistance)**2 / 2, 0.0) * factor
