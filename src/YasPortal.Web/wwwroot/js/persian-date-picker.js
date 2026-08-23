(() => {
    const POPUP_GAP = 7;
    const VIEWPORT_MARGIN = 8;

    function isVisible(element) {
        if (!element) return false;
        const style = getComputedStyle(element);
        return style.visibility !== 'hidden' && style.display !== 'none' && style.pointerEvents !== 'none';
    }

    function positionPopup(trigger, popup) {
        if (!trigger || !popup || !isVisible(popup)) return;
        const rect = trigger.getBoundingClientRect();
        const width = Math.min(320, window.innerWidth - VIEWPORT_MARGIN * 2);
        popup.style.position = 'fixed'; popup.style.width = `${width}px`; popup.style.right = 'auto'; popup.style.left = 'auto'; popup.style.top = '0px';
        const popupRect = popup.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom - POPUP_GAP - VIEWPORT_MARGIN;
        const spaceAbove = rect.top - POPUP_GAP - VIEWPORT_MARGIN;
        const openAbove = popupRect.height > spaceBelow && spaceAbove > spaceBelow;
        let top = openAbove ? rect.top - popupRect.height - POPUP_GAP : rect.bottom + POPUP_GAP;
        top = Math.max(VIEWPORT_MARGIN, Math.min(top, window.innerHeight - popupRect.height - VIEWPORT_MARGIN));
        let left = rect.right - width;
        left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - width - VIEWPORT_MARGIN));
        popup.style.left = `${left}px`; popup.style.top = `${top}px`;
    }

    function positionMenu(button, menu) {
        if (!button || !menu || !isVisible(menu)) return;
        const rect = button.getBoundingClientRect();
        const width = menu.classList.contains('pdp-month-menu') ? 190 : 180;
        const maxWidth = Math.min(width, window.innerWidth - VIEWPORT_MARGIN * 2);
        menu.style.position = 'fixed'; menu.style.width = `${maxWidth}px`; menu.style.right = 'auto'; menu.style.left = 'auto'; menu.style.top = '0px';
        const menuRect = menu.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom - POPUP_GAP - VIEWPORT_MARGIN;
        const spaceAbove = rect.top - POPUP_GAP - VIEWPORT_MARGIN;
        const openAbove = menuRect.height > spaceBelow && spaceAbove > spaceBelow;
        let top = openAbove ? rect.top - menuRect.height - POPUP_GAP : rect.bottom + POPUP_GAP;
        top = Math.max(VIEWPORT_MARGIN, Math.min(top, window.innerHeight - menuRect.height - VIEWPORT_MARGIN));
        let left = rect.right - maxWidth;
        left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - maxWidth - VIEWPORT_MARGIN));
        menu.style.left = `${left}px`; menu.style.top = `${top}px`;
    }

    function hidePicker(container) {
        container.querySelectorAll('.pdp-popover, .pdp-menu').forEach(element => {
            element.style.visibility = 'hidden';
            element.style.pointerEvents = 'none';
        });
    }

    function closeAll(except = null) {
        document.querySelectorAll('.pdp').forEach(container => {
            if (container !== except) hidePicker(container);
        });
    }

    function reposition(container = null) {
        const containers = container ? [container] : [...document.querySelectorAll('.pdp')];
        containers.forEach(picker => {
            const trigger = picker.querySelector('.pdp-input');
            const popup = picker.querySelector('.pdp-popover');
            if (trigger && popup) positionPopup(trigger, popup);
            const yearButton = picker.querySelector('.pdp-picker:first-child .pdp-picker-button');
            const yearMenu = picker.querySelector('.pdp-picker:first-child .pdp-menu');
            if (yearButton && yearMenu) positionMenu(yearButton, yearMenu);
            const monthButton = picker.querySelector('.pdp-picker:nth-child(2) .pdp-picker-button');
            const monthMenu = picker.querySelector('.pdp-picker:nth-child(2) .pdp-menu');
            if (monthButton && monthMenu) positionMenu(monthButton, monthMenu);
        });
    }

    function scheduleReposition(container = null) { requestAnimationFrame(() => reposition(container)); }

    document.addEventListener('click', event => {
        const picker = event.target.closest('.pdp');
        const trigger = event.target.closest('.pdp-input, .pdp-picker-button');

        if (trigger && picker) {
            // Close every other picker immediately. The clicked picker is the only one
            // allowed to remain open, even if its Blazor render happens asynchronously.
            closeAll(picker);
            scheduleReposition(picker);
            return;
        }

        if (!picker) {
            closeAll();
            return;
        }

        if (event.target.closest('.pdp-menu, .pdp-popover')) {
            scheduleReposition(picker);
        }
    }, true);

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeAll();
    }, true);

    window.addEventListener('resize', () => scheduleReposition(), { passive: true });
    window.addEventListener('scroll', () => scheduleReposition(), { passive: true, capture: true });

    const observer = new MutationObserver(() => {
        // Do not reposition every picker after a Blazor render. That was reopening
        // hidden date pickers. Only currently visible popovers are repositioned.
        scheduleReposition();
    });
    observer.observe(document.body, { childList: true, subtree: true });

    window.persianDatePicker = {
        reposition: () => reposition(),
        closeAll
    };
})();
