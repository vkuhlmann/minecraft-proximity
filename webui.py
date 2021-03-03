
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
            "type": "updateplayers",
            "data": [
                {
                    "name": name,
                    "x": float(x),
                    "z": float(z)
                }
            ]
        })
    )

densityMapX = 0
densityMapZ = 0

# https://websockets.readthedocs.io/en/stable/intro.html
# WS server example

USERS = set()
STATE = {"value": 0}

def state_event():
    return json.dumps({"type": "state", **STATE})

async def register(websocket):
    USERS.add(websocket)

async def unregister(websocket):
    USERS.remove(websocket)

async def socket_listen(websocket, path):
    #print("Receiving socket")
    # register(websocket) sends user_event() to websocket
    await register(websocket)
    try:
        send_message({
            "type": "webui",
            "data": {
                "type": "sendmap",
                "data": {}
            }
        })
        
        #await websocket.send(state_event())
        async for message in websocket:
            data = json.loads(message)
            if data["action"] == "updatemap":
                #print("Updatemap")
                await DoUpdateMap(data["data"])
                
            else:
                logging.error(f"[WebUI] Unsupported event: {data['action']}")
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

    # obj["x"] = densityMap.x
    # obj["z"] = densityMap.z
    
    # obj["x"] = densityMapX
    # obj["z"] = densityMapZ
    #densityMap.setDensities(obj)

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
send_message = None

class RequestHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, request, client_address, server):
        super().__init__(request, client_address, server, directory=httpdDirectory)

    def log_request(self, code='-', size='-'):
        return

def do_httpd():
    global httpd, isServingForever, httpdDirectory

    server_address = ('', 9200)
    print(f"[WebUI] Serving directory is {httpdDirectory}")

    httpd = HTTPServer(server_address, RequestHandler)
    print("[WebUI] Open in your browser: http://localhost:9200/")

    isServingForever = True
    httpd.serve_forever()
    isServingForever = False

    httpd.server_close()

def start_webui(basepath, onupdated_callback_p, send_message_callback):
    global thr, onupdated_callback, httpd, httpdDirectory, httpThr, loop, send_message
    if thr != None:
        print("[WebUI] Thr was already non-null!")
        return

    loop = asyncio.new_event_loop()

    httpdDirectory = os.path.join(basepath, "webui")

    send_message = send_message_callback
    onupdated_callback = onupdated_callback_p

    thr = threading.Thread(target=DoDensityMapServer)
    thr.start()

    httpThr = threading.Thread(target=do_httpd)
    httpThr.start()
    print("[WebUI] WebUI has started!")

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

    print("[WebUI] WebUI has shut down")

def put_data(data):
    global currentState

    data = json.loads(data)
    # densityMapX = data["x"]
    # densityMapZ = data["z"]

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
    msg = json.dumps(
        {
            "type": "updateplayers",
            "data": arr
        })

    def sendUpdate():
        global scheduledMessages
        nonlocal msg
        scheduledMessages.put(msg)

    loop.call_soon_threadsafe(sendUpdate)

    # for pl in arr:
    #     #densityMap.setPlayerPosition(pl["name"], pl["x"], pl["z"])
    #     loop.call_soon_threadsafe(lambda: sendCoords(pl["name"], pl["x"], pl["z"]))

def HandleXZCommand(args):
    #global densityMapX,densityMapZ

    m = re.fullmatch(r"((?P<x>(\+|-|)\d+) (?P<z>(\+|-|)\d+))?", args)
    if m is None:
        print("Invalid syntax. Syntax is")
        print("xz [<x> <z>]")
        return
    if m.group("x") is None:
        #print(f"topleft is {densityMapX}, {densityMapZ}")
        if currentState == None:
            print(f"currentState is None")
        else:
            print(f"topleft is {currentState['x']}, {currentState['z']}")
        return
    if currentState == None:
        print(f"currentState is None")
        return

    x = int(m.group("x"))
    z = int(m.group("z"))

    prevX = currentState["x"]
    prevZ = currentState["z"]

    currentState["x"] = x
    currentState["z"] = z
    currentState["sender"] = 0

    # put_data(json.dumps(currentState))

    if onupdated_callback != None:
        onupdated_callback(json.dumps(currentState))

    print(f"topleft is now {densityMapX}, {densityMapZ} (was {prevX}, {prevZ})")



