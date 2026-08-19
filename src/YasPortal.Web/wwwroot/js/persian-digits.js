/* YasPortal — Persian digit normalization for Admin UI.
   Converts Latin/Arabic-Indic digits displayed as text into Persian digits.
   Form controls, code/technical values, and editable fields are intentionally excluded.
*/
(function () {
    'use strict';

    const digitMap = {
        '0': '۰', '1': '۱', '2': '۲', '3': '۳', '4': '۴',
        '5': '۵', '6': '۶', '7': '۷', '8': '۸', '9': '۹',
        '٠': '۰', '١': '۱', '٢': '۲', '٣': '۳', '٤': '۴',
        '٥': '۵', '٦': '۶', '٧': '۷', '٨': '۸', '٩': '۹'
    };

    const excluded = new Set(['SCRIPT', 'STYLE', 'INPUT', 'TEXTAREA', 'SELECT', 'OPTION', 'CODE', 'PRE']);
    const scopeSelector = '.admin-page, .access-page, .positions-page, .history-page';

    function normalizeTextNode(node) {
        const parent = node.parentElement;
        if (!parent || excluded.has(parent.tagName) || !parent.closest(scopeSelector)) return;

        const value = node.nodeValue;
        if (!value || !/[0-9٠-٩]/.test(value)) return;

        const normalized = value.replace(/[0-9٠-٩]/g, d => digitMap[d] || d);
        if (normalized !== value) node.nodeValue = normalized;
    }

    function normalize(root) {
        if (!root) return;
        if (root.nodeType === Node.TEXT_NODE) {
            normalizeTextNode(root);
            return;
        }
        if (root.nodeType !== Node.ELEMENT_NODE && root.nodeType !== Node.DOCUMENT_FRAGMENT_NODE) return;

        if (root.matches && root.matches(scopeSelector)) {
            const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
            const nodes = [];
            let node;
            while ((node = walker.nextNode())) nodes.push(node);
            nodes.forEach(normalizeTextNode);
            return;
        }

        if (root.querySelectorAll) {
            root.querySelectorAll(scopeSelector).forEach(normalize);
        }
    }

    function start() {
        normalize(document.body);

        const observer = new MutationObserver(mutations => {
            for (const mutation of mutations) {
                if (mutation.type === 'childList') {
                    mutation.addedNodes.forEach(normalize);
                } else if (mutation.type === 'characterData') {
                    normalizeTextNode(mutation.target);
                }
            }
        });

        observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
