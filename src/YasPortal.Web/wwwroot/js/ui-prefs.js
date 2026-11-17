// Applies a user's appearance settings to <html>. Also used by the inline head script in App.razor.
window.yasUi = (function () {
    var root = document.documentElement;
    var media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    // Works out whether the page should look light or dark. 'system' follows the computer's own setting.
    // The result is stored on the page as data-scheme, and the stylesheet reads it.
    function resolve() {
        var theme = root.getAttribute('data-theme') || 'system';
        var dark = theme === 'dark' || (theme === 'system' && media && media.matches);
        root.setAttribute('data-scheme', dark ? 'dark' : 'light');
    }

    // When the computer switches between light and dark, update the page at once.
    if (media && media.addEventListener) media.addEventListener('change', resolve);

    return {
        resolve: resolve,
        // Apply the person's saved choices (theme, accent colour, font, text size, density) as attributes on the page.
        apply: function (p) {
            ['theme', 'accent', 'font', 'size', 'density'].forEach(function (k) {
                if (p && p[k]) root.setAttribute('data-' + k, p[k]);
            });
            resolve();
        }
    };
})();
// Run once when the file loads so the first screen already has the right look.
window.yasUi.resolve();

// The content area (not the window) scrolls, so a page change has to scroll it back to the top itself.
(function () {
    // Remember the current address; when it changes (the person moved to another page), scroll the content back to the top.
    var last = location.pathname;
    function check() {
        if (location.pathname === last) return;
        last = location.pathname;
        var content = document.querySelector('main.content');
        if (content) content.scrollTop = 0;
    }
    // The portal changes pages without a full reload. These two browser actions signal such a change, so they are wrapped to run the check.
    ['pushState', 'replaceState'].forEach(function (name) {
        var original = history[name];
        history[name] = function () {
            var result = original.apply(this, arguments);
            setTimeout(check, 0);
            return result;
        };
    });
    // The browser's back and forward buttons also count as a page change.
    window.addEventListener('popstate', check);
})();
