"use strict";


function setVisualPointers(pointers) {
    if (pointers.length >= 1) {
        $("#pointerVisual0")[0].style.visibility = "visible";
        $("#pointerVisual0")[0].setAttribute("cx", pointers[0].pos.x);
        $("#pointerVisual0")[0].setAttribute("cy", pointers[0].pos.y);

    } else {
        $("#pointerVisual0")[0].style.visibility = "hidden";
    }

    if (pointers.length >= 2) {
        $("#pointerVisual1")[0].style.visibility = "visible";
        $("#pointerVisual1")[0].setAttribute("cx", pointers[1].pos.x);
        $("#pointerVisual1")[0].setAttribute("cy", pointers[1].pos.y);

    } else {
        $("#pointerVisual1")[0].style.visibility = "hidden";
    }
}

class PanZoomController {
    constructor(targetEl, update) {
        this.targetEl = targetEl;
        this.isUsingTouch = false;

        this.gestureBaseGrip = null;
        this.gestureBaseDist = null;
        this.gestureBaseZoom = 1.0;
        this.intentionPanOffset = { x: 0, y: 0 };

        this.pointers = [];
        this.pointersHist = "";

        this.update = update;
        this.ondismiss = null;
        this.clientToCoordSpace = null;
        this.coordToClientSpace = null;

        this.zoom = 1.0;
        this.panOffset = { x: 0, y: 0 };

        this.updateFrame = 0;
    }

    getZoom() {
        return this.zoom;
    }

    setZoom(zoom, origin = null) {
        let oldZoom = this.zoom;
        this.zoom = zoom;
        let origPanX = this.panOffset.x;
        let origPanY = this.panOffset.y;

        if (origin == null)
            origin = { x: 0, y: 0 };

        if (this.update != null)
            this.update();

        // this.panOffset.x = (origPanX + origin.x) * this.zoom / oldZoom - origin.x;
        // this.panOffset.y = (origPanY + origin.y) * this.zoom / oldZoom - origin.y;

        this.panOffset.x = origin.x * this.zoom / oldZoom - (origin.x - origPanX);
        this.panOffset.y = origin.y * this.zoom / oldZoom - (origin.y - origPanY);

        if (this.update != null)
            this.update();
    }

    getPanOffset() {
        return { x: this.panOffset.x, y: this.panOffset.y };
    }

    setPanOffset(x, y) {
        this.panOffset.x = x;
        this.panOffset.y = y;

        if (this.update != null)
            this.update();
    }

    resetGrip() {
        this.onresetgrip();

        this.resetGripPanOffset();

        this.gestureBaseDist = this.calcGripDist();
        this.gestureBaseZoom = this.zoom;

        // this.pointersHist += " Zoom grip reset";
        // $("#outputArea")[0].value = this.pointersHist;
    }

    resetGripPanOffset() {
        let grip = this.calcGripPoint();
        this.baseGrip = grip;
        // if (grip != null)
        //     transfMatrix = new DOMMatrix().translateSelf(-grip.x, -grip.y).preMultiplySelf(new DOMMatrix().scaleSelf(-1, -1));
        // else
        //     transfMatrix = null;
        //this.panOffset = { x: panContext.panOffset.x, y: panContext.panOffset.y };
    }

    setPointer(event, pos) {
        if (this.isUsingTouch)
            return;
        for (let i in this.pointers) {
            if (this.pointers[i]?.ev?.pointerId === event.pointerId) {
                if (pos != null) {
                    this.pointers[i] = { ev: event, pos: pos };
                } else {
                    this.pointers.splice(i, 1);
                    if (i == "0")
                        this.resetGrip();
                    //this.pointersHist += "-";
                    // $("#outputArea")[0].value = this.pointersHist;
                }
                //setVisualPointers(this.pointers);
                return;
            }
        }
        if (pos == null)
            return;
        this.pointers.push({ ev: event, pos: pos });
        //this.pointersHist += "+";
        //$("#outputArea")[0].value = this.pointersHist;
        //setVisualPointers(this.pointers);
        //resetGrip();
    }

    hasPointer(event) {
        for (let i in this.pointers) {
            if (this.pointers[i]?.ev?.pointerId === event.pointerId)
                return true;
        }
        return false;
    }

    calcGripPoint() {
        let totX = 0.0;
        let totY = 0.0;
        for (let p of this.pointers) {
            totX += p.pos.x;
            totY += p.pos.y;
        }
        return new DOMPoint(totX / this.pointers.length + this.panOffset.x, totY / this.pointers.length + this.panOffset.y);
    }

