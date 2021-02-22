"use strict";

class ResizeController {
    constructor() {
        this.rightHandle = { gripPosSvg: null, pointerId: null, el: null, baseWidth: null };
        this.bottomHandle = { gripPosSvg: null, pointerId: null, el: null, baseHeight: null };

        this.handles = { "right": $("#handleRight")[0], "bottom": $("#handleBottom")[0] };
        this.rightHandle.el = this.handles["right"];
        this.bottomHandle.el = this.handles["bottom"];

        const that = this;
        this.handles["right"].addEventListener("pointerdown", e => { that.onrightdown(e) });
        this.handles["right"].addEventListener("pointerup", e => { that.onrightup(e) });
        this.handles["right"].addEventListener("pointercancel", e => { that.onrightup(e) });
        this.handles["right"].addEventListener("pointermove", e => { that.onrightmove(e) });

        this.handles["bottom"].addEventListener("pointerdown", e => { that.onbottomdown(e) });
        this.handles["bottom"].addEventListener("pointerup", e => { that.onbottomup(e) });
        this.handles["bottom"].addEventListener("pointercancel", e => { that.onbottomup(e) });
        this.handles["bottom"].addEventListener("pointermove", e => { that.onbottommove(e) });

        this.leftGrip = 0.0;
        this.topGrip = 0.0;
        this.rightGrip = 0.0;
        this.bottomGrip = 0.0;
        this.isResizing = false;
        this.onResizeDone = () => {};
    }

    setVisible() {
        $("#resizeFrame")[0].style.visibility = "visible";
    }

    setInvisible() {
        $("#resizeFrame")[0].style.visibility = "hidden";
    }

    updateResizeFrame() {
        this.leftGrip = 0.0;
        this.topGrip = 0.0;
        if (this.rightHandle.gripPosSvg == null)
            this.rightGrip = diagram.tileSize * diagram.width;
        if (this.bottomHandle.gripPosSvg == null)
            this.bottomGrip = diagram.tileSize * diagram.height;
    
        // let leftGrip = 0.0;
        // let topGrip = 0.0;
        // let rightGrip = this.tileSize * this.width;
        // let bottomGrip = this.tileSize * this.height;
        let leftGrip = this.leftGrip;
        let topGrip = this.topGrip;
        let rightGrip = this.rightGrip;
        let bottomGrip = this.bottomGrip;

        let resizeFrameEl = $("#resizeFrame")[0];
        let resizeBoxEl = $("#resizeBox")[0];
        let resizeBox2El = $("#resizeBox2")[0];
        if (resizeBoxEl != null) {
            resizeBoxEl.setAttribute("width", `${rightGrip - leftGrip}`);
            resizeBoxEl.setAttribute("height", `${bottomGrip - topGrip}`);
            resizeBoxEl.setAttribute("x", `${leftGrip}`);
            resizeBoxEl.setAttribute("y", `${topGrip}`);

            resizeBox2El.setAttribute("width", `${rightGrip - leftGrip}`);
            resizeBox2El.setAttribute("height", `${bottomGrip - topGrip}`);
            resizeBox2El.setAttribute("x", `${leftGrip}`);
            resizeBox2El.setAttribute("y", `${topGrip}`);
        }

        let handleRightEl = $("#handleRight")[0];
        if (handleRightEl != null)
            handleRightEl.setAttribute("transform", `translate(${rightGrip} ${(topGrip + bottomGrip) / 2})`);
        let handleBottomEl = $("#handleBottom")[0];
        if (handleBottomEl != null)
            handleBottomEl.setAttribute("transform", `translate(${(leftGrip + rightGrip) / 2} ${bottomGrip})`);

    }

    onrightdown(e) {
        this.rightHandle.gripPosSvg = diagram.toSVGSpace({ x: e.clientX, y: e.clientY });
        this.rightHandle.baseWidth = diagram.width;
        this.rightHandle.pointerId = e.pointerId;
        this.isResizing = true;
        this.rightHandle.el.setPointerCapture(this.rightHandle.pointerId);

        $("body")[0].style.cursor = "ew-resize";
        e.stopPropagation();
    }

