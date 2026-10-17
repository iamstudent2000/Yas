// Applies a user's appearance settings to <html>. Also used by the inline head script in App.razor.
window.yasUi = (function () {
    var root = document.documentElement;
    var media = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    function resolve() {
        var theme = root.getAttribute('data-theme') || 'system';
        var dark = theme === 'dark' || (theme === 'system' && media && media.matches);
        root.setAttribute('data-scheme', dark ? 'dark' : 'light');
    }

    if (media && media.addEventListener) media.addEventListener('change', resolve);

    return {
        resolve: resolve,
        apply: function (p) {
            ['theme', 'accent', 'font', 'size', 'density'].forEach(function (k) {
                if (p && p[k]) root.setAttribute('data-' + k, p[k]);
            });
            resolve();
        }
    };
})();
window.yasUi.resolve();

// The content area (not the window) scrolls, so a page change has to scroll it back to the top itself.
(function () {
    var last = location.pathname;
    function check() {
        if (location.pathname === last) return;
        last = location.pathname;
        var content = document.querySelector('main.content');
        if (content) content.scrollTop = 0;
    }
    ['pushState', 'replaceState'].forEach(function (name) {
        var original = history[name];
        history[name] = function () {
            var result = original.apply(this, arguments);
            setTimeout(check, 0);
            return result;
        };
    });
    window.addEventListener('popstate', check);
})();
