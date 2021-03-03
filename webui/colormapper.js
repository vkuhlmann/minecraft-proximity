"use strict";

let colorMapEntry = document.createElement("template");
colorMapEntry.innerHTML = `
<div class="colormapper-entry">
    <div class="mapping-color-holder">
        <input type="color" data-binding="color" id="mappingColor" name="mappingColor" value="#ff0000">
    </div>
    <div class="mapping-details">
        <input type="text" class="form-control" data-binding="coefficient" style="flex:1 1 2ch;" />
    </div>
</div>
`;

function setBinding(el, bindName, bindValue) {
    for (let element of $(`[data-binding=\"${bindName}\"]`, el)) {
        element.innerText = bindValue;
    }
}

class ColorMapperEntry {
    constructor(el, parent, desc) {
        this.el = el;
        this.parent = parent;
        this.color = { value: desc.color || "rgb(255, 0, 0)" };
        this.coefficient = desc["coefficient"] ?? 1.0;
        this.paletteColorID = desc["id"];
        this.bindings = {};

        //setBinding(el, "label", `${label}`);
        bindElements(el, [this]);
    }

    static Create(parent, desc) {
        let el = colorMapEntry.content.children[0].cloneNode(true);
        return new ColorMapperEntry(el, parent, desc);
    }

    setColor(color) {
        if (this.color.suppressSet)
            return;
        this.color.suppressSet = true;
        this.color.value = color;
        //$("[data-binding=centerpoint]", obj.el)[0].style.fill = obj.color;
        updateBinding(this, "color");
        this.parent.toColor[this.paletteColorID] = this.color.value;
        updateSVGDisplay();
        colorselector.update();

        delete this.color.suppressSet;
    };

    setCoefficient(coefficient) {
        coefficient = parseFloat(coefficient)
        if (isNaN(coefficient))
            return;

        this.coefficient = coefficient;
        //$("[data-binding=centerpoint]", obj.el)[0].style.fill = obj.color;
        updateBinding(this, "coefficient");
        this.parent.toCoefficient[this.paletteColorID] = this.coefficient;
        onChanged();
    }
}

class Mapper {
    constructor(el, onUpdate) {
        this.el = el;
        this.el.classList.add("colormapper");
        this.onUpdate = onUpdate;
        this.toColor = {};
        this.toCoefficient = {};
        this.orderedIds = [];
        this.nextId = 0;

        this.items = [];

        this.clear();
        this.add({ value: "AA" });
        this.add({ color: "green", value: "BB" });
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
        this.toCoefficient[newId] = desc.coefficient ?? 1.0;
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
            this.add({ color: this.toColor[id], value: `${id}`, "id": id, "coefficient": this.toCoefficient[id] });
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
        let entry = ColorMapperEntry.Create(this, desc);
        this.items.push(entry);
        this.el.appendChild(entry.el);
    }
}
