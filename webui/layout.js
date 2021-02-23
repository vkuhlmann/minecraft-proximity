"use strict"

$(function () {
    onDOMReady();
});

// const pixelart =
// "..........\n" +
// "..x....x..\n" +
// "..........\n" +
// "..........\n" +
// "..........\n" +
// "..x....x..\n" +
// "..xxxxxx..\n" +
// "..........\n";

let pixelartText =
    "........\n" +
    ".x....x.\n" +
    "........\n" +
    "........\n" +
    "........\n" +
    ".x....x.\n" +
    ".xxxxxx.\n" +
    "........\n";

let mapper = null;
let colorselector = null;

let pixelart = pixelartText.replace(/\n+$/, "").split("\n");

for (let i in pixelart) {
    let list = [];
    for (let c of pixelart[i])
        list.push(c);
    pixelart[i] = list;
}

let colormap = { ".": "rgb(181,186,253)", "x": "rgb(63,72,204)" };

let panButton;
let resizeButton;
let resizeController;
let socket = null;

function onDOMReady() {
    mapper = new Mapper($("#mapper")[0]);
    //mapper.setToColorMapping(colormap);
    mapper.toColor = colormap;
    mapper.toCoefficient = { ".": 1.0, "x": 0.2 };
    mapper.update();

    diagram = new Diagram();
    resizeButton = new ResizeButton();
    resizeController = new ResizeController();

    updateSVGDisplay();
    onChanged();

    $("#copyOutputButton").on("click",
        () => {
            $("#outputArea")[0].select();
            document.execCommand("copy");
        }
    );

    setupOverlay();

    //printCoordOnDiagramClick();
    //paintOnDiagramClick();

    colorselector = new ColorSelector();
    colorselector.update();

    setupSplitter();

    window.setTimeout(function () { diagram.updateViewSize(); }, 500);

    panButton = new PanButton();


    // let isResizable = false;
    // let onPanelsContainerResize = function (e) {
    //     //console.log(`Window innerWidth is ${window.innerWidth}`);
    //     if (window.innerWidth > 1100) {
    //         if (!isResizable) {
    //             console.log("Adding resizability");
    //             $("#configurationPanel").resizable({
    //                 handles: "w"// {e: $("#splitter")[0]}
    //             });
    //             $("#configurationPanel")[0].classList.add("resizable");
    //             isResizable = true;
    //         }
    //     } else {
    //         if (isResizable) {
    //             console.log("Removing resizability");
    //             $("#configurationPanel")[0].classList.remove("resizable");
    //             //$("#pixelartPanel")[0].style.width = "";
    //             $("#configurationPanel").resizable("destroy");
    //             isResizable = false;
    //         }
    //     }
    // }

    // //$("#panelsContainer").on("resize", onPanelsContainerResize);
    // window.addEventListener("resize", onPanelsContainerResize);
    // onPanelsContainerResize();


    // $("#drawingContainerSvg")[0].addEventListener("click", 
    //     (e) => {
    //         let rect = $("#drawingContainerSvg")[0].getBoundingClientRect();
    //         let offsetX = e.clientX - rect.left;
    //         let offsetY = e.clientY - rect.top;
    //         let scale = 80.0;
    //         let x = Math.floor(offsetX / scale);
    //         let y = Math.floor(offsetY / scale);
    //         console.log(`Hit tile (${x}, ${y})`);
    //     }
    // );

    $("#saveImage").on("click", e => {
        saveImage();
    });

    tryOpenSocket();
}

// function resetSplitterWidth() {

// }

function setupSplitter() {
    let el = $("#splitter")[0];
    let resizePanel = $("#configurationPanel")[0];
    let capturedPointer = null;
    let prevPos = null;
    let prevPanelWidth = null;

    el.addEventListener("pointerdown", function (event) {
        el.setPointerCapture(event.pointerId);
        document.body.style.cursor = "col-resize";

        capturedPointer = event.pointerId;
        prevPanelWidth = resizePanel.clientWidth;
        prevPos = event.clientX;
        event.preventDefault();
    });

    el.addEventListener("pointermove", function (event) {
        if (event.pointerId !== capturedPointer)
            return;
        let newPos = event.clientX;
        let newWidth = prevPanelWidth + (prevPos - newPos);
        resizePanel.style.flex = `1 0 ${newWidth}px`;
        prevPos = newPos;
        prevPanelWidth = newWidth;
        event.preventDefault();
    });

    el.addEventListener("pointerup", function (event) {
        if (capturedPointer === event.pointerId) {
            document.body.style.cursor = "auto";
            event.target.releasePointerCapture(capturedPointer);
            capturedPointer = null;
        }
        event.preventDefault();
    });
}

function colorFromUINT(u) {
    let red = u >>> 24;
    let green = (u >>> 16) % 256;
    let blue = (u >>> 8) % 256;
    let alpha = u % 256;
    return `rgb(${red}, ${green}, ${blue})`;
}

