window.serverLookupDropdown = {
    position: function (trigger, menu) {
        if (!trigger || !menu) return;

        const rect = trigger.getBoundingClientRect();
        const gap = 6;
        const viewportPadding = 8;
        const availableBelow = window.innerHeight - rect.bottom - gap - viewportPadding;
        const availableAbove = rect.top - gap - viewportPadding;
        const minHeight = 120;
        const preferredHeight = 320;

        let openUp = false;
        if (availableBelow < minHeight && availableAbove > availableBelow) {
            openUp = true;
        }

        const height = Math.max(minHeight, Math.min(preferredHeight, openUp ? availableAbove : availableBelow));
        const width = rect.width;

        menu.style.position = 'fixed';
        menu.style.zIndex = '2147483647';
        menu.style.width = `${width}px`;
        menu.style.maxWidth = `calc(100vw - ${viewportPadding * 2}px)`;
        menu.style.maxHeight = `${height}px`;
        menu.style.right = 'auto';
        menu.style.left = `${Math.max(viewportPadding, Math.min(rect.left, window.innerWidth - width - viewportPadding))}px`;
        menu.style.top = openUp
            ? `${Math.max(viewportPadding, rect.top - height - gap)}px`
            : `${Math.min(window.innerHeight - height - viewportPadding, rect.bottom + gap)}px`;

        menu.dataset.openUp = openUp ? 'true' : 'false';
    },

    clear: function (menu) {
        if (!menu) return;
        menu.style.position = '';
        menu.style.zIndex = '';
        menu.style.width = '';
        menu.style.maxWidth = '';
        menu.style.maxHeight = '';
        menu.style.right = '';
        menu.style.left = '';
        menu.style.top = '';
    }
};