    onrightup(e) {
        if (this.rightHandle.gripPosSvg == null)
            return;
        $("body")[0].style.cursor = "";
        this.rightHandle.gripPosSvg = null;
        this.rightHandle.pointerId = null;
        if (this.rightHandle.gripPosSvg == null && this.bottomHandle.gripPosSvg == null) {
            this.isResizing = false;
            this.onResizeDone();
        }
        this.updateResizeFrame();
        e.stopPropagation();
    }

    onrightmove(e) {
        if (this.rightHandle.gripPosSvg == null)
            return;
        if (this.rightHandle.pointerId != null && this.rightHandle.pointerId !== e.pointerId)
            return;

        let curPos = diagram.toSVGSpace({ x: e.clientX, y: e.clientY });

        this.rightGrip = curPos.x + diagram.panOffset.x;
        let intentWidth = this.rightHandle.baseWidth + (curPos.x - this.rightHandle.gripPosSvg.x) / diagram.tileSize;
        if (intentWidth >= diagram.width + 0.8) {
            setWidth(Math.floor(intentWidth + 0.2));
        } else if (intentWidth < diagram.width - 1.2) {
            setWidth(Math.floor(intentWidth + 0.25));
        }
        if (Math.abs(intentWidth - diagram.width) < 0.5)
            this.rightGrip = diagram.width * diagram.tileSize;
        this.updateResizeFrame();
        e.stopPropagation();
        //diagram.toSVGSpace({ x: e.clientX, y: e.clientY });
        //console.log()
    }

    onbottomdown(e) {
        this.bottomHandle.gripPosSvg = diagram.toSVGSpace({ x: e.clientX, y: e.clientY });
        this.bottomHandle.baseHeight = diagram.height;
        this.bottomHandle.pointerId = e.pointerId;
        this.isResizing = true;
        this.bottomHandle.el.setPointerCapture(this.bottomHandle.pointerId);

        $("body")[0].style.cursor = "ns-resize";
        e.stopPropagation();
    }

    onbottomup(e) {
        if (this.bottomHandle.gripPosSvg == null)
            return;
        $("body")[0].style.cursor = "";
        this.bottomHandle.gripPosSvg = null;
        this.bottomHandle.pointerId = null;
        if (this.rightHandle.gripPosSvg == null && this.bottomHandle.gripPosSvg == null) {
            this.isResizing = false;
            this.onResizeDone();
        }
        this.updateResizeFrame();
        e.stopPropagation();
    }

    onbottommove(e) {
        if (this.bottomHandle.gripPosSvg == null)
            return;
        if (this.bottomHandle.pointerId != null && this.bottomHandle.pointerId !== e.pointerId)
            return;

        let curPos = diagram.toSVGSpace({ x: e.clientX, y: e.clientY });

        this.bottomGrip = curPos.y + diagram.panOffset.y;
        let intentHeight = this.bottomHandle.baseHeight + (curPos.y - this.bottomHandle.gripPosSvg.y) / diagram.tileSize;
        //console.log(`intentheight: ${intentHeight}`);
        if (intentHeight >= diagram.height + 0.8) {
            setHeight(Math.floor(intentHeight + 0.2));
        } else if (intentHeight < diagram.height - 1.2) {
            setHeight(Math.floor(intentHeight + 0.25));
        }
        if (Math.abs(intentHeight - diagram.height) < 0.5)
            this.bottomGrip = diagram.height * diagram.tileSize;
        this.updateResizeFrame();
        e.stopPropagation();
        //diagram.toSVGSpace({ x: e.clientX, y: e.clientY });
        //console.log()
    }
}

class ResizeButton extends ToggleButton {
    constructor() {
        super($("#resizeButton")[0]);
    }

    onToggle() {
        $(".coloroption-resize")[0].classList.add("coloroption-selected");

    }

    onUntoggle() {
        setDiagramHandle({});
        $(".coloroption-resize")[0].classList.remove("coloroption-selected");
    }
}