function interpretImage(data, width, height) {
    //let uints = new Uint32Array(data[0]);
    let bytes = new Uint8Array(data[0]);
    //console.log(bytes);
    let colorsToIDs = {};
    let colors = [];
    let content = [];
    let currentLine = [];

    for (let i = 0; i < bytes.length; i += 4) {
        let u = (bytes[i] << 24) | (bytes[i + 1] << 16) | (bytes[i + 2] << 8) | bytes[i + 3];

        let id = colorsToIDs[u];
        if (id == null) {
            id = colors.length;
            colorsToIDs[u] = id;
            colors.push(u);
        }
        currentLine.push(id);
        if (currentLine.length == width) {
            content.push(currentLine);
            currentLine = [];
        }
    }

    // console.log(content);
    // console.log(colors);

    pixelart = content;
    mapper.toCoefficient[0] = mapper.toCoefficient["."];
    //colormap[0] = colormap["."];
    colormap = {};
    mapper.toColor = {};
    mapper.toColor[0] = colorFromUINT(colors[0]);

    for (let i = 1; i < colors.length; i++) {
        mapper.toCoefficient[i] = mapper.toCoefficient["x"];
        //colormap[i] = colormap["x"];
        mapper.toColor[i] = colorFromUINT(colors[i]);
    }

    updateSVGDisplay();
    onChanged();

    //mapper.setColors(colors);
    //mapper.setToColorMapping(colormap);
    mapper.update();
    colorselector.update();
}

function increaseWidth(width, fillColorId = null) {
    let newPixelArt = [];
    if (fillColorId == null)
        fillColorId = mapper.orderedIds[0] ?? Object.keys(mapper.toColor)[0];

    for (let row of pixelart) {
        newPixelArt.push([...Array.from(row), ...(new Array(width - diagram.width).fill(fillColorId))]);
    }
    pixelart = newPixelArt;
    updateSVGDisplay();
    onChanged();
}

function decreaseWidth(width) {
    let newPixelArt = [];
    for (let row of pixelart) {
        newPixelArt.push(row.slice(0, Math.max(width, 1)));
    }
    pixelart = newPixelArt;
    updateSVGDisplay();
    onChanged();
}

function increaseHeight(height, fillColorId = null) {
    if (fillColorId == null)
        fillColorId = mapper.orderedIds[0] ?? Object.keys(mapper.toColor)[0];

    for (let currHeight = diagram.height; currHeight < height; currHeight++) {
        pixelart.push(new Array(diagram.width).fill(fillColorId));
    }

    updateSVGDisplay();
    onChanged();
}

function decreaseHeight(height) {
    pixelart.splice(Math.max(height, 1));
    updateSVGDisplay();
    onChanged();
}


function setWidth(width, fillColorId = null) {
    if (width < diagram.width)
        decreaseWidth(width);
    else if (width > diagram.width)
        increaseWidth(width, fillColorId);
    diagram.redraw();
}

function setHeight(height, fillColorId = null) {
    if (height < diagram.height)
        decreaseHeight(height);
    else if (height > diagram.height)
        increaseHeight(height, fillColorId);
}


function importFromFile(file) {
    file.arrayBuffer().then(buffer => {
        //buffer.pipe()
        //let data = new BmpDecoder(buffer);
        let img = UPNG.decode(buffer);
        let width = img.width;
        let height = img.height;
        let rawPixels = UPNG.toRGBA8(img);

        console.log(img);
        interpretImage(rawPixels, width, height);
    });
}

// const parseRGBARegex = new RegExp(
//     "^.*?(?<red>\\d+(\\.\\d+)?)(?<redPercentage>[^\\d,]+.*?%)?.*?" + 
//     "(?<green>\\d+(\\.\\d+)?)(?<greenPercentage>[^\\d,]+.*?%)?.*?" + 
//     "(?<blue>\\d+(\\.\\d+)?)(?<bluePercentage>[^\\d,]+.*?%)?.*?" + 
//     "((?<alpha>\\d+(\\.\\d+)?)(?<alphaPercentage>[^\\d,]+.*?%)?.*)?$");

const parseRGBARegex = new RegExp(
    "^.*?\\(\\s*(?<red>\\d+(\\.\\d+)?)\\s*(?<redPercentage>%)?\\s*,\\s*" +
    "(?<green>\\d+(\\.\\d+)?)\\s*(?<greenPercentage>%)?\\s*,\\s*" +
    "(?<blue>\\d+(\\.\\d+)?)\\s*(?<bluePercentage>%)?\\s*" +
    "(,\\s*(?<alpha>\\d+(\\.\\d+)?)\\s*(?<alphaPercentage>%)?\\s*)?\\)\\s*$");