    calcGripDist() {
        if (this.pointers.length < 2)
            return null;
        //console.log(JSON.stringify(pointers, null, 4));
        let deltaX = this.pointers[1].pos.x - this.pointers[0].pos.x;
        let deltaY = this.pointers[1].pos.y - this.pointers[0].pos.y;
        let dist = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
        return dist;
    }

    pointerdown(event, pos) {
        this.setPointer(event, pos);
        this.resetGrip();

        this.targetEl.setPointerCapture(event.pointerId);

        event.preventDefault();
        event.stopPropagation();
    }

    pointerend(event) {
        this.setPointer(event, null);

        event.preventDefault();
        event.stopPropagation();
    }

    pointercancel(event) {
        this.pointerend(event);
    }

    pointerup(event) {
        this.pointerend(event);
    }

    pointerout(event) {
        if (event.target !== this.targetEl)
            return;
        this.pointerend(event);
    }

    pointerleave(event) {
        if (event.target !== this.targetEl)
            return;
        //console.log(`Pointer ${event.pointerId} leave. Target: ${event.target}`);
        this.pointerend(event);
    }

    pointermove(event, pos) {
        if (this.pointers.length > 0)
            event = event;
        if (!this.hasPointer(event))
            return;
        this.setPointer(event, pos);
        this.updatePanZoom();
    }

    updatePanZoom() {
        let grip = this.calcGripPoint();
        if (grip == null)
            return;

        if (this.baseGrip != null) {
            let point = DOMPoint.fromPoint(grip);
            let newPanOffset =
                point
                    .matrixTransform(new DOMMatrix().translateSelf(-this.panOffset.x, -this.panOffset.y))
                    .matrixTransform(new DOMMatrix().translateSelf(-this.baseGrip.x, -this.baseGrip.y))
                    .matrixTransform(new DOMMatrix().scaleSelf(-1, -1))
            //.matrixTransform(new DOMMatrix().translateSelf(this.baseGrip.x, this.baseGrip.y));

            this.setPanOffset(newPanOffset.x, newPanOffset.y);
        }

        let dist;
        if (this.gestureBaseDist != null && (dist = this.calcGripDist()) != null) {
            // this.pointersHist += " A";
            // $("#outputArea")[0].value = this.pointersHist;

            let newZoom = (dist / this.gestureBaseDist) * this.gestureBaseZoom;
            this.updateFrame += 1;
            // if ((this.updateFrame % 100) == 1) {
            //     this.pointersHist += ` ${(newZoom * 100).toFixed(0)}%`;
            //     $("#outputArea")[0].value = this.pointersHist;
            // }

            // if ((i % 100) == 1) {
            //     pointersHist += ` (${pointers[0].pos.x.toFixed(0)}, ${pointers[0].pos.y.toFixed(0)})&`
            //      + `(${pointers[1].pos.x.toFixed(0)}, ${pointers[1].pos.y.toFixed(0)})`;
            //     $("#outputArea")[0].value = pointersHist;
            // }
            this.setZoom(newZoom, grip);
            //panContext.setZoom(newZoom);
            //resetGrip();
            this.resetGripPanOffset();
        }
        //resetGrip();
    }

    gotpointercapture(event, pos) {
        //capturedPointer = event.pointerId;
        //console.log(`Acquired ${event.pointerId}`);
    }

    lostpointercapture(event, pos) {
        // if (capturedPointer === event.pointerId)
        //     capturedPointer = null;
        //console.log(`Lost ${event.pointerId}`);
    }

    wheel(event, pos) {
        let scrollAmount = event.deltaY;
        if (event.deltaMode == 1) {
            scrollAmount = -0.2 * scrollAmount / 10;
        } else {
            scrollAmount = scrollAmount / 1000;
        }
        //panContext.zoomIncrease(scrollAmount, pos);
        //let origin = this.calcGripPoint();
        let origin = { x: pos.x + this.panOffset.x, y: pos.y + this.panOffset.y };
        this.setZoom(this.zoom * (1 + scrollAmount), origin);
        // panOffset = { x: panContext.panOffset.x, y: panContext.panOffset.y };
        this.resetGrip();
    }

    updateTouches(event) {
        // this.pointersHist += ` (${event.targetTouches.length},${event.touches.length})`;
        // $("#outputArea")[0].value = this.pointersHist;

        this.pointers = [];
        for (let p of event.touches) {
            this.pointers.push({touch: p, pos: this.clientToCoordSpace(new DOMPoint(p.clientX, p.clientY))});
        }
        //setVisualPointers(this.pointers);
    }

