let canvas;
let context;
let latticeCanvas;
let latticeContext;
let image;
let width;
let height;
let typeByCellId;
let boundaryMode;
let dotNet;
let currentIds;
let currentMcs = 0;
let view = "identity";
let boundaries = false;
let scale = 1;
let offsetX = 0;
let offsetY = 0;
let drag;
let suppressClickAfterDrag = false;

const fixedTypePalette = [
    [54, 82, 196], [224, 112, 58], [41, 154, 112], [181, 77, 154],
    [220, 177, 57], [52, 156, 190], [199, 78, 76], [121, 101, 181]
];

export function initialise(element, latticeWidth, latticeHeight, cellTypes, latticeBoundaryMode, callback) {
    canvas = element;
    context = canvas.getContext("2d", { alpha: false });
    width = latticeWidth;
    height = latticeHeight;
    typeByCellId = cellTypes;
    boundaryMode = latticeBoundaryMode;
    dotNet = callback;
    canvas.width = width;
    canvas.height = height;
    latticeCanvas = document.createElement("canvas");
    latticeCanvas.width = width;
    latticeCanvas.height = height;
    latticeContext = latticeCanvas.getContext("2d", { alpha: false });
    image = latticeContext.createImageData(width, height);
    context.imageSmoothingEnabled = false;
    latticeContext.imageSmoothingEnabled = false;
    canvas.addEventListener("wheel", onWheel, { passive: false });
    canvas.addEventListener("pointerdown", onPointerDown);
    canvas.addEventListener("pointermove", onPointerMove);
    canvas.addEventListener("pointerup", onPointerUp);
    canvas.addEventListener("pointercancel", onPointerUp);
    canvas.addEventListener("click", onClick);
    render();
}

export function draw(bytes, latticeWidth, latticeHeight, mcs, nextView, showBoundaries) {
    if (latticeWidth !== width || latticeHeight !== height) {
        throw new Error("The published lattice dimensions changed during a session.");
    }
    if (bytes.byteLength !== width * height * Int32Array.BYTES_PER_ELEMENT)
        throw new Error("The published frame byte length does not match the lattice dimensions.");
    currentIds = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    currentMcs = mcs;
    view = nextView;
    boundaries = showBoundaries;
    const rgba = image.data;
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const index = y * width + x;
            const cellId = currentIds.getInt32(index * Int32Array.BYTES_PER_ELEMENT, true);
            const pixel = index * 4;
            let colour = cellId === 0 ? [14, 20, 29] : view === "type"
                ? colourForType(typeByCellId[cellId] ?? 0)
                : colourForIdentity(cellId);
            if (boundaries && cellId !== 0 && hasDifferentNeighbour(x, y, cellId))
                colour = [244, 247, 255];
            rgba[pixel] = colour[0];
            rgba[pixel + 1] = colour[1];
            rgba[pixel + 2] = colour[2];
            rgba[pixel + 3] = 255;
        }
    }
    latticeContext.putImageData(image, 0, 0);
    render();
    canvas.dataset.displayMcs = String(currentMcs);
}

export function setView(nextView) {
    view = nextView;
    redrawFromCurrent();
}

export function setBoundaries(enabled) {
    boundaries = enabled;
    redrawFromCurrent();
}

function redrawFromCurrent() {
    if (!currentIds)
        return;
    const ids = currentIds;
    const bytes = new Uint8Array(ids.buffer, ids.byteOffset, ids.byteLength);
    draw(bytes, width, height, currentMcs, view, boundaries);
}

function render() {
    if (!context)
        return;
    context.setTransform(1, 0, 0, 1, 0, 0);
    context.fillStyle = "#0e141d";
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.translate(offsetX, offsetY);
    context.scale(scale, scale);
    context.drawImage(latticeCanvas, 0, 0);
}

