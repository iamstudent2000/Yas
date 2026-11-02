// Printing helper: lists print as they are on screen (print.css hides the app chrome).
window.yasPrintPage = function () { window.print(); };

// The phone book records every print, also one started with Ctrl+P: the browser tells the page just before it prints.
window.yasWatchPrint = function (ref) {
    window.yasUnwatchPrint();
    window._yasPrintHandler = function () { ref.invokeMethodAsync('OnBrowserPrint').catch(function () { }); };
    window.addEventListener('beforeprint', window._yasPrintHandler);
};
window.yasUnwatchPrint = function () {
    if (window._yasPrintHandler) { window.removeEventListener('beforeprint', window._yasPrintHandler); window._yasPrintHandler = null; }
};
