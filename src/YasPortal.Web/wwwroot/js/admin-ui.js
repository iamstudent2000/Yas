(() => {
  // Small helper applied automatically to every page: drop-down lists with at least this many options get a search box.
  // (Long lists are paged by the page itself with the shared page bar, not by this script.)
  const SELECT_MIN_OPTIONS = 8;
  const enhanced = new WeakSet();
  // Parts of the page added since the last scan; only these are looked at again, not the whole page.
  const pending = new Set();
  let timer;

  // Add the look of the search box once (it is added by this script, not by a stylesheet file).
  function styles() {
    if (document.getElementById('ys-admin-ui-style')) return;
    const s = document.createElement('style');
    s.id = 'ys-admin-ui-style';
    s.textContent = `
      .ys-search-select-host{position:relative;width:100%;}
      .ys-search-select-host>select{position:absolute!important;opacity:0!important;width:1px!important;height:1px!important;pointer-events:none!important;}
      .ys-search-select-box{position:relative;}
      .ys-search-select-input{width:100%;min-height:42px;padding:8px 36px 8px 12px;border:1px solid var(--border,#d0d5dd);border-radius:9px;background:var(--surface,#fff);color:var(--text,inherit);font:inherit;outline:none;box-sizing:border-box;}
      .ys-search-select-input:focus{border-color:var(--accent,#2563eb);box-shadow:0 0 0 3px rgba(37,99,235,.10);}
      .ys-search-select-icon{position:absolute;right:12px;top:50%;transform:translateY(-50%);color:var(--text-muted,#667085);pointer-events:none;}
      .ys-search-select-menu{position:absolute;z-index:5000;right:0;left:0;top:calc(100% + 4px);max-height:260px;overflow:auto;padding:4px;border:1px solid var(--border,#e4e7ec);border-radius:10px;background:var(--surface,#fff);box-shadow:0 12px 30px rgba(16,24,40,.14);display:none;}
      .ys-search-select-menu.open{display:block;}
      .ys-search-select-option{display:block;width:100%;padding:8px 10px;border:0;border-radius:7px;background:transparent;text-align:right;font:inherit;cursor:pointer;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
      .ys-search-select-option:hover,.ys-search-select-option.selected{background:var(--accent-soft,#eef4ff);}
      .ys-search-select-empty{padding:18px;text-align:center;color:var(--text-muted,#667085);font-size:13px;}
    `;
    document.head.appendChild(s);
  }

  // Turn a long drop-down list into a box where the person types to find the option. The original list stays hidden behind it.
  // Lists marked data-searchable="false" and the text editor's own pickers are skipped.
  function searchableSelect(select) {
    if (enhanced.has(select) || select.disabled || select.multiple || select.dataset.searchable === 'false') return;
    // the rich-text editor (guides) builds its own colour / heading pickers from hidden selects; turning those into search boxes breaks them
    if (select.closest('.ql-toolbar, .guide-editor-host')) return;
    const options = [...select.options];
    if (options.length < SELECT_MIN_OPTIONS) return;
    const parent = select.parentElement;
    if (!parent) return;

    enhanced.add(select);
    const host = document.createElement('div');
    host.className = 'ys-search-select-host';
    select.parentNode.insertBefore(host, select);
    host.appendChild(select);

    const box = document.createElement('div');
    box.className = 'ys-search-select-box';
    const input = document.createElement('input');
    input.className = 'ys-search-select-input';
    input.type = 'text';
    input.autocomplete = 'off';
    input.placeholder = 'جستجو و انتخاب...';
    const icon = document.createElement('i');
    icon.className = 'ti ti-search ys-search-select-icon';
    const menu = document.createElement('div');
    menu.className = 'ys-search-select-menu';
    box.append(input, icon, menu);
    host.insertBefore(box, select);

    const sync = () => input.value = select.selectedOptions[0]?.textContent?.trim() || '';
    const close = () => menu.classList.remove('open');
    const render = () => {
      const q = input.value.trim().toLocaleLowerCase();
      menu.innerHTML = '';
      const matches = [...select.options].filter(o => !o.disabled && (!q || o.textContent.trim().toLocaleLowerCase().includes(q))).slice(0,100);
      if (!matches.length) { const e=document.createElement('div'); e.className='ys-search-select-empty'; e.textContent='موردی پیدا نشد'; menu.appendChild(e); return; }
      matches.forEach(o => { const b=document.createElement('button'); b.type='button'; b.className='ys-search-select-option'+(o.selected?' selected':''); b.textContent=o.textContent.trim(); b.onmousedown=e=>e.preventDefault(); b.onclick=()=>{select.value=o.value;select.dispatchEvent(new Event('change',{bubbles:true}));sync();close();}; menu.appendChild(b); });
    };
    input.onfocus=()=>{render();menu.classList.add('open');};
    input.oninput=()=>{render();menu.classList.add('open');};
    input.onkeydown=e=>{if(e.key==='Escape'){sync();close();}else if(e.key==='Enter'){const first=menu.querySelector('.ys-search-select-option');if(first){e.preventDefault();first.click();}}};
    select.addEventListener('change',sync);
    // (clicking outside is handled by one shared listener at the bottom of this file)
    sync();
  }

  // Add search boxes to the long drop-down lists under the given part of the page.
  function scan(root=document){
    styles();
    root.querySelectorAll?.('select').forEach(searchableSelect);
    if (root.matches?.('select')) searchableSelect(root);
  }
  // The portal redraws parts of the page often; after the page settles for a moment, scan only the new parts for drop-down lists to improve.
  function schedule(){
    clearTimeout(timer);
    timer=setTimeout(()=>{ const nodes=[...pending]; pending.clear(); nodes.forEach(n=>{ if(n.isConnected) scan(n); }); },100);
  }
  // One shared listener closes every open search-box list when the click happened outside of it.
  document.addEventListener('click',e=>{
    document.querySelectorAll('.ys-search-select-menu.open').forEach(m=>{ if(!m.closest('.ys-search-select-host')?.contains(e.target)) m.classList.remove('open'); });
  });
  window.addEventListener('load',()=>{
    scan();
    new MutationObserver(changes=>{
      changes.forEach(c=>c.addedNodes.forEach(n=>{ if(n.nodeType===1) pending.add(n); }));
      if(pending.size) schedule();
    }).observe(document.body,{childList:true,subtree:true});
  });
})();
