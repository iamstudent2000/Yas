window.activePositionDropdown = (() => {
    let outsideHandler = null;
    let resizeHandler = null;
    let scrollHandler = null;
    let escapeHandler = null;
    let portal = null;
    let source = null;

    function position(trigger, dropdown) {
        if (!trigger || !dropdown) return;

        const rect = trigger.getBoundingClientRect();
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const margin = 8;
        const gap = 6;
        const width = Math.min(Math.max(rect.width, 280), vw - margin * 2);

        dropdown.style.width = `${width}px`;
        dropdown.style.maxHeight = `${Math.max(160, vh - margin * 2)}px`;
        dropdown.style.left = '0px';
        dropdown.style.top = '0px';
        dropdown.style.visibility = 'hidden';

        const measured = dropdown.getBoundingClientRect();
        const naturalHeight = Math.min(measured.height, 420);
        const below = Math.max(0, vh - rect.bottom - margin);
        const above = Math.max(0, rect.top - margin);
        const openAbove = below < naturalHeight && above > below;
        const available = openAbove ? above : below;
        const finalHeight = Math.max(120, Math.min(naturalHeight, available || naturalHeight));

        dropdown.style.maxHeight = `${finalHeight}px`;
        dropdown.style.overflow = 'hidden';

        let top = openAbove
            ? rect.top - finalHeight - gap
            : rect.bottom + gap;
        top = Math.max(margin, Math.min(top, vh - finalHeight - margin));

        let left = rect.right - width;
        left = Math.max(margin, Math.min(left, vw - width - margin));

        dropdown.style.left = `${Math.round(left)}px`;
        dropdown.style.top = `${Math.round(top)}px`;
        dropdown.style.visibility = 'visible';
    }

    function createPortal(sourceDropdown) {
        const clone = sourceDropdown.cloneNode(true);
        clone.removeAttribute('style');
        clone.classList.add('active-position-dropdown-portal');
        clone.style.position = 'fixed';
        clone.style.zIndex = '2147483647';
        clone.style.display = 'block';
        clone.style.visibility = 'hidden';
        clone.dataset.portalFor = 'active-position';
        document.body.appendChild(clone);
        return clone;
    }

    function bindOptionClicks(clone, sourceDropdown) {
        const cloneOptions = [...clone.querySelectorAll('.active-position-option')];
        const sourceOptions = [...sourceDropdown.querySelectorAll('.active-position-option')];

        cloneOptions.forEach((option, index) => {
            option.addEventListener('click', event => {
                event.preventDefault();
                event.stopPropagation();
                sourceOptions[index]?.click();
            });
        });
    }

    function start(trigger, dropdown, dotNetRef) {
        stop();
        if (!trigger || !dropdown) return;

        source = dropdown;
        source.style.display = 'none';
        portal = createPortal(source);
        bindOptionClicks(portal, source);
        position(trigger, portal);

        resizeHandler = () => position(trigger, portal);
        scrollHandler = () => position(trigger, portal);
        escapeHandler = event => {
            if (event.key === 'Escape') dotNetRef.invokeMethodAsync('CloseDropdown');
        };
        outsideHandler = event => {
            if (!portal?.contains(event.target) && !trigger.contains(event.target)) {
                dotNetRef.invokeMethodAsync('CloseDropdown');
            }
        };

        window.addEventListener('resize', resizeHandler);
        window.addEventListener('scroll', scrollHandler, true);
        document.addEventListener('keydown', escapeHandler, true);
        document.addEventListener('pointerdown', outsideHandler, true);
    }

    function stop() {
        if (resizeHandler) window.removeEventListener('resize', resizeHandler);
        if (scrollHandler) window.removeEventListener('scroll', scrollHandler, true);
        if (escapeHandler) document.removeEventListener('keydown', escapeHandler, true);
        if (outsideHandler) document.removeEventListener('pointerdown', outsideHandler, true);

        if (portal) portal.remove();
        if (source) source.style.display = '';

        portal = null;
        source = null;
        resizeHandler = null;
        scrollHandler = null;
        escapeHandler = null;
        outsideHandler = null;
    }

    return { start, stop, position };
})();
