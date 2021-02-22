"use strict";

let colorSelectionEntry = document.createElement("template");
colorSelectionEntry.innerHTML = `
<div class="coloroption">
    <button type="button" role="button" class="colordisplay" data-id="colorDisplay"></button>
</div>
`;

class ColorOption {
    constructor(id, color, parent) {
        this.el = colorSelectionEntry.content.children[0].cloneNode(true);
        this.colorDisplay = { el: $("[data-id=\"colorDisplay\"]", this.el)[0] };

        this.id = id;
        this.setColor(color);
        this.parent = parent;
        const that = this;
        this.el.addEventListener("click", e => { that.onClick(e); });
    }

    setColor(color) {
        this.color = color;
        this.colorDisplay.el.style.backgroundColor = this.color;
    }

    setSelected(val) {
        if (val) {
            this.el.classList.add("coloroption-selected");
        } else {
            this.el.classList.remove("coloroption-selected");
        }
    }

    onClick(e) {
        this.parent.selectSingle(this.id);
    }
}

class ColorSelector {
    constructor() {
        this.el = $("#colorSelector")[0];
        this.dynamicOptions = { el: $("[data-id=\"dynamicOptions\"]", this.el)[0] };
        this.addColorButton = { el: $("#addColorButton")[0] };
        this.newColorChooserContainer = { el: $("#newColorChooserContainer")[0] };
        this.newColorChooser = { el: $("#newColorChooser")[0] };
        //this.newColorChooser.colorDisplay = {el: $("[data-id=\"colorDisplay\"]", this.newColorChooser.el)[0]};

        const that = this;
        this.newColorChooser.setColor = color => {
            that.newColorChooser.el.style.backgroundColor = color;
        };

        this.selected = [];

        this.options = [];

        this.add(0, "rgb(255, 0, 0)");
        this.add(1, "rgb(0, 255, 0)");
        //this.el.children[this.el.children.length - 1].classList.add("coloroption-selected");
        this.selectSingle(this.options.length - 1);

        this.initColorChooser();


        this.addColorButton.el.addEventListener("click", function (ev) {
            that.onAddColor();
        });
    }

    initColorChooser() {
        if (this.newColorChooser.pickr != null)
            this.newColorChooser.pickr.destroy();

        // Source: https://www.npmjs.com/package/@simonwep/pickr
        this.newColorChooser.pickr = Pickr.create({
            el: this.newColorChooser.el,
            theme: 'classic', // or 'monolith', or 'nano'
            //container: panel.el,
            comparison: false,
            default: "rgb(181,186,253)",
            useAsButton: true,

            components: {
                // Main components
                //preview: true,
                opacity: false,
                hue: true,

                // Input / output Options
                interaction: {
                    hex: true,
                    rgba: true,
                    hsla: true,
                    hsva: true,
                    cmyk: true,
                    input: true,
                    clear: false,
                    save: false
                }
            }
        });

        this.newColorChooser.pickr.on("change", (color, source, instance) => {
            this.newColorChooser.setColor(color.toRGBA().toString(2));
        });

        this.newColorChooser.pickr.on("hide", instance => {
            let id = mapper.registerNew({ color: this.newColorChooser.pickr.getColor().toRGBA().toString(2) });
            this.selectSingle(id);
            this.newColorChooserContainer.el.classList.add("hide");
        })
    }

    onAddColor() {
        this.newColorChooser.setColor(this.newColorChooser.pickr.getColor().toRGBA().toString(2));
        this.newColorChooserContainer.el.classList.remove("hide");
        this.newColorChooser.pickr.show();
    }

    clear() {
        this.selected = [];
        this.options = [];
        this.dynamicOptions.el.innerHTML = "";
    }

    add(id, color) {
        let newVal = new ColorOption(id, color, this);
        this.options.push(newVal);
        this.dynamicOptions.el.appendChild(newVal.el);
        //let el = colorSelectionEntry.content.children[0].cloneNode(true);
        //this.el.appendChild(el);
    }

    getSingleSelected() {
        return this.selected[0];
    }

    selectSingle(id) {
        paintOnDiagramClick();
        this.selectMulti([id]);
    }

    selectMulti(ids) {
        this.selected = ids;
        for (let opt of this.options) {
            opt.setSelected(ids.includes(opt.id));
        }
    }

    update() {
        let prevSelected = [...this.selected];
        this.clear();
        for (let id of mapper.orderedIds) {
            this.add(id, mapper.toColor[id]);
        }

        let select = [];
        for (let opt of this.options) {
            if (prevSelected.includes(opt.id))
                select.push(opt.id);
        }
        this.selectMulti(select);
    }
}

function paintOnDiagramClick() {
    let capturedPointer = null;
    let paintPixel = function (event, pos) {
        let x = Math.floor((pos.x + diagram.panOffset.x) / diagram.tileSize);
        let y = Math.floor((pos.y + diagram.panOffset.y) / diagram.tileSize);

        let selected = colorselector.getSingleSelected();
        if (selected != null)
            diagram.setPixel(x, y, selected);

        // x = pos.x / diagram.tileSize;
        // y = pos.y / diagram.tileSize;

        // console.log(`Clicked at tile (${x}, ${y})`);
        //console.log(`Clicked at tile (${x}, ${y})`);
        // if (addPointButton.getState() == 1)
        //     setDiagramHandle({});
    };

    function isPointerCaptured(ev) {
        return capturedPointer === true || capturedPointer === ev.pointerId;
    }
    function releasePointerCapture(ev) {
        if (ev == null || capturedPointer === ev.pointerId) {
            if (diagram.el.hasPointerCapture(capturedPointer)) {
                console.log(`Releasing ${capturedPointer}`);
                try {
                    diagram.el.releasePointerCapture(capturedPointer);
                } catch (e) {
                    console.log(`Error releasing capture: ${e.message}`);
                }
            } else {
                console.log(`Vacuously releasing ${capturedPointer}`);
            }
            capturedPointer = null;
        } else if (capturedPointer === true) {
            capturedPointer = null;
        }
    }

    setDiagramHandle({
        click: paintPixel,
        pointerdown: function (event, pos) {
            capturedPointer = true;
            diagram.el.setPointerCapture(event.pointerId);
            paintPixel(event, pos);
            //event.preventDefault();
        },
        pointercancel: function (event, pos) {
            if (!isPointerCaptured(event))
                return;
            capturedPointer = null;
            //releasePointerCapture(event);
            //event.preventDefault();
        },
        pointerup: function (event, pos) {
            if (!isPointerCaptured(event))
                return;
            capturedPointer = null;
            //releasePointerCapture(event);
            //event.preventDefault();
        },
        pointermove: function (event, pos) {
            if (!isPointerCaptured(event))
                return;
            paintPixel(event, pos);
            //event.preventDefault();
        },
        gotpointercapture: function (event, pos) {
            capturedPointer = event.pointerId;
            //console.log(`Acquired ${event.pointerId}`);
        },
        lostpointercapture: function (event, pos) {
            if (capturedPointer === event.pointerId)
                capturedPointer = null;
            //console.log(`Lost ${event.pointerId}`);
        },
        dismiss: function () {
            releasePointerCapture();
            colorselector.selectMulti([]);
            //that.untoggle();
        },
    });
}

