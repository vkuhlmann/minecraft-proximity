"use strict";

let playerPos = {};
// let mapX = 0;
// let mapZ = 0;
let lastPlayerData = null;
let lastPlayerDataTimestamp = null;

let mapParams = null;
let clientId = 0;

let playerlist;

function setupMap() {
    clientId = Math.floor(Math.random() * 1e9);

    let el = $("#mapXZ")[0];
    mapParams = { mapX: 0, mapZ: 0 };
    mapParams.bindings = {};
    mapParams.setMapX = setMapX;
    mapParams.setMapZ = setMapZ;

    bindElements(el, [mapParams]);

    playerlist = new Playerlist($("#playerlist")[0]);
    playerlist.toColor = { "1": "rgb(181,186,253)", "2": "rgb(63,72,204)" };
    playerlist.toName = { "1": "Player 1", "2": "Player 2" };
    playerlist.update();

    setInterval(checkUpToDate, 1000);

    $("#globalProximityEnable")[0].addEventListener("click", enableGlobalProximity);
    $("#globalProximityDisable")[0].addEventListener("click", disableGlobalProximity);

    $("#server-tab")[0].addEventListener("click", e => {
        setTab("server");
    });

    $("#home-tab")[0].addEventListener("click", e => {
        setTab("home");
    });
    //updateBinding(obj, "label");
}

function enableGlobalProximity() {
    socket.send(JSON.stringify({
        "type": "setparams",
        "data": {
            "proximityEnabled": true
        }
    }));
}

function disableGlobalProximity() {
    socket.send(JSON.stringify({
        "type": "setparams",
        "data": {
            "proximityEnabled": false
        }
    }));
}

function checkUpToDate() {
    if (lastPlayerDataTimestamp == null
        || lastPlayerDataTimestamp < Date.now() - 1500) {

        //console.log(`Out of date (${lastPlayerDataTimestamp} < ${Date.now()} - 1500))`);

        playerlist.setOutOfDate();
    } else {
        //console.log(`Up to date (${lastPlayerDataTimestamp} >= ${Date.now()} - 1500)`);
    }
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
        case "updateplayers":
            {
                updatePlayerlist(json.data);
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

        case "paramsupdated":
            {
                onParamsUpdated(json);
            }

        default:
            console.log(`Unknown message type ${json.type ?? "null"}`);
    }
}

function onParamsUpdated(msg) {
    if (msg.data.proximityEnabled === true) {
        $("#globalProximityDisable")[0].classList.add("btn-outline-danger");
        $("#globalProximityDisable")[0].classList.remove("btn-danger");

        $("#globalProximityEnable")[0].classList.add("btn-success");
        $("#globalProximityEnable")[0].classList.remove("btn-outline-success");

    } else if (msg.data.proximityEnabled === false) {
        $("#globalProximityEnable")[0].classList.add("btn-outline-success");
        $("#globalProximityEnable")[0].classList.remove("btn-success");

        $("#globalProximityDisable")[0].classList.add("btn-danger");
        $("#globalProximityDisable")[0].classList.remove("btn-outline-danger");
    }
}

let playerIndicator = document.createElement("template");
playerIndicator.innerHTML = `
<g>
<circle cx="0" cy="0" r="2" fill="red" />
<text x="0" y="10">AA</text>
</g>
`;


function updatePlayerlist(data) {
    lastPlayerData = data;
    lastPlayerDataTimestamp = Date.now();

    playerlist.clear();
    playerlist.toColor = {};
    playerlist.toName = {};

    let colors = ["rgb(181,186,253)", "rgb(253,180,189)", "rgb(186,253,180)", "rgb(180,232,253)", "rgb(253,251,180)"];

    let i = 0;

    for (let pl of data) {
        let id = i;
        i += 1;

        let obj = { value: `${id}`, "id": id };
        obj.color = colors[id % colors.length];
        obj.name = pl.name;

        playerlist.toColor[id] = obj.color;
        playerlist.toName[id] = obj.name;

        obj.status = pl.status;

        playerlist.add(pl);
    }
}

function updatePlayerCoords(data) {
    lastPlayerData = data;
    lastPlayerDataTimestamp = Date.now();

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

function sendNewData() {
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
        socket.send(JSON.stringify({ "type": "updatemap", "data": data }));

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
            await socket.send(JSON.stringify({ "type": "command", "command": comm }));
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
