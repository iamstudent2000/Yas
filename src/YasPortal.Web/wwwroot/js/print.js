// Printing helpers. A single request is printed by cloning its card into a root of its own and hiding the rest of
// the page, so the app shell (fixed height, scroll containers) can never cut the printout short.
window.yasPrintElement = function (element, title) {
    if (!element) { window.print(); return; }
    var root = document.createElement('div');
    root.id = 'yas-print-root';
    root.setAttribute('dir', 'rtl');
    var clone = element.cloneNode(true);
    root.appendChild(clone);
    document.body.appendChild(root);
    var html = document.documentElement;
    var scheme = html.getAttribute('data-scheme');
    var oldTitle = document.title;
    html.setAttribute('data-scheme', 'light');   // dark theme colours are unreadable on paper
    if (title) document.title = title;
    document.body.classList.add('yas-printing');
    var cleanup = function () {
        document.body.classList.remove('yas-printing');
        if (root.parentNode) root.parentNode.removeChild(root);
        if (scheme === null) html.removeAttribute('data-scheme'); else html.setAttribute('data-scheme', scheme);
        document.title = oldTitle;
        window.removeEventListener('afterprint', cleanup);
    };
    window.addEventListener('afterprint', cleanup);
    window.print();
    // Browsers that return from print() without firing afterprint still get cleaned up.
    setTimeout(cleanup, 1500);
};

window.yasPrintPage = function () { window.print(); };