function hasDifferentNeighbour(x, y, cellId) {
    const points = [[x - 1, y], [x + 1, y], [x, y - 1], [x, y + 1]];
    for (const [nextX, nextY] of points) {
        let neighbourX = nextX;
        let neighbourY = nextY;
        if (boundaryMode === "Periodic") {
            neighbourX = (nextX + width) % width;
            neighbourY = (nextY + height) % height;
        } else if (nextX < 0 || nextY < 0 || nextX >= width || nextY >= height) {
            continue;
        }
        if (currentIds.getInt32((neighbourY * width + neighbourX) * Int32Array.BYTES_PER_ELEMENT, true) !== cellId)
            return true;
    }
    return false;
}

function colourForIdentity(cellId) {
    let hash = Math.imul(cellId >>> 0, 2654435761) >>> 0;
    return hslToRgb(hash % 360, 0.64, 0.54);
}

function colourForType(typeId) {
    if (typeId <= 0)
        return [14, 20, 29];
    const fixed = fixedTypePalette[typeId - 1];
    if (fixed)
        return fixed;
    return hslToRgb((typeId * 137.508) % 360, 0.68, 0.54);
}

function hslToRgb(hue, saturation, lightness) {
    const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
    const sector = hue / 60;
    const secondary = chroma * (1 - Math.abs((sector % 2) - 1));
    let red = 0, green = 0, blue = 0;
    if (sector < 1) [red, green, blue] = [chroma, secondary, 0];
    else if (sector < 2) [red, green, blue] = [secondary, chroma, 0];
    else if (sector < 3) [red, green, blue] = [0, chroma, secondary];
    else if (sector < 4) [red, green, blue] = [0, secondary, chroma];
    else if (sector < 5) [red, green, blue] = [secondary, 0, chroma];
    else [red, green, blue] = [chroma, 0, secondary];
    const offset = lightness - chroma / 2;
    return [Math.round((red + offset) * 255), Math.round((green + offset) * 255), Math.round((blue + offset) * 255)];
}

function onWheel(event) {
    // Keep ordinary wheel and trackpad gestures available to the page. Canvas zoom is
    // deliberately modifier-gated so a large lattice cannot trap document scrolling.
    if (!event.ctrlKey && !event.metaKey)
        return;

    event.preventDefault();
    const point = canvasPoint(event);
    const oldScale = scale;
    scale = Math.max(1, Math.min(32, scale * (event.deltaY < 0 ? 1.15 : 1 / 1.15)));
    const latticeX = (point.x - offsetX) / oldScale;
    const latticeY = (point.y - offsetY) / oldScale;
    offsetX = point.x - latticeX * scale;
    offsetY = point.y - latticeY * scale;
    render();
}

function onPointerDown(event) {
    if (event.button !== 0)
        return;
    canvas.setPointerCapture(event.pointerId);
    drag = { pointerId: event.pointerId, x: event.clientX, y: event.clientY, offsetX, offsetY, moved: false };
}

function onPointerMove(event) {
    if (!drag || drag.pointerId !== event.pointerId)
        return;
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 2)
        drag.moved = true;
    if (drag.moved) {
        offsetX = drag.offsetX + dx * canvas.width / canvas.clientWidth;
        offsetY = drag.offsetY + dy * canvas.height / canvas.clientHeight;
        render();
    }
}

function onPointerUp(event) {
    if (drag?.pointerId === event.pointerId) {
        suppressClickAfterDrag = drag.moved;
        canvas.releasePointerCapture(event.pointerId);
        drag = null;
    }
}

function onClick(event) {
    if (suppressClickAfterDrag) {
        suppressClickAfterDrag = false;
        return;
    }
    if (!currentIds)
        return;
    const point = canvasPoint(event);
    const x = Math.floor((point.x - offsetX) / scale);
    const y = Math.floor((point.y - offsetY) / scale);
    if (x >= 0 && y >= 0 && x < width && y < height)
        dotNet.invokeMethodAsync("OnCellSelected", currentIds.getInt32((y * width + x) * Int32Array.BYTES_PER_ELEMENT, true), currentMcs);
}

function canvasPoint(event) {
    const bounds = canvas.getBoundingClientRect();
    return {
        x: (event.clientX - bounds.left) * canvas.width / bounds.width,
        y: (event.clientY - bounds.top) * canvas.height / bounds.height
    };
}
