(() => {
    const POPUP_GAP = 7;
    const VIEWPORT_MARGIN = 8;

    function positionPopup(trigger, popup) {
        if (!trigger || !popup) return;

        const rect = trigger.getBoundingClientRect();
        const width = Math.min(320, window.innerWidth - VIEWPORT_MARGIN * 2);
        popup.style.position = 'fixed';
        popup.style.width = `${width}px`;
        popup.style.right = 'auto';
        popup.style.left = 'auto';
        popup.style.top = '0px';
        popup.style.visibility = 'hidden';

        const popupRect = popup.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom - POPUP_GAP - VIEWPORT_MARGIN;
        const spaceAbove = rect.top - POPUP_GAP - VIEWPORT_MARGIN;
        const openAbove = popupRect.height > spaceBelow && spaceAbove > spaceBelow;

        let top = openAbove
            ? rect.top - popupRect.height - POPUP_GAP
            : rect.bottom + POPUP_GAP;

        top = Math.max(VIEWPORT_MARGIN, Math.min(top, window.innerHeight - popupRect.height - VIEWPORT_MARGIN));

        // The date picker is RTL, so align the popup's right edge with the trigger's right edge.
        let left = rect.right - width;
        left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - width - VIEWPORT_MARGIN));

        popup.style.left = `${left}px`;
        popup.style.top = `${top}px`;
        popup.style.visibility = 'visible';
    }

    function positionMenu(button, menu) {
        if (!button || !menu) return;

        const rect = button.getBoundingClientRect();
        const width = menu.classList.contains('pdp-month-menu') ? 190 : 180;
        const maxWidth = Math.min(width, window.innerWidth - VIEWPORT_MARGIN * 2);

        menu.style.position = 'fixed';
        menu.style.width = `${maxWidth}px`;
        menu.style.right = 'auto';
        menu.style.left = 'auto';
        menu.style.top = '0px';
        menu.style.visibility = 'hidden';

        const menuRect = menu.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom - POPUP_GAP - VIEWPORT_MARGIN;
        const spaceAbove = rect.top - POPUP_GAP - VIEWPORT_MARGIN;
        const openAbove = menuRect.height > spaceBelow && spaceAbove > spaceBelow;

        let top = openAbove
            ? rect.top - menuRect.height - POPUP_GAP
            : rect.bottom + POPUP_GAP;

        top = Math.max(VIEWPORT_MARGIN, Math.min(top, window.innerHeight - menuRect.height - VIEWPORT_MARGIN));

        let left = rect.right - maxWidth;
        left = Math.max(VIEWPORT_MARGIN, Math.min(left, window.innerWidth - maxWidth - VIEWPORT_MARGIN));

        menu.style.left = `${left}px`;
        menu.style.top = `${top}px`;
        menu.style.visibility = 'visible';
    }

    function repositionAll() {
        document.querySelectorAll('.pdp').forEach(container => {
            const trigger = container.querySelector('.pdp-input');
            const popup = container.querySelector('.pdp-popover');
            if (trigger && popup) positionPopup(trigger, popup);

            const yearButton = container.querySelector('.pdp-picker:first-child .pdp-picker-button');
            const yearMenu = container.querySelector('.pdp-picker:first-child .pdp-menu');
            if (yearButton && yearMenu) positionMenu(yearButton, yearMenu);

            const monthButton = container.querySelector('.pdp-picker:nth-child(2) .pdp-picker-button');
            const monthMenu = container.querySelector('.pdp-picker:nth-child(2) .pdp-menu');
            if (monthButton && monthMenu) positionMenu(monthButton, monthMenu);
        });
    }

    function scheduleReposition() {
        requestAnimationFrame(repositionAll);
    }

    document.addEventListener('click', event => {
        if (event.target.closest('.pdp-input, .pdp-picker-button, .pdp-menu, .pdp-popover')) {
            scheduleReposition();
        }
    }, true);

    window.addEventListener('resize', scheduleReposition, { passive: true });
    window.addEventListener('scroll', scheduleReposition, { passive: true, capture: true });

    const observer = new MutationObserver(scheduleReposition);
    observer.observe(document.body, { childList: true, subtree: true });

    window.persianDatePicker = {
        reposition: repositionAll
    };
})();
