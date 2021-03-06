
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

scheduled_messages = queue.Queue()
is_quit_requested = False

http_thr = None
thr = None
onupdated_callback = None
current_state = None
httpd = None
is_serving_forever = False
httpd_directory = None
loop = None
send_message = None


def send_coords(name, x, z):
    scheduled_messages.put(
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

# https://websockets.readthedocs.io/en/stable/intro.html
# WS server example


USERS = set()


async def register(websocket):
    USERS.add(websocket)


async def unregister(websocket):
    USERS.remove(websocket)


async def socket_listen(websocket, path):
    await register(websocket)
    try:
        send_message({
            "type": "webui",
            "data": {
                "type": "sendmap",
                "data": {}
            }
        })

        async for message in websocket:
            await handle_message(message)

    finally:
        await unregister(websocket)


async def handle_message(message):
    data = json.loads(message)
    if data["type"] == "updatemap":
        await update_map(data["data"])

    elif data["type"] == "setparams":
        if send_message != None:
            send_message(data)

    else:
        logging.error(f"[WebUI] Unsupported event: {data['type']}")


async def do_websocket_sendloop():
    try:
        while not is_quit_requested:
            await asyncio.sleep(0.2)
            if scheduled_messages.qsize() > 0:
                it = scheduled_messages.get()
                if USERS:
                    message = it
                    await asyncio.wait([user.send(message) for user in USERS])

    except Exception as e:
        logging.error(f"Exception doing updates: {e}")
        return


async def update_map(obj):
    global current_state

    current_state = obj
    if onupdated_callback != None:
        onupdated_callback(json.dumps(obj))


class RequestHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, request, client_address, server):
        super().__init__(request, client_address, server, directory=httpd_directory)

    def log_request(self, code='-', size='-'):
        return

    def log_error(self, *args):
        # Often just a distraction.
        return


def run_httpd():
    global httpd, is_serving_forever, httpd_directory

    server_address = ('', 9200)
    print(f"[WebUI] Serving directory is {httpd_directory}")

    httpd = HTTPServer(server_address, RequestHandler)
    print("[WebUI] Open in your browser: http://localhost:9200/")

    is_serving_forever = True
    httpd.serve_forever()
    is_serving_forever = False
    httpd.server_close()


def run_websockets():
    asyncio.set_event_loop(loop)
    start_server = websockets.serve(socket_listen, "localhost", 6789)

    res = asyncio.get_event_loop().run_until_complete(start_server)
    asyncio.get_event_loop().run_until_complete(
        asyncio.wait([do_websocket_sendloop()]))

    if res != None:
        res.close()
        asyncio.get_event_loop().run_until_complete(res.wait_closed())


def start_webui(basepath, onupdated_callback_p, send_message_callback):
    global thr, onupdated_callback, httpd, httpd_directory, http_thr, loop, send_message
    if thr != None:
        logging.warning("[WebUI] Thr was already non-null!")
        return

    loop = asyncio.new_event_loop()

    httpd_directory = os.path.join(basepath, "webui")

    send_message = send_message_callback
    onupdated_callback = onupdated_callback_p

    thr = threading.Thread(target=run_websockets)
    thr.start()

    http_thr = threading.Thread(target=run_httpd)
    http_thr.start()
    print("[WebUI] WebUI has started!")


def stop_webui():
    global is_serving_forever, is_quit_requested, http_thr, httpd, thr, loop, onupdated_callback

    is_quit_requested = True
    if is_serving_forever:
        is_serving_forever = False
        httpd.shutdown()

    if http_thr != None:
        http_thr.join()
    http_thr = None

    if thr != None:
        thr.join()
    thr = None

    onupdated_callback = None

    loop.call_soon_threadsafe(loop.stop)

    print("[WebUI] WebUI has shut down")


def on_message(data):
    scheduled_messages.put(
        json.dumps(data)
    )


def put_data(data):
    global current_state

    data = json.loads(data)
    scheduled_messages.put(
        json.dumps({
            "type": "imageput",
            "data": data
        })
    )
    current_state = data


def handle_command(cmd_name, args):
    if cmd_name == "xz":
        handle_XZ_command(args)
        return True
    return False


def set_players(data):
    arr = json.loads(data)
    msg = json.dumps(
        {
            "type": "updateplayers",
            "data": arr
        })

    def send_update():
        global scheduled_messages
        nonlocal msg
        scheduled_messages.put(msg)

    loop.call_soon_threadsafe(send_update)


def handle_XZ_command(args):
    m = re.fullmatch(r"((?P<x>(\+|-|)\d+) (?P<z>(\+|-|)\d+))?", args)
    if m is None:
        print("Invalid syntax. Syntax is")
        print("xz [<x> <z>]")
        return
    if m.group("x") is None:
        if current_state == None:
            print(f"currentState is None")
        else:
            print(f"topleft is {current_state['x']}, {current_state['z']}")
        return
    if current_state == None:
        print(f"currentState is None")
        return

    x = int(m.group("x"))
    z = int(m.group("z"))

    prev_x = current_state["x"]
    prev_z = current_state["z"]

    current_state["x"] = x
    current_state["z"] = z
    current_state["sender"] = 0

    # put_data(json.dumps(currentState))

    if onupdated_callback != None:
        onupdated_callback(json.dumps(current_state))

    print(
        f"topleft is now {current_state['x']}, {current_state['z']} (was {prev_x}, {prev_z})")
