"use strict";

let playerlistEntry = document.createElement("template");
playerlistEntry.innerHTML = `
<div class="playerlist-entry">
    <div class="playerlist-main">
        <div class="mapping-color-holder">
            <input type="color" data-binding="color" id="mappingColor" name="mappingColor" value="#ff0000">
        </div>
        <div class="mapping-details" style="flex-flow:column;">
            <input type="text" class="form-control" data-binding="name" style="flex:1 1 2ch;" />
        </div>
    </div>
    <div data-binding="status">
        Status unknown
    </div>
</div>
`;

class PlayerlistEntry {
    constructor(el, parent, desc) {
        this.el = el;
        this.parent = parent;
        this.color = { value: desc.color || "rgb(255, 0, 0)" };
        this.name = desc["name"] ?? "Unknown";
        this.id = desc["id"];
        this.bindings = {};
        //console.log(desc);
        this.status = desc["status"] ?? "Status unknown";

        //setBinding(el, "label", `${label}`);
        bindElements(el, [this]);
    }

    static Create(parent, desc) {
        let el = playerlistEntry.content.children[0].cloneNode(true);
        return new PlayerlistEntry(el, parent, desc);
    }

    setColor(color) {
        if (this.color.suppressSet)
            return;
        this.color.suppressSet = true;
        this.color.value = color;
        //$("[data-binding=centerpoint]", obj.el)[0].style.fill = obj.color;
        updateBinding(this, "color");
        this.parent.toColor[this.id] = this.color.value;
        //updateSVGDisplay();
        colorselector.update();

        delete this.color.suppressSet;
    };

    setName(name) {
        this.name = name;
        //$("[data-binding=centerpoint]", obj.el)[0].style.fill = obj.color;
        updateBinding(this, "name");
        this.parent.toName[this.id] = this.name;
        //onChanged();
    }

    setStatus(status) {
        this.status = status;
        updateBinding(this, "status");
    }
}

class Playerlist {
    constructor(el, onUpdate) {
        this.el = el;
        this.el.classList.add("colormapper");
        this.onUpdate = onUpdate;
        this.toColor = {};
        this.toName = {};
        this.orderedIds = [];
        this.nextId = 0;

        this.items = [];

        this.clear();
        this.add({ value: "AA" });
        this.add({ color: "green", value: "BB" });
    }

    setOutOfDate() {
        for (let entry of this.items) {
            entry.setStatus("Unknown");
        }
    }

    getNextId() {
        return this.nextId++;
    }

    clear() {
        this.items = [];
        this.el.innerHTML = "";
    }

    registerNew(desc) {
        let newId = this.getNextId();
        this.toColor[newId] = desc.color;
        this.toName[newId] = desc.name ?? "Unknown";
        this.orderedIds.push(newId);
        this.update();
        colorselector.update();
        return newId;
    }

    update() {
        for (let id in this.toColor) {
            if (parseInt(id) == id)
                id = parseInt(id);
            if (!this.orderedIds.includes(id))
                this.orderedIds.push(id);
        }

        //this.toColors = colormap;
        this.clear();
        let i = 0;
        for (let id of this.orderedIds) {
            this.add({ color: this.toColor[id], value: `${id}`, "id": id, "name": this.toName[id] });
            i += 1;
        }
    }

    setColors(colors) {
        this.colors = colors;
        this.clear();
        let i = 0;
        for (let u of this.colors) {
            this.add({ color: u, value: `${i}` });
            i += 1;
        }
    }

    add(desc) {
        if (!isNaN(desc.id))
            this.nextId = Math.max(this.nextId, desc.id + 1);
        let entry = PlayerlistEntry.Create(this, desc);
        this.items.push(entry);
        this.el.appendChild(entry.el);
    }
}

