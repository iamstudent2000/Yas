(() => {
    const POPUP_GAP = 7;
    const VIEWPORT_MARGIN = 8;

    function isVisible(element) {
        if (!element) return false;
        const style = getComputedStyle(element);
        return style.visibility !== 'hidden' && style.display !== 'none' && style.pointerEvents !== 'none';
    }

    // Wanted width: as wide as the box it opens under, but never narrower than a readable calendar.
    function popupWidth(boxWidth, min, max) {
        return Math.min(Math.max(boxWidth, min), max, window.innerWidth - VIEWPORT_MARGIN * 2);
    }

    // Both popups line up with the right edge of their box (the start edge in a right-to-left page).
    function placeBelow(element, anchorRect, width) {
        // Measure the height at the final width first, then pick below/above and place it.
        window.yasPopup.place(element, 0, 0, width);
        const height = element.getBoundingClientRect().height;
        const spaceBelow = window.innerHeight - anchorRect.bottom - POPUP_GAP - VIEWPORT_MARGIN;
        const spaceAbove = anchorRect.top - POPUP_GAP - VIEWPORT_MARGIN;
        const openAbove = height > spaceBelow && spaceAbove > spaceBelow;
        let top = openAbove ? anchorRect.top - height - POPUP_GAP : anchorRect.bottom + POPUP_GAP;
        top = Math.max(VIEWPORT_MARGIN, Math.min(top, window.innerHeight - height - VIEWPORT_MARGIN));
        let left = anchorRect.right - width;
        left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - width - VIEWPORT_MARGIN));
        window.yasPopup.place(element, left, top, width);
    }

    function positionPopup(trigger, popup) {
        if (!trigger || !popup || !isVisible(popup)) return;
        const rect = trigger.getBoundingClientRect();
        placeBelow(popup, rect, popupWidth(rect.width, 280, 420));
    }

    function positionMenu(button, menu) {
        if (!button || !menu || !isVisible(menu)) return;
        const rect = button.getBoundingClientRect();
        const min = menu.classList.contains('pdp-month-menu') ? 150 : 130;
        placeBelow(menu, rect, popupWidth(rect.width, min, 240));
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
