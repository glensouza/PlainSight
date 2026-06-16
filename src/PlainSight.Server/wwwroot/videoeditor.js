// Interactive trim/crop controller for the Content edit modal.
// Owns all smooth pointer interactions (crop box drag/resize, trim handle drag,
// scrub-to-seek) entirely client-side and only commits final values back to
// .NET via JSInvokable callbacks, so Blazor Server latency never enters a drag loop.
(function () {
    "use strict";

    const MinCropFrac = 0.05;
    const MinTrimGap = 0.1;
    let ctrl = null;

    function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }

    function formatTime(t) {
        if (!isFinite(t) || t < 0) { t = 0; }
        const h = Math.floor(t / 3600);
        const m = Math.floor((t % 3600) / 60);
        const s = Math.floor(t % 60);
        const pad = (n) => n.toString().padStart(2, "0");
        const base = h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${m}:${pad(s)}`;
        return `${base}.${Math.floor((t % 1) * 10)}`;
    }

    function Controller(dotNet, cfg) {
        this.dotNet = dotNet;
        this.video = document.getElementById(cfg.videoId);
        this.stage = document.getElementById(cfg.stageId);
        this.track = document.getElementById(cfg.trackId);
        this.startLabel = document.getElementById(cfg.startLabelId);
        this.endLabel = document.getElementById(cfg.endLabelId);
        this.srcW = cfg.srcW;
        this.srcH = cfg.srcH;
        this.duration = cfg.duration;
        this.aspect = 0; // 0 == free

        // Crop region as source fractions [0,1]: top-left (x0,y0) to bottom-right (x1,y1).
        this.x0 = 0; this.y0 = 0; this.x1 = 1; this.y1 = 1;

        // Trim points in seconds.
        this.start = clamp(cfg.start, 0, this.duration);
        this.end = clamp(cfg.end, 0, this.duration);

        this.buildCropBox();
        this.buildTrim();

        this.onResize = () => { this.renderCrop(); this.renderTrim(); };
        window.addEventListener("resize", this.onResize);
        this.video.addEventListener("loadedmetadata", this.onResize);

        this.renderCrop();
        this.renderTrim();
    }

    // ---- Crop ----

    Controller.prototype.buildCropBox = function () {
        const box = document.createElement("div");
        box.className = "ve-crop-box";
        this.box = box;

        const move = document.createElement("div");
        move.className = "ve-crop-move";
        move.dataset.handle = "move";
        box.appendChild(move);

        ["nw", "n", "ne", "e", "se", "s", "sw", "w"].forEach((h) => {
            const handle = document.createElement("div");
            handle.className = "ve-crop-handle ve-crop-" + h;
            handle.dataset.handle = h;
            box.appendChild(handle);
        });

        this.stage.appendChild(box);

        this.onCropDown = (e) => this.beginCropDrag(e);
        box.addEventListener("pointerdown", this.onCropDown);
    };

    Controller.prototype.kFactor = function () {
        // widthFrac = k * heightFrac keeps output pixel ratio == this.aspect.
        return this.aspect * this.srcH / this.srcW;
    };

    Controller.prototype.beginCropDrag = function (e) {
        const handle = e.target.dataset.handle;
        if (!handle) { return; }
        e.preventDefault();
        const rect = this.stage.getBoundingClientRect();
        const snap = { x0: this.x0, y0: this.y0, x1: this.x1, y1: this.y1 };
        const startFx = (e.clientX - rect.left) / rect.width;
        const startFy = (e.clientY - rect.top) / rect.height;

        const onMove = (ev) => {
            const fx = clamp((ev.clientX - rect.left) / rect.width, 0, 1);
            const fy = clamp((ev.clientY - rect.top) / rect.height, 0, 1);
            if (handle === "move") {
                this.moveCrop(snap, fx - startFx, fy - startFy);
            } else if (this.aspect > 0) {
                this.resizeCropAspect(handle, snap, fx, fy);
            } else {
                this.resizeCropFree(handle, snap, fx, fy);
            }
            this.renderCrop();
        };
        const onUp = () => {
            window.removeEventListener("pointermove", onMove);
            window.removeEventListener("pointerup", onUp);
            this.commitCrop();
        };
        window.addEventListener("pointermove", onMove);
        window.addEventListener("pointerup", onUp);
    };

    Controller.prototype.moveCrop = function (snap, dx, dy) {
        const w = snap.x1 - snap.x0;
        const h = snap.y1 - snap.y0;
        const nx = clamp(snap.x0 + dx, 0, 1 - w);
        const ny = clamp(snap.y0 + dy, 0, 1 - h);
        this.x0 = nx; this.x1 = nx + w; this.y0 = ny; this.y1 = ny + h;
    };

    Controller.prototype.resizeCropFree = function (handle, snap, fx, fy) {
        let { x0, y0, x1, y1 } = snap;
        if (handle.includes("w")) { x0 = Math.min(fx, x1 - MinCropFrac); }
        if (handle.includes("e")) { x1 = Math.max(fx, x0 + MinCropFrac); }
        if (handle.includes("n")) { y0 = Math.min(fy, y1 - MinCropFrac); }
        if (handle.includes("s")) { y1 = Math.max(fy, y0 + MinCropFrac); }
        this.x0 = clamp(x0, 0, 1); this.y0 = clamp(y0, 0, 1);
        this.x1 = clamp(x1, 0, 1); this.y1 = clamp(y1, 0, 1);
    };

    Controller.prototype.resizeCropAspect = function (handle, snap, fx, fy) {
        const k = this.kFactor();
        // Anchor the opposite corner, drive width from the horizontal pointer, derive height.
        const anchorX = handle.includes("w") ? snap.x1 : snap.x0;
        const anchorY = handle.includes("n") ? snap.y1 : snap.y0;
        let width = Math.max(MinCropFrac, Math.abs(fx - anchorX));
        let height = width / k;
        if (height > 1) { height = 1; width = k * height; }

        let x0, x1, y0, y1;
        if (handle.includes("w")) { x1 = anchorX; x0 = anchorX - width; } else { x0 = anchorX; x1 = anchorX + width; }
        if (handle.includes("n")) { y1 = anchorY; y0 = anchorY - height; } else { y0 = anchorY; y1 = anchorY + height; }

        // Clamp to bounds, holding the ratio by shrinking from the dragged edges.
        if (x0 < 0) { width += x0; height = width / k; x0 = 0; x1 = handle.includes("w") ? x1 : width; y0 = handle.includes("n") ? y1 - height : y0; y1 = handle.includes("n") ? y1 : y0 + height; }
        if (x1 > 1) { width -= (x1 - 1); height = width / k; x1 = 1; x0 = handle.includes("w") ? 1 - width : x0; y0 = handle.includes("n") ? y1 - height : y0; y1 = handle.includes("n") ? y1 : y0 + height; }
        if (y0 < 0) { height += y0; width = k * height; y0 = 0; }
        if (y1 > 1) { height -= (y1 - 1); width = k * height; y1 = 1; }

        this.x0 = clamp(x0, 0, 1); this.y0 = clamp(y0, 0, 1);
        this.x1 = clamp(x1, 0, 1); this.y1 = clamp(y1, 0, 1);
    };

    Controller.prototype.renderCrop = function () {
        const box = this.box;
        box.style.left = (this.x0 * 100) + "%";
        box.style.top = (this.y0 * 100) + "%";
        box.style.width = ((this.x1 - this.x0) * 100) + "%";
        box.style.height = ((this.y1 - this.y0) * 100) + "%";
        box.classList.toggle("ve-aspect-locked", this.aspect > 0);
    };

    Controller.prototype.commitCrop = function () {
        const left = Math.round(this.x0 * this.srcW);
        const right = Math.round((1 - this.x1) * this.srcW);
        const top = Math.round(this.y0 * this.srcH);
        const bottom = Math.round((1 - this.y1) * this.srcH);
        this.dotNet.invokeMethodAsync("OnCropChanged", left, right, top, bottom);
    };

    Controller.prototype.setAspect = function (ratioText) {
        if (!ratioText || ratioText === "free") {
            this.aspect = 0;
            this.renderCrop();
            return;
        }
        let ratio;
        if (ratioText === "orig") {
            ratio = this.srcW / this.srcH;
        } else {
            const parts = ratioText.split(":");
            ratio = parseFloat(parts[0]) / parseFloat(parts[1]);
        }
        this.aspect = ratio;
        this.reshapeToAspect();
        this.renderCrop();
        this.commitCrop();
    };

    // Reshape the current box to this.aspect, keeping its centre and width where possible.
    Controller.prototype.reshapeToAspect = function () {
        const k = this.kFactor();
        const cx = (this.x0 + this.x1) / 2;
        const cy = (this.y0 + this.y1) / 2;
        let width = this.x1 - this.x0;
        let height = width / k;
        if (height > 1) { height = 1; width = k * height; }
        this.x0 = clamp(cx - width / 2, 0, 1 - width);
        this.x1 = this.x0 + width;
        this.y0 = clamp(cy - height / 2, 0, 1 - height);
        this.y1 = this.y0 + height;
    };

    Controller.prototype.resetCrop = function () {
        this.x0 = 0; this.y0 = 0; this.x1 = 1; this.y1 = 1;
        if (this.aspect > 0) {
            this.reshapeToAspect();
        }
        this.renderCrop();
        this.commitCrop();
    };

    // ---- Trim ----

    Controller.prototype.buildTrim = function () {
        const fill = document.createElement("div");
        fill.className = "ve-trim-fill";
        this.trimFill = fill;
        this.track.appendChild(fill);

        const startH = document.createElement("div");
        startH.className = "ve-trim-handle ve-trim-start";
        startH.dataset.handle = "start";
        this.track.appendChild(startH);

        const endH = document.createElement("div");
        endH.className = "ve-trim-handle ve-trim-end";
        endH.dataset.handle = "end";
        this.track.appendChild(endH);

        this.onTrimDown = (e) => this.beginTrimDrag(e);
        this.track.addEventListener("pointerdown", this.onTrimDown);
    };

    Controller.prototype.timeFromEvent = function (e) {
        const rect = this.track.getBoundingClientRect();
        return clamp((e.clientX - rect.left) / rect.width, 0, 1) * this.duration;
    };

    Controller.prototype.beginTrimDrag = function (e) {
        let handle = e.target.dataset.handle;
        const t = this.timeFromEvent(e);
        // A click on the bare track moves whichever handle is nearer.
        if (!handle) {
            handle = Math.abs(t - this.start) <= Math.abs(t - this.end) ? "start" : "end";
        }
        e.preventDefault();

        const apply = (ev) => {
            const time = this.timeFromEvent(ev);
            if (handle === "start") {
                this.start = clamp(time, 0, this.end - MinTrimGap);
            } else {
                this.end = clamp(time, this.start + MinTrimGap, this.duration);
            }
            this.seek(handle === "start" ? this.start : this.end);
            this.renderTrim();
        };
        apply(e);

        const onMove = (ev) => apply(ev);
        const onUp = () => {
            window.removeEventListener("pointermove", onMove);
            window.removeEventListener("pointerup", onUp);
            this.commitTrim();
        };
        window.addEventListener("pointermove", onMove);
        window.addEventListener("pointerup", onUp);
    };

    Controller.prototype.seek = function (time) {
        if (isFinite(time)) {
            try { this.video.currentTime = time; } catch { /* not seekable yet */ }
        }
    };

    Controller.prototype.stamp = function (which) {
        const t = clamp(this.video.currentTime, 0, this.duration);
        if (which === "start") {
            this.start = Math.min(t, this.end - MinTrimGap);
        } else {
            this.end = Math.max(t, this.start + MinTrimGap);
        }
        this.renderTrim();
        this.commitTrim();
    };

    Controller.prototype.renderTrim = function () {
        const sp = this.duration > 0 ? (this.start / this.duration) * 100 : 0;
        const ep = this.duration > 0 ? (this.end / this.duration) * 100 : 100;
        this.trimFill.style.left = sp + "%";
        this.trimFill.style.width = (ep - sp) + "%";
        this.track.querySelector(".ve-trim-start").style.left = sp + "%";
        this.track.querySelector(".ve-trim-end").style.left = ep + "%";
        if (this.startLabel) { this.startLabel.textContent = formatTime(this.start); }
        if (this.endLabel) { this.endLabel.textContent = formatTime(this.end); }
    };

    Controller.prototype.commitTrim = function () {
        this.dotNet.invokeMethodAsync("OnTrimChanged", this.start, this.end);
    };

    Controller.prototype.dispose = function () {
        window.removeEventListener("resize", this.onResize);
        this.video.removeEventListener("loadedmetadata", this.onResize);
        if (this.box) { this.box.remove(); }
        if (this.track) {
            this.track.removeEventListener("pointerdown", this.onTrimDown);
            this.track.innerHTML = "";
        }
    };

    window.videoEditor = {
        init: function (dotNet, cfg) { if (ctrl) { ctrl.dispose(); } ctrl = new Controller(dotNet, cfg); return true; },
        setAspect: function (ratio) { if (ctrl) { ctrl.setAspect(ratio); } },
        resetCrop: function () { if (ctrl) { ctrl.resetCrop(); } },
        stamp: function (which) { if (ctrl) { ctrl.stamp(which); } },
        dispose: function () { if (ctrl) { ctrl.dispose(); ctrl = null; } }
    };
})();
