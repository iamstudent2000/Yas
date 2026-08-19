window.activePositionDropdown = (() => {
    let outsideHandler = null;
    let resizeHandler = null;
    let scrollHandler = null;
    let escapeHandler = null;
    let portal = null;
    let source = null;
    let triggerElement = null;

    function setImportant(element, property, value) {
        element.style.setProperty(property, value, 'important');
    }

    function position(trigger, dropdown) {
        if (!trigger || !dropdown || !dropdown.isConnected) return;

        const rect = trigger.getBoundingClientRect();
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight;
        const margin = 8;
        const gap = 6;

        const width = Math.min(Math.max(rect.width, 280), viewportWidth - (margin * 2));

        setImportant(dropdown, 'position', 'fixed');
        setImportant(dropdown, 'display', 'block');
        setImportant(dropdown, 'visibility', 'hidden');
        setImportant(dropdown, 'width', `${Math.round(width)}px`);
        setImportant(dropdown, 'left', '0px');
        setImportant(dropdown, 'right', 'auto');
        setImportant(dropdown, 'top', '0px');
        setImportant(dropdown, 'bottom', 'auto');
        setImportant(dropdown, 'margin', '0');
        setImportant(dropdown, 'z-index', '2147483647');
        setImportant(dropdown, 'max-width', `calc(100vw - ${margin * 2}px)`);
        setImportant(dropdown, 'overflow', 'hidden');
        setImportant(dropdown, 'transform', 'none');

        const naturalHeight = Math.min(dropdown.scrollHeight || dropdown.getBoundingClientRect().height, 420);
        const below = Math.max(0, viewportHeight - rect.bottom - margin - gap);
        const above = Math.max(0, rect.top - margin - gap);
        const openAbove = below < naturalHeight && above > below;
        const availableHeight = Math.max(120, openAbove ? above : below);
        const finalHeight = Math.min(naturalHeight, availableHeight);

        setImportant(dropdown, 'height', 'auto');
        setImportant(dropdown, 'max-height', `${Math.round(finalHeight)}px`);

        let top = openAbove
            ? rect.top - finalHeight - gap
            : rect.bottom + gap;

        top = Math.max(margin, Math.min(top, viewportHeight - finalHeight - margin));

        let left = rect.right - width;
        left = Math.max(margin, Math.min(left, viewportWidth - width - margin));

        setImportant(dropdown, 'left', `${Math.round(left)}px`);
        setImportant(dropdown, 'top', `${Math.round(top)}px`);
        setImportant(dropdown, 'visibility', 'visible');
    }

    function createPortal(sourceDropdown) {
        const clone = sourceDropdown.cloneNode(true);

        clone.removeAttribute('hidden');
        clone.classList.add('active-position-dropdown-portal');
        clone.dataset.portalFor = 'active-position';

        // The portal must not inherit the original dropdown's containing/overflow layout.
        setImportant(clone, 'position', 'fixed');
        setImportant(clone, 'display', 'block');
        setImportant(clone, 'visibility', 'hidden');
        setImportant(clone, 'z-index', '2147483647');
        setImportant(clone, 'margin', '0');
        setImportant(clone, 'transform', 'none');
        setImportant(clone, 'inset', 'auto');
        setImportant(clone, 'overflow', 'hidden');
        setImportant(clone, 'box-sizing', 'border-box');

        // Append directly to body. It is no longer a descendant of the header/section.
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
        if (!trigger || !dropdown || !document.body) return;

        triggerElement = trigger;
        source = dropdown;
        setImportant(source, 'display', 'none');

        portal = createPortal(source);
        bindOptionClicks(portal, source);
        position(triggerElement, portal);

        resizeHandler = () => position(triggerElement, portal);
        scrollHandler = () => position(triggerElement, portal);

        escapeHandler = event => {
            if (event.key === 'Escape') {
                dotNetRef.invokeMethodAsync('CloseDropdown');
            }
        };

        outsideHandler = event => {
            const target = event.target;
            if (!portal?.contains(target) && !triggerElement?.contains(target)) {
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
        if (source) source.style.removeProperty('display');

        portal = null;
        source = null;
        triggerElement = null;
        resizeHandler = null;
        scrollHandler = null;
        escapeHandler = null;
        outsideHandler = null;
    }

    return { start, stop, position };
})();
