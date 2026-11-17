// Positions the drop-down list of the search-as-you-type pickers (the server lookup selector). The list opens below its box,
// or above it when there is not enough room, and stays inside the screen.
window.serverLookupDropdown = (() => {
    const handlers = new WeakMap();

    // Work out size and place of the list from the free space above and below the box, then place it.
    function position(trigger, menu) {
        if (!trigger || !menu || !document.body.contains(trigger) || !document.body.contains(menu)) return;

        const rect = trigger.getBoundingClientRect();
        const gap = 6;
        const padding = 8;
        const minHeight = 120;
        const preferredHeight = 320;
        const availableBelow = Math.max(0, window.innerHeight - rect.bottom - gap - padding);
        const availableAbove = Math.max(0, rect.top - gap - padding);
        const openUp = availableBelow < minHeight && availableAbove > availableBelow;
        const available = openUp ? availableAbove : availableBelow;
        const height = Math.max(80, Math.min(preferredHeight, available));
        const width = Math.min(rect.width, window.innerWidth - padding * 2);
        const left = Math.max(padding, Math.min(rect.left, window.innerWidth - width - padding));

        menu.style.zIndex = '2147483647';
        menu.style.maxWidth = `calc(100vw - ${padding * 2}px)`;
        menu.style.maxHeight = `${height}px`;
        const top = openUp
            ? Math.max(padding, rect.top - height - gap)
            : Math.min(window.innerHeight - height - padding, rect.bottom + gap);
        window.yasPopup.place(menu, left, top, width);
        menu.dataset.openUp = openUp ? 'true' : 'false';
    }

    // Start keeping the list in place while the window is resized or any area is scrolled.
    function attach(trigger, menu) {
        if (!trigger || !menu) return;
        detach(trigger, menu);
        const reposition = () => position(trigger, menu);
        window.addEventListener('resize', reposition, { passive: true });
        window.addEventListener('scroll', reposition, { passive: true, capture: true });
        handlers.set(menu, reposition);
        reposition();
    }

    // Stop keeping the list in place (when it closes).
    function detach(trigger, menu) {
        const handler = handlers.get(menu);
        if (!handler) return;
        window.removeEventListener('resize', handler);
        window.removeEventListener('scroll', handler, true);
        handlers.delete(menu);
    }

    return { position, attach, detach };
})();
