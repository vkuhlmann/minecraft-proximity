
import asyncio
import json
import logging
import websockets
import threading
import numpy as np
import queue

logging.basicConfig()

scheduledMessages = queue.Queue()
isQuitRequested = False

def sendCoords(name, x, z):
    scheduledMessages.put(
        json.dumps({
            "type": "playercoords",
            "data": [
                {
                    "name": name,
                    "x": float(x),
                    "z": float(z)
                }
            ]
        })
    )
    print(f"ScheduledMessage has now size {scheduledMessages.qsize()}")

    # if USERS:
    #     message = json.dumps({
    #         "type": "playercoords",
    #         "data": [
    #             {
    #                 "name": name,
    #                 "x": x,
    #                 "z": z
    #             }
    #         ]
    #     })

    #     await asyncio.wait([user.send(message) for user in USERS])

class DensityMap:
    def __init__(self):
        self.densities = []
        self.x = 0
        self.z = 0
        self.onUpdate = None

    def setDensities(self, obj):
        self.densities = obj
        if self.onUpdate != None:
            self.onUpdate()

    def setX(self, x):
        self.x = int(x)
        if self.onUpdate != None:
            self.onUpdate()
    
    def setZ(self, z):
        self.z = int(z)
        if self.onUpdate != None:
            self.onUpdate()

    def setPlayerPosition(self, name, x, z):
        #loop.call_soon_threadsafe(asyncio.async, sendCoords(name, x - self.x, z - self.z))
        #asyncio.run_coroutine_threadsafe(lambda:sendCoords(name, x - self.x, z - self.z), loop)
        loop.call_soon_threadsafe(lambda:sendCoords(name, x - self.x, z - self.z))

    # def getFactor(self, rayFrom, rayTo):
    #     rayFrom = np.copy(rayFrom)
    #     rayTo = np.copy(rayTo)

    #     lowRay = rayFrom
    #     highRay = rayTo
    #     if rayFrom[0] > rayTo[0]:
    #         lowRay = rayTo
    #         highRay = rayFrom

    #     self.lowCorner = np.array([self.x, self.y])

    #     width = 0
    #     if len(densities) > 0:
    #         width = len(densities[0])
    #     self.highCorner = np.array([self.x + width, self.y + len(densities)])

    #     if highRay[0] < self.lowCorner[0] or lowRay[0] > self.highCorner[0]:
    #         return 1.0

    #     if lowRay[0] < self.lowCorner[0]:
    #         lowRay += (self.lowCorner[0] - lowRay[0]) / (highRay[0] - lowRay[0])\
    #             * (highRay - lowRay)
    #     if highRay[0] > self.highCorner[0]:
    #         highRay += (self.highCorner[0] - highRay[0]) / (highRay[0] - lowRay[0])\
    #             * (highRay - lowRay)

    #     if lowRay[2] > highRay[2]:
    #         swap = lowRay
    #         lowRay = highRay
    #         highRay = swap

    #     if highRay[2] < self.lowCorner[2] or highRay[2] > self.highCorner[2]:
    #         return 1.0

    #     if lowRay[2] < self.lowCorner[2]:
    #         lowRay += (self.lowCorner[2] - lowRay[2]) / (highRay[2] - lowRay[2])\
    #             * (highRay - lowRay)
    #     if highRay[2] > self.highCorner[2]:
    #         highRay += (self.highCorner[2] - highRay[2]) / (highRay[2] - lowRay[2])\
    #             * (highRay - lowRay)

    #     highRay[1] = lowRay[1]



    #     dist = np.linalg.norm(highRay - lowRay)
    #     return np.exp(np.log(self.transmissionCoeff) * dist)


densityMap = DensityMap()


# https://websockets.readthedocs.io/en/stable/intro.html
# WS server example


USERS = set()
STATE = {"value": 0}

def state_event():
    return json.dumps({"type": "state", **STATE})

def users_event():
    return json.dumps({"type": "users", "count": len(USERS)})

async def notify_users():
    if USERS:  # asyncio.wait doesn't accept an empty list
        message = users_event()
        await asyncio.wait([user.send(message) for user in USERS])

async def notify_state():
    if USERS:  # asyncio.wait doesn't accept an empty list
        message = state_event()
        await asyncio.wait([user.send(message) for user in USERS])

async def register(websocket):
    USERS.add(websocket)
    await notify_users()


async def unregister(websocket):
    USERS.remove(websocket)
    await notify_users()

async def counter(websocket, path):
    # register(websocket) sends user_event() to websocket
    await register(websocket)
    try:
        await websocket.send(state_event())
        async for message in websocket:
            data = json.loads(message)
            if data["action"] == "minus":
                #STATE["value"] -= 1
                await notify_state()
            elif data["action"] == "plus":
                #STATE["value"] += 1
                await notify_state()
            elif data["action"] == "updatemap":
                await DoUpdateMap(data["data"])
                
            else:
                logging.error(f"Unsupported event: {data['action']}")
    finally:
        await unregister(websocket)


async def doTimeUpdates():
    try:
        while not isQuitRequested:
            await asyncio.sleep(0.2)
            if scheduledMessages.qsize() > 0:
                it = scheduledMessages.get()
                print(f"Sending message to {len(USERS)} users:\n{it}")
                if USERS:
                    message = it
                    await asyncio.wait([user.send(message) for user in USERS])


    except Exception as e:
        print(f"Exception doing updates: {e}")
        return

loop = asyncio.new_event_loop()


async def DoUpdateMap(obj):
    densityMap.setDensities(obj)

def DoDensityMapServer():
    asyncio.set_event_loop(loop)
    start_server = websockets.serve(counter, "localhost", 6789)

    print("Starting server!")
    asyncio.get_event_loop().run_until_complete(start_server)
    print("B")
    asyncio.get_event_loop().run_until_complete(asyncio.wait([doTimeUpdates()]))
    print("C")
    asyncio.get_event_loop().run_forever()
    print("Server is done")

thr = threading.Thread(target=DoDensityMapServer)
thr.start()
