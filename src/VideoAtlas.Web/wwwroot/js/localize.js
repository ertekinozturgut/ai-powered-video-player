const skip = new Set(['SCRIPT', 'STYLE', 'CODE', 'PRE']);
const normalize = value => value.replace(/\s+/g, ' ').trim();
const storageKey = 'VideoAtlas.Language';

const storedLanguage = () => {
  try { return localStorage.getItem(storageKey)?.trim().toLowerCase() || ''; }
  catch { return ''; }
};
const rememberLanguage = code => {
  try { localStorage.setItem(storageKey, code); }
  catch { /* Cookie and account preference remain available. */ }
};
const browserLanguage = supported => {
  const requested = [...(navigator.languages || []), navigator.language].filter(Boolean).map(x => x.toLowerCase());
  for (const candidate of requested) {
    const exact = supported.find(x => x.toLowerCase() === candidate);
    if (exact) return exact;
    const base = candidate.split('-')[0];
    const compatible = supported.find(x => x.toLowerCase().split('-')[0] === base);
    if (compatible) return compatible;
  }
  return supported.find(x => x.toLowerCase() === 'en') || supported[0] || 'en';
};
const persistOnServer = async code => {
  const token = document.querySelector('meta[name="csrf-token"]')?.content;
  if (!token) return false;
  const body = new URLSearchParams({code});
  const response = await fetch('/language/preference', {
    method: 'POST', cache: 'no-store', credentials: 'same-origin', body,
    headers: {'Content-Type': 'application/x-www-form-urlencoded;charset=UTF-8', 'X-CSRF-TOKEN': token}
  });
  return response.ok;
};

try {
  const response = await fetch('/api/localization', {cache: 'no-store', headers: {'Accept': 'application/json', 'Cache-Control': 'no-cache'}});
  if (response.ok) {
    const catalog = await response.json(), texts = catalog.texts ?? {};
    const supported = Array.isArray(catalog.supported) ? catalog.supported : [];
    const stored = storedLanguage();
    const desired = supported.includes(stored) ? stored : browserLanguage(supported);
    rememberLanguage(desired);
    if (desired !== catalog.language && await persistOnServer(desired)) {
      location.reload();
      throw new Error('language-reload');
    }
    document.documentElement.lang = catalog.language;
    const translate = value => texts[normalize(value)] || value;
    const visit = root => {
      if (root.nodeType === Node.TEXT_NODE) {
        if (skip.has(root.parentElement?.tagName)) return;
        const source = normalize(root.nodeValue || ''), translated = texts[source];
        if (source && translated && translated !== source) root.nodeValue = `${root.nodeValue.match(/^\s*/)?.[0] ?? ''}${translated}${root.nodeValue.match(/\s*$/)?.[0] ?? ''}`;
        return;
      }
      if (root.nodeType !== Node.ELEMENT_NODE || skip.has(root.tagName)) return;
      for (const name of ['placeholder', 'title', 'aria-label']) if (root.hasAttribute(name)) root.setAttribute(name, translate(root.getAttribute(name)));
      for (const child of root.childNodes) visit(child);
    };
    visit(document.body);
    const title = document.title.split(' · '); if (title.length) document.title = [translate(title[0]), ...title.slice(1)].join(' · ');
    new MutationObserver(records => { for (const record of records) for (const node of record.addedNodes) visit(node); })
      .observe(document.body, {childList: true, subtree: true});
    for (const select of document.querySelectorAll('[data-language-select]')) {
      select.value = catalog.language;
      select.addEventListener('change', () => {
        rememberLanguage(select.value);
        select.closest('[data-language-form]')?.requestSubmit();
      });
    }
  }
} catch { /* Server-rendered fallback remains usable. */ }
