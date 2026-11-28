(() => {
  // Small helpers applied automatically to every page. Drop-down lists with at least this many options get a search box,
  // and tables marked data-auto-paginate (long lists that have no page bar of their own) are split into pages of PAGE_SIZE rows.
  const SELECT_MIN_OPTIONS = 8;
  const PAGE_SIZE = 15;
  const enhanced = new WeakSet();
  const paged = new WeakSet();
  // Parts of the page added since the last scan; only these are looked at again, not the whole page.
  const pending = new Set();
  let timer;
  const LIST_SELECTOR = '[data-auto-paginate] tbody';

  // Add the look of the search box and the page buttons once (it is added by this script, not by a stylesheet file).
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
      .ys-pagination{display:flex;align-items:center;justify-content:space-between;gap:10px;margin-top:12px;padding:8px 2px;direction:rtl;}
      .ys-pagination-info{font-size:12px;color:var(--text-muted,#667085);}
      .ys-pagination-buttons{display:flex;align-items:center;gap:4px;}
      .ys-pagination button{min-width:32px;height:32px;padding:0 8px;border:1px solid var(--border,#d0d5dd);border-radius:7px;background:var(--surface,#fff);color:var(--text,#344054);cursor:pointer;font:inherit;font-size:12px;}
      .ys-pagination button:hover:not(:disabled),.ys-pagination button.active{background:var(--accent-soft,#eef4ff);border-color:#84adff;color:var(--accent,#155eef);}
      .ys-pagination button:disabled{opacity:.45;cursor:not-allowed;}
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

  // The rows of a table body that should be paged.
  function rows(container) {
    return [...container.children].filter(x=>x.tagName==='TR');
  }

  // Split a long list into pages and add the previous / next and page number buttons (only tables marked data-auto-paginate are paged).
  function paginate(container) {
    if (paged.has(container)) return;
    const initial=rows(container); if(initial.length<=PAGE_SIZE) return;
    paged.add(container);
    let page=1;
    const pager=document.createElement('div'); pager.className='ys-pagination';
    container.parentElement?.appendChild(pager);
    const render=()=>{
      const all=rows(container), pages=Math.max(1,Math.ceil(all.length/PAGE_SIZE)); page=Math.min(page,pages);
      const start=(page-1)*PAGE_SIZE;
      all.forEach((r,i)=>r.style.display=i>=start&&i<start+PAGE_SIZE?'':'none');
      pager.innerHTML='';
      const info=document.createElement('span'); info.className='ys-pagination-info'; info.textContent=`${start+1}–${Math.min(start+PAGE_SIZE,all.length)} از ${all.length}`;
      const controls=document.createElement('div'); controls.className='ys-pagination-buttons';
      const add=(text,disabled,fn,active=false)=>{const b=document.createElement('button');b.textContent=text;b.disabled=disabled;if(active)b.classList.add('active');b.onclick=fn;controls.appendChild(b);};
      add('قبلی',page===1,()=>{page--;render();});
      const max=7, first=Math.max(1,Math.min(page-3,pages-max+1)), last=Math.min(pages,first+max-1);
      for(let p=first;p<=last;p++)add(String(p),false,()=>{page=p;render();},p===page);
      add('بعدی',page===pages,()=>{page++;render();});
      pager.append(info,controls);
    };
    render();
  }

  // Apply both improvements (search boxes and paging) to everything under the given part of the page.
  function scan(root=document){
    styles();
    root.querySelectorAll?.('select').forEach(searchableSelect);
    root.querySelectorAll?.(LIST_SELECTOR).forEach(paginate);
    // a list that was already on the page may only now have received its rows
    root.parentElement?.closest?.(LIST_SELECTOR) && paginate(root.parentElement.closest(LIST_SELECTOR));
    if (root.matches?.('select')) searchableSelect(root);
    if (root.matches?.(LIST_SELECTOR)) paginate(root);
  }
  // The portal redraws parts of the page often; after the page settles for a moment, scan only the new parts for lists to improve.
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
      changes.forEach(c=>c.addedNodes.forEach(n=>{ if(n.nodeType===1 && !n.classList.contains('ys-pagination')) pending.add(n); }));
      if(pending.size) schedule();
    }).observe(document.body,{childList:true,subtree:true});
  });
})();
