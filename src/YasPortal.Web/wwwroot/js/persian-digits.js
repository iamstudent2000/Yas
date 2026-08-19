/* YasPortal — Persian digit normalization.
   Converts displayed Latin/Arabic-Indic digits into Persian digits.
   Editable fields and technical/code content remain untouched.
*/
(function () {
    'use strict';

    const digitMap = {
        '0': '۰', '1': '۱', '2': '۲', '3': '۳', '4': '۴',
        '5': '۵', '6': '۶', '7': '۷', '8': '۸', '9': '۹',
        '٠': '۰', '١': '۱', '٢': '۲', '٣': '۳', '٤': '۴',
        '٥': '۵', '٦': '۶', '٧': '۷', '٨': '۸', '٩': '۹'
    };

    const excluded = new Set([
        'SCRIPT', 'STYLE', 'INPUT', 'TEXTAREA', 'SELECT', 'OPTION',
        'CODE', 'PRE'
    ]);

    function shouldExclude(node) {
        const parent = node.parentElement;
        return !parent || excluded.has(parent.tagName) || !!parent.closest('[data-persian-digits="off"]');
    }

    function normalizeTextNode(node) {
        if (shouldExclude(node)) return;

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
        if (root.nodeType === Node.ELEMENT_NODE && excluded.has(root.tagName)) return;
        if (root.closest && root.closest('[data-persian-digits="off"]')) return;

        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        const nodes = [];
        let node;
        while ((node = walker.nextNode())) nodes.push(node);
        nodes.forEach(normalizeTextNode);
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

        observer.observe(document.body, {
            childList: true,
            subtree: true,
            characterData: true
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