function tryOpenSocket() {
    //debugger;
    socket = new WebSocket("ws://127.0.0.1:6789");
    socket.onopen = e => {
        console.log("[open] Connection established");
    };

    socket.onmessage = e => {
        handleMessage(JSON.parse(e.data));
    };

    socket.onerror = (e) => {
        console.log(`[error] Socket error`);
    }

    socket.onclose = (e) => {
        console.log(`[close] Socket closed with code ${e.code}, reason: ${e.reason}`);
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

let playerPos = {}

function updatePlayerCoords(data) {
    //debugger;
    for (let pl of data) {
        let el = null;
        if (playerPos[pl.name] == null) {
            el = createTemplateInstance("template-pointmarker", diagram.pannableContent.el);
            let obj = {el: el, name: pl.name, x: pl.x, z: pl.z};
            obj.label = obj.name;
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
        let x = obj.x * diagram.tileSize;
        let y = obj.z * diagram.tileSize;

        el.setAttribute("transform", `translate(${x} ${y})scale(1)`);
    }
    //console.log(JSON.stringify(data));
}

async function sendNewData() {
    let data = [];
    let map = mapper.toCoefficient;
    let lines = pixelart;
    for (let line of lines) {
        let dat = [];
        for (let ch of line) {
            dat.push(map[ch]);
        }
        data.push(dat);
    }
    if (socket != null)
        await socket.send(JSON.stringify({ "action": "updatemap", "data": data }));
    else
        console.log("Socket is not open! Can't send new data.");
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

function parseRGBAValues(string) {
    let match = string.match(parseRGBARegex);
    let arr = [+match.groups["red"], +match.groups["green"], +match.groups["blue"],
    +(match.groups["alpha"] ?? 1)];

    if (match.groups["redPercentage"] != null)
        arr[0] = Math.round(arr[0] * 255.0 / 100.0);
    if (match.groups["greenPercentage"] != null)
        arr[1] = Math.round(arr[1] * 255.0 / 100.0);
    if (match.groups["bluePercentage"] != null)
        arr[2] = Math.round(arr[2] * 255.0 / 100.0);
    if (match.groups["alphaPercentage"] != null)
        arr[3] = Math.round(arr[3] * 255.0 / 100.0);
    else
        arr[3] = Math.round(arr[3] * 255.0);
    return arr;
}

function saveToBuffer() {
    let arrayBuffer = new ArrayBuffer(diagram.width * diagram.height * 4);
    //let array = new Uint8Array(diagram.width * diagram.height * 4);
    let array = new Uint8Array(arrayBuffer);
    let colorsToBytes = {};
    for (let id of mapper.orderedIds) {
        let color = mapper.toColor[id];
        // let match = color.match(regex);
        // colorsToBytes[id] = [+match.groups["red"], +match.groups["green"], +match.groups["blue"],
        // +(match.groups["alpha"] ?? 1) * 100];
        colorsToBytes[id] = parseRGBAValues(color);
    }
    //console.log(JSON.stringify(colorsToBytes));
    for (let y = 0; y < diagram.height; y++) {
        for (let x = 0; x < diagram.width; x++) {
            array.set(colorsToBytes[pixelart[y][x]], (y * diagram.width + x) * 4);
        }
    }
    let buffer = UPNG.encode([arrayBuffer], diagram.width, diagram.height, 0);
    //let buffer = result.buffer.slice(result.byteOffset, result.byteOffset + result.byteLength);
    return buffer;
}

function saveImage(fileName = "pixelart.png") {
    let buffer = saveToBuffer();

    // Source: https://stackoverflow.com/questions/19327749/javascript-blob-filename-without-link
    // Modified
    let a = document.createElement("a");
    a.style.display = "none";
    document.body.appendChild(a);

    let blob = new Blob([buffer], { type: "image/png" });
    let url = window.URL.createObjectURL(blob);
    a.href = url;
    a.download = fileName;
    a.click();
    window.URL.revokeObjectURL(url);
}

function setupOverlay() {
    $("#fileImportForm").on("submit", (e) => {
        try {
            importFromFile($("#selectedImportFile")[0].files[0]);
        } catch (error) {
            alert(`Error on importing: ${error}`);
        }

        $("#importImageOverlay").hide();
        e.preventDefault();
    });

    $("#importImageOverlay").on("click", (e) => {
        $("#importImageOverlay").hide();
    });

    $("#importImage").on("click", (e) => {
        //console.log("Clicked");
        $("#importImageOverlay").show();
        $("#importImageOverlay")[0].style.display = "flex";
    });

    $(".overlay-container").hide();

    $(".overlay-card").on("click", (e) => {
        e.stopPropagation();
        //e.preventDefault();
    });
}

function onChanged() {
    sendNewData();
}

function updateSVGDisplay() {
    diagram.redraw();
}
