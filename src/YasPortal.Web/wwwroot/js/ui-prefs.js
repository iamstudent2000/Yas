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
