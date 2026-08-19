window.activePositionDropdown = (() => {
    let outsideHandler = null;
    let resizeHandler = null;
    let scrollHandler = null;

    function position(trigger, dropdown) {
        if (!trigger || !dropdown) return;

        const rect = trigger.getBoundingClientRect();
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;
        const gap = 6;
        const margin = 8;

        dropdown.style.width = Math.min(Math.max(rect.width, 280), viewportWidth - margin * 2) + 'px';
        dropdown.style.maxHeight = Math.max(180, viewportHeight - margin * 2) + 'px';

        // Render invisibly first so its real height is available.
        dropdown.style.visibility = 'hidden';
        dropdown.style.left = '0px';
        dropdown.style.top = '0px';

        const menuRect = dropdown.getBoundingClientRect();
        const availableBelow = viewportHeight - rect.bottom - margin;
        const availableAbove = rect.top - margin;
        const openAbove = availableBelow < Math.min(menuRect.height, 420) && availableAbove > availableBelow;

        const height = Math.min(menuRect.height, openAbove ? availableAbove : availableBelow);
        dropdown.style.maxHeight = Math.max(180, height) + 'px';

        const finalHeight = Math.min(menuRect.height, Math.max(180, height));
        let top = openAbove ? rect.top - finalHeight - gap : rect.bottom + gap;
        top = Math.max(margin, Math.min(top, viewportHeight - finalHeight - margin));

        // RTL: align the dropdown's right edge with the trigger's right edge.
        let left = rect.right - menuRect.width;
        left = Math.max(margin, Math.min(left, viewportWidth - menuRect.width - margin));

        dropdown.style.left = `${left}px`;
        dropdown.style.top = `${top}px`;
        dropdown.style.visibility = 'visible';
    }

    function start(trigger, dropdown, dotNetRef) {
        stop();
        position(trigger, dropdown);

        resizeHandler = () => position(trigger, dropdown);
        scrollHandler = () => position(trigger, dropdown);
        outsideHandler = (event) => {
            if (!dropdown.contains(event.target) && !trigger.contains(event.target)) {
                dotNetRef.invokeMethodAsync('CloseDropdown');
            }
        };

        window.addEventListener('resize', resizeHandler);
        window.addEventListener('scroll', scrollHandler, true);
        document.addEventListener('pointerdown', outsideHandler, true);
    }

    function stop() {
        if (resizeHandler) window.removeEventListener('resize', resizeHandler);
        if (scrollHandler) window.removeEventListener('scroll', scrollHandler, true);
        if (outsideHandler) document.removeEventListener('pointerdown', outsideHandler, true);
        resizeHandler = null;
        scrollHandler = null;
        outsideHandler = null;
    }

    return { start, stop, position };
})();
