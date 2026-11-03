// Rich-text editor for user guides (Quill, shipped in /lib/quill so it works on an intranet without internet).
// Loaded on demand as an ES module by the guides admin page: init(host, html) builds the editor inside `host`, getHtml(host) reads it.
const base = new URL('../lib/quill/', import.meta.url);
const version = '2.0.3';
let loading = null;

function loadQuill() {
    if (window.Quill) return Promise.resolve();
    if (!loading) {
        loading = new Promise((resolve, reject) => {
            const css = document.createElement('link');
            css.rel = 'stylesheet';
            css.href = new URL('quill.snow.css?v=' + version, base).href;
            document.head.appendChild(css);
            const script = document.createElement('script');
            script.src = new URL('quill.js?v=' + version, base).href;
            script.onload = () => resolve();
            script.onerror = () => { loading = null; reject(new Error('quill failed to load')); };
            document.head.appendChild(script);
        });
    }
    return loading;
}

let registered = false;
function registerFormats(Quill) {
    if (registered) return;
    registered = true;
    // Quill only knows direction "rtl"; a guide also needs left-to-right paragraphs (an English sentence in Persian text).
    const Parchment = Quill.import('parchment');
    const Direction = new Parchment.ClassAttributor('direction', 'ql-direction', { scope: Parchment.Scope.BLOCK, whitelist: ['rtl', 'ltr'] });
    Quill.register({ 'formats/direction': Direction }, true);
}

const titles = {
    '.ql-header': 'قالب بند', '.ql-bold': 'ضخیم', '.ql-italic': 'کج', '.ql-underline': 'زیرخط', '.ql-strike': 'خط‌خورده',
    '.ql-color': 'رنگ نوشته', '.ql-background': 'رنگ پس‌زمینه', '.ql-align': 'تراز', '.ql-link': 'پیوند', '.ql-blockquote': 'نقل‌قول',
    '.ql-clean': 'پاک‌کردن قالب‌بندی', '.ql-list[value="ordered"]': 'فهرست شماره‌دار', '.ql-list[value="bullet"]': 'فهرست نقطه‌ای',
    '.ql-indent[value="-1"]': 'کاهش تورفتگی', '.ql-indent[value="+1"]': 'افزایش تورفتگی',
    '.ql-direction[value="rtl"]': 'راست‌به‌چپ', '.ql-direction[value="ltr"]': 'چپ‌به‌راست',
};

export async function init(host, html, placeholder) {
    await loadQuill();
    const Quill = window.Quill;
    registerFormats(Quill);

    host.innerHTML = '';
    const area = document.createElement('div');
    host.appendChild(area);

    const quill = new Quill(area, {
        theme: 'snow',
        placeholder: placeholder || '',
        modules: {
            toolbar: {
                container: [
                    [{ header: [1, 2, 3, false] }],
                    ['bold', 'italic', 'underline', 'strike'],
                    [{ color: [] }, { background: [] }],
                    [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
                    [{ align: [] }],
                    [{ direction: 'rtl' }, { direction: 'ltr' }],
                    ['link', 'blockquote'],
                    ['clean'],
                ],
                handlers: {
                    // the built-in handler also forces right alignment; the guide only needs the direction itself
                    direction(value) { this.quill.format('direction', value || false, 'user'); },
                },
            },
        },
    });

    quill.root.setAttribute('dir', 'rtl');
    quill.root.setAttribute('lang', 'fa');
    const toolbar = host.querySelector('.ql-toolbar');
    if (toolbar) {
        for (const [selector, title] of Object.entries(titles))
            toolbar.querySelectorAll(selector).forEach(el => el.setAttribute('title', title));
    }

    if (html && html.trim()) quill.setContents(quill.clipboard.convert({ html }), 'silent');
    host.__quill = quill;
}

// The editor's content as clean semantic HTML (real <ul>/<ol>, no editor-only elements). Empty editor -> "".
export function getHtml(host) {
    const quill = host && host.__quill;
    if (!quill) return '';
    if (quill.getText().trim().length === 0) return '';
    // getSemanticHTML writes every space as &nbsp;, which would stop the text from wrapping on the reader's page.
    return quill.getSemanticHTML().replace(/&nbsp;/g, ' ');
}

export function dispose(host) {
    if (host) { host.__quill = null; host.innerHTML = ''; }
}
