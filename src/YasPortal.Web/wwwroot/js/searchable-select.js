(() => {
    const SELECTOR = 'select.form-control:not([data-searchable-disabled])';
    const MIN_OPTIONS = 10;

    function normalize(value) {
        return (value || '').toLocaleLowerCase('fa-IR').trim();
    }

    function shouldEnhance(select) {
        if (!select || select.multiple || select.dataset.searchableEnhanced === 'true') return false;
        if (select.closest('.searchable-select')) return false;
        return select.options.length >= MIN_OPTIONS;
    }

    function enhance(select) {
        if (!shouldEnhance(select)) return;

        const wrapper = document.createElement('div');
        wrapper.className = 'searchable-select';
        wrapper.setAttribute('dir', 'rtl');

        select.parentNode.insertBefore(wrapper, select);
        wrapper.appendChild(select);
        select.dataset.searchableEnhanced = 'true';
        select.classList.add('searchable-native-select');

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'form-control searchable-select-input';
        input.autocomplete = 'off';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.placeholder = select.options[0]?.textContent?.trim() || 'جستجو...';

        const dropdown = document.createElement('div');
        dropdown.className = 'searchable-select-dropdown';
        dropdown.setAttribute('role', 'listbox');
        dropdown.hidden = true;

        wrapper.appendChild(input);
        wrapper.appendChild(dropdown);

        let activeIndex = -1;

        function options() {
            return Array.from(select.options)
                .filter(option => option.value !== '')
                .map(option => ({
                    value: option.value,
                    text: option.textContent.trim(),
                    normalized: normalize(option.textContent)
                }));
        }

        function selectedText() {
            return select.selectedOptions.length && select.value
                ? select.selectedOptions[0].textContent.trim()
                : '';
        }

        function syncInput() {
            input.value = selectedText();
            input.disabled = select.disabled;
            input.title = input.value;
        }

        function close() {
            dropdown.hidden = true;
            input.setAttribute('aria-expanded', 'false');
            activeIndex = -1;
        }

        function choose(option) {
            select.value = option.value;
            select.dispatchEvent(new Event('change', { bubbles: true }));
            syncInput();
            close();
        }

        function render(filter = '') {
            const query = normalize(filter);
            const matches = options().filter(option => !query || option.normalized.includes(query));
            dropdown.replaceChildren();
            activeIndex = -1;

            if (!matches.length) {
                const empty = document.createElement('div');
                empty.className = 'searchable-select-empty';
                empty.textContent = 'موردی پیدا نشد';
                dropdown.appendChild(empty);
                return;
            }

            for (const option of matches.slice(0, 100)) {
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'searchable-select-option';
                item.textContent = option.text;
                item.setAttribute('role', 'option');
                item.dataset.value = option.value;
                if (option.value === select.value) item.classList.add('selected');
                item.addEventListener('mousedown', event => event.preventDefault());
                item.addEventListener('click', () => choose(option));
                dropdown.appendChild(item);
            }

            if (matches.length > 100) {
                const more = document.createElement('div');
                more.className = 'searchable-select-more';
                more.textContent = `نتیجه‌های بیشتر با دقیق‌تر کردن جستجو نمایش داده می‌شوند (${matches.length} مورد)`;
                dropdown.appendChild(more);
            }
        }

        function open() {
            if (input.disabled) return;
            render(input.value === selectedText() ? '' : input.value);
            dropdown.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        }

        input.addEventListener('focus', () => open());
        input.addEventListener('input', () => {
            if (input.value === selectedText()) {
                open();
                return;
            }
            render(input.value);
            dropdown.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        });

        input.addEventListener('keydown', event => {
            const items = Array.from(dropdown.querySelectorAll('.searchable-select-option'));
            if (event.key === 'Escape') {
                syncInput();
                close();
                return;
            }
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                if (dropdown.hidden) open();
                if (items.length) {
                    activeIndex = Math.min(activeIndex + 1, items.length - 1);
                    items.forEach((item, index) => item.classList.toggle('active', index === activeIndex));
                    items[activeIndex]?.scrollIntoView({ block: 'nearest' });
                }
                return;
            }
            if (event.key === 'ArrowUp') {
                event.preventDefault();
                if (items.length) {
                    activeIndex = Math.max(activeIndex - 1, 0);
                    items.forEach((item, index) => item.classList.toggle('active', index === activeIndex));
                    items[activeIndex]?.scrollIntoView({ block: 'nearest' });
                }
                return;
            }
            if (event.key === 'Enter') {
                if (!dropdown.hidden && activeIndex >= 0 && items[activeIndex]) {
                    event.preventDefault();
                    const value = items[activeIndex].dataset.value;
                    const option = options().find(item => item.value === value);
                    if (option) choose(option);
                }
            }
        });

        input.addEventListener('blur', () => {
            window.setTimeout(() => {
                if (!wrapper.contains(document.activeElement)) {
                    syncInput();
                    close();
                }
            }, 120);
        });

        select.addEventListener('change', syncInput);
        syncInput();
    }

    function scan(root = document) {
        root.querySelectorAll?.(SELECTOR).forEach(enhance);
    }

    const observer = new MutationObserver(mutations => {
        for (const mutation of mutations) {
            mutation.addedNodes.forEach(node => {
                if (node.nodeType === Node.ELEMENT_NODE) scan(node);
            });
        }
    });

    function start() {
        scan();
        observer.observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
