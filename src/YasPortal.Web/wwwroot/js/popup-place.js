// Places a position:fixed popup so its box sits exactly at the wanted viewport coordinates.
// It sets the position, measures where the browser really put it, and corrects the difference, so the
// result is right even when an ancestor changes the coordinate space (CSS zoom, transforms) or the
// browser reports zoomed boxes differently.
window.yasPopup = (() => {
    function place(el, left, top, width) {
        if (!el) return;
        el.style.position = 'fixed';
        el.style.right = 'auto';
        el.style.bottom = 'auto';
        el.style.margin = '0';
        if (width != null) el.style.width = `${Math.round(width)}px`;
        el.style.left = `${left}px`;
        el.style.top = `${top}px`;

        if (width != null) {
            const w = el.getBoundingClientRect().width;
            if (w > 0 && Math.abs(w - width) > 1) el.style.width = `${Math.round(width * width / w)}px`;
        }
        for (let i = 0; i < 2; i++) {
            const r = el.getBoundingClientRect();
            const dx = left - r.left;
            const dy = top - r.top;
            if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) break;
            el.style.left = `${(parseFloat(el.style.left) || 0) + dx}px`;
            el.style.top = `${(parseFloat(el.style.top) || 0) + dy}px`;
        }
    }
    return { place };
})();