    touchstart(event) {
        if (resizeController != null && resizeController.isResizing)
            return;
        this.isUsingTouch = true;
        this.releaseCaptures();

        this.updateTouches(event);

        this.resetGrip();

        // this.baseGrip = this.calcGripPoint();
        // this.gestureBaseDist = this.calcGripDist();
        // this.gestureBaseZoom = this.zoom;

        // this.pointersHist += " +";
        // $("#outputArea")[0].value = this.pointersHist;

        // this.pointersHist += ` ${this.pointers.length}`;
        // $("#outputArea")[0].value = this.pointersHist;
    }

    touchmove(event) {
        if (resizeController != null && resizeController.isResizing)
            return;
        this.updateTouches(event);

        this.updatePanZoom();
    }

    touchend(event) {
        if (resizeController != null && resizeController.isResizing)
            return;
        this.updateTouches(event);
        // this.pointersHist += " -";
        //     $("#outputArea")[0].value = this.pointersHist;

        if (this.pointers.length == 0) {
            // this.pointersHist += " 0";
            // $("#outputArea")[0].value = this.pointersHist;
            
            this.isUsingTouch = false;
        }

        this.resetGrip();
    }

    touchcancel(event) {
        if (resizeController != null && resizeController.isResizing)
            return;
        this.updateTouches(event);
        // this.pointersHist += " -";
        // $("#outputArea")[0].value = this.pointersHist;

        if (this.pointers.length == 0) {
            // this.pointersHist += " 0";
            // $("#outputArea")[0].value = this.pointersHist;

            this.isUsingTouch = false;
        }

        this.resetGrip();
    }

    releaseCaptures() {
        for (let p of this.pointers) {
            if (p.ev?.pointerId != null && this.targetEl.hasPointerCapture(p.ev.pointerId))
                this.targetEl.releasePointerCapture(p.ev.pointerId);
        }
    }

    dismiss() {
        this.releaseCaptures();

        //$(".pannable-diagram").css("cursor", "");
        this.targetEl.style.cursor = "";

        if (this.ondismiss != null)
            this.ondismiss();
        //that.untoggle();
    }

    engage() {
        //let panEl = $("#pixelartSvg")[0];
        let panEl = this.targetEl;
        // let panContext = diagram;

        panEl.style.cursor = "all-scroll";

        // const that = this;
        // let i = 0;

        setDiagramHandle(this);
    }
}

class PanButton extends ToggleButton {
    constructor() {
        super($("#panButton")[0]);

        const that = this;
        this.panAndZoom = new PanZoomController($("#pixelartSvg")[0], () => { that.updateTarget() });
        this.panAndZoom.ondismiss = () => {
            that.untoggle();
            if (resizeController != null)
                resizeController.setInvisible();
        }
        this.isUpdatingTarget = false;

        this.panAndZoom.clientToCoordSpace = (pos) => {
            return diagram.toSVGSpace(pos);
        };

        this.panAndZoom.coordToClientSpace = (pos) => {
            return diagram.toClientSpace(pos);
        };

        this.panAndZoom.onresetgrip = () => {
            that.panAndZoom.setZoom(diagram.zoom);
            that.panAndZoom.setPanOffset(diagram.panOffset.x, diagram.panOffset.y);
        }
    }

    onToggle() {
        $(".coloroption-pan")[0].classList.add("coloroption-selected");
        this.panAndZoom.engage();
        if (resizeController != null)
            resizeController.setVisible();
    }

    updateTarget() {
        if (this.isUpdatingTarget)
            return;
        this.isUpdatingTarget = true;

        diagram.setZoom(this.panAndZoom.getZoom());
        if (diagram.zoom != this.panAndZoom.zoom) {
            this.panAndZoom.setZoom(diagram.zoom);
        }

        diagram.setPanOffset(this.panAndZoom.getPanOffset().x, this.panAndZoom.getPanOffset().y);
        // if (diagram.panOffset.x != this.panAndZoom.panOffset.x || diagram.panOffset.y != this.panAndZoom.panOffset.y)
        //     this.panAndZoom.setPanOffset(diagram.panOffset.x, diagram.panOffset.y);

        this.isUpdatingTarget = false;
    }

    onUntoggle() {
        setDiagramHandle({});
        $(".coloroption-pan")[0].classList.remove("coloroption-selected");
    }
}

