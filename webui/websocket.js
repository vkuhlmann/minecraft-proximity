"use strict";

let playerPos = {};
// let mapX = 0;
// let mapZ = 0;
let lastPlayerData = null;

let mapParams = null;
let clientId = 0;

function setupMap() {
    clientId = Math.floor(Math.random() * 1e9);

    let el = $("#mapXZ")[0];
    mapParams = { mapX: 0, mapZ: 0 };
    mapParams.bindings = {};
    mapParams.setMapX = setMapX;
    mapParams.setMapZ = setMapZ;

    bindElements(el, [mapParams]);
    //updateBinding(obj, "label");
}

function setMapX(val) {
    let coord = parseFloat(val)
    if (isNaN(coord))
        return;

    mapParams.mapX = coord;
    updateBinding(mapParams, "mapX");
    onChanged();
}

function setMapZ(val) {
    let coord = parseFloat(val)
    if (isNaN(coord))
        return;

    mapParams.mapZ = coord;
    updateBinding(mapParams, "mapZ");
    onChanged();
}

function tryOpenSocket() {
    //debugger;
    $("#statusBrand")[0].innerHTML = `<span style="color:gray;">Trying to connect...</span>`;
    socket = new WebSocket("ws://127.0.0.1:6789");
    socket.onopen = e => {
        console.log("[open] Connection established");
        $("#statusBrand")[0].innerHTML = `<span style="color:green;">Connected</span>`;
    };

    socket.onmessage = e => {
        handleMessage(JSON.parse(e.data));
    };

    socket.onerror = (e) => {
        console.log(`[error] Socket error`);
        $("#statusBrand")[0].innerHTML = `<span style="color:red;" onclick="tryOpenSocket">Disconnected</span>`;
    }

    socket.onclose = (e) => {
        console.log(`[close] Socket closed with code ${e.code}, reason: ${e.reason}`);
        $("#statusBrand")[0].innerHTML = `<span style="color:red;" onclick="tryOpenSocket">Disconnected</span>`;
        socket = null;
    }
}

function handleMessage(json) {
    switch (json.type) {
        case "playercoords":
            {
                updatePlayerCoords(json.data);
            }
            break;

        case "imageput":
            {
                if (json.data.sender === clientId)
                    return;
                imagePut(json.data);
            }
            break;

        default:
            console.log(`Unknown message type ${json.type ?? "null"}`);
    }
}

let playerIndicator = document.createElement("template");
playerIndicator.innerHTML = `
<g>
<circle cx="0" cy="0" r="2" fill="red" />
<text x="0" y="10">AA</text>
</g>
`;

function updatePlayerCoords(data) {
    lastPlayerData = data;

    for (let pl of data) {
        let el = null;
        if (playerPos[pl.name] == null) {
            el = createTemplateInstance("template-pointmarker", diagram.pannableContent.el);
            let obj = { el: el, name: pl.name, x: pl.x, z: pl.z };
            obj.label = obj.name;
            obj.bindings = {};
            bindElements(el, [obj]);
            updateBinding(obj, "label");

            activateTemplateInstance(el);

            //playerPos[pl] = {el: playerIndicator.content.children[0].cloneNode(true)};
            playerPos[pl.name] = obj;
            //diagram.pannableContent.el.appendChild(playerPos[pl].el);
            console.log("Appended new playerPos element");
        }
        let obj = playerPos[pl.name];
        obj.x = pl.x;
        obj.z = pl.z;

        el = obj.el;
        let x = (obj.x - mapParams.mapX) * diagram.tileSize;
        let y = (obj.z - mapParams.mapZ) * diagram.tileSize;

        el.setAttribute("transform", `translate(${x} ${y})scale(1)`);
    }
    //console.log(JSON.stringify(data));
}

async function sendNewData() {
    // let data = [];
    // let map = mapper.toCoefficient;
    // let lines = pixelart;
    // for (let line of lines) {
    //     let dat = [];
    //     for (let ch of line) {
    //         dat.push(map[ch]);
    //     }
    //     data.push(dat);
    // }

    if (socket != null) {
        let data = imageGet();
        data.sender = clientId;
        await socket.send(JSON.stringify({ "action": "updatemap", "data": data }));

    } else {
        console.log("Socket is not open! Can't send new data.");
    }
}

async function runCommand(comm) {
    commandHistory[commandHistory.length - 1] = comm;
    commandHistory.push("");
    commandHistoryIndex = commandHistory.length - 1;
    addConsoleLine(`> ${comm}`);
    //console.log(`Command: ${comm}`);

    if (comm.length > 0) {
        try {
            await socket.send(JSON.stringify({ "action": "command", "command": comm }));
        } catch (error) {
            console.log(`Error trying to send the command: ${error}`);
        }
    }
}

function imagePut(data) {
    pixelart = data.pixelart;
    mapper.toCoefficient = data.toCoefficient;
    mapper.toColor = data.toColor;
    let mapX = data["x"];
    let mapZ = data["z"];

    // inputMapX.value = mapX;
    // inputMapZ.value = mapZ;

    mapParams["mapX"] = mapX;
    mapParams["mapZ"] = mapZ;

    updateBinding(mapParams, "mapX");
    updateBinding(mapParams, "mapZ");

    updateSVGDisplay();
    mapper.update();
    colorselector.update();
}

function imageGet() {
    let data = {
        "pixelart": pixelart,
        toCoefficient: mapper.toCoefficient,
        toColor: mapper.toColor,
        "x": mapParams.mapX,
        "z": mapParams.mapZ
    };
    return JSON.parse(JSON.stringify(data));
}
