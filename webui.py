
import asyncio
import json
import logging
import websockets
import threading
import numpy as np
import queue
import re
import http.server
from http.server import HTTPServer
import time
import os
import encodings.idna

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
    #print(f"ScheduledMessage has now size {scheduledMessages.qsize()}")

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
        pass
        # self.densities = obj
        # if self.onUpdate != None:
        #     self.onUpdate()

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

async def socket_listen(websocket, path):
    #print("Receiving socket")
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
                #print(f"Sending message to {len(USERS)} users:\n{it}")
                if USERS:
                    message = it
                    await asyncio.wait([user.send(message) for user in USERS])


    except Exception as e:
        print(f"Exception doing updates: {e}")
        return

async def DoUpdateMap(obj):
    global currentState

    obj["x"] = densityMap.x
    obj["z"] = densityMap.z
    densityMap.setDensities(obj)

    currentState = obj

    if onupdated_callback != None:
        onupdated_callback(json.dumps(obj))

def DoDensityMapServer():
    asyncio.set_event_loop(loop)
    start_server = websockets.serve(socket_listen, "localhost", 6789)

    #print("Starting server!")
    res = asyncio.get_event_loop().run_until_complete(start_server)
    asyncio.get_event_loop().run_until_complete(asyncio.wait([doTimeUpdates()]))

    #print(f"res is {res}")
    if res != None:
        res.close()
        asyncio.get_event_loop().run_until_complete(res.wait_closed())
    #print("Server is done")

httpThr = None
thr = None
onupdated_callback = None
currentState = None
httpd = None
isServingForever = False
httpdDirectory = None
loop = None

class RequestHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, request, client_address, server):
        super().__init__(request, client_address, server, directory=httpdDirectory)

def do_httpd():
    global httpd, isServingForever, httpdDirectory

    server_address = ('', 9200)
    print(f"Serving directory is {httpdDirectory}")

    httpd = HTTPServer(server_address, RequestHandler)
    print("Open in your browser: http://localhost:9200/")

    isServingForever = True
    httpd.serve_forever()
    isServingForever = False

    httpd.server_close()

def start_webui(basepath, onupdated_callback_p):
    global thr, onupdated_callback, httpd, httpdDirectory, httpThr, loop
    if thr != None:
        print("Thr was already non-null!")
        return

    loop = asyncio.new_event_loop()

    httpdDirectory = os.path.join(basepath, "webui")

    onupdated_callback = onupdated_callback_p

    thr = threading.Thread(target=DoDensityMapServer)
    thr.start()

    httpThr = threading.Thread(target=do_httpd)
    httpThr.start()
    print("WebUI has started!")

def stop_webui():
    global isServingForever, isQuitRequested, httpThr, httpd, thr, loop, onupdated_callback

    isQuitRequested = True
    if isServingForever:
        isServingForever = False
        httpd.shutdown()

    if httpThr != None:
        httpThr.join()
    httpThr = None

    if thr != None:
        thr.join()
    thr = None

    onupdated_callback = None

    loop.call_soon_threadsafe(loop.stop)

    print("WebUI has shut down")

def put_data(data):
    global currentState

    data = json.loads(data)
    densityMap.x = data["x"]
    densityMap.z = data["z"]

    scheduledMessages.put(
        json.dumps({
            "type": "imageput",
            "data": data
        })
    )
    currentState = data

def handle_command(cmdName, args):
    #print(f"Received command {cmdName} with args '{args}'")
    # if cmdName == "updatemap":
    #     print("Updating map...")
    #     for username, pos in self.positions.items():
    #         densitymap.densityMap.setPlayerPosition(username, pos[0], pos[2])
    #         print(f"Submitted player {username} (x={pos[0]}, z={pos[2]})")

    #     print("Updated map")
    #     return True
    if cmdName == "xz":
        HandleXZCommand(args)
        return True
    return False

def set_players(data):
    arr = json.loads(data)
    for pl in arr:
        densityMap.setPlayerPosition(pl["name"], pl["x"], pl["z"])


def HandleXZCommand(args):
    m = re.fullmatch(r"((?P<x>(\+|-|)\d+) (?P<z>(\+|-|)\d+))?", args)
    if m is None:
        print("Invalid syntax. Syntax is")
        print("xz [<x> <z>]")
        return
    if m.group("x") is None:
        print(f"topleft is {densityMap.x}, {densityMap.z}")
        return
    x = int(m.group("x"))
    z = int(m.group("z"))
    prevX = densityMap.x
    prevZ = densityMap.z

    densityMap.x = x
    densityMap.z = z

    if currentState != None:
        currentState["x"] = x
        currentState["z"] = z

        put_data(json.dumps(currentState))

        if onupdated_callback != None:
            onupdated_callback(json.dumps(currentState))

    print(f"topleft is now {densityMap.x}, {densityMap.z} (was {prevX}, {prevZ})")



