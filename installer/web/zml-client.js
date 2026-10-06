/* Independent extension for the launcher's local Qt WebEngine page.
 * No access to HGJsBridge, account state or React internals. Ready-state DOM adapter only. */
(function () {
  'use strict';
  if (document.getElementById('zml-panel') || !window.ZML_LAUNCHER) return;
  const cfg = window.ZML_LAUNCHER;
  // Own monochrome glyphs; no launcher assets or React component implementation.
  const gridIcon = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><rect x="4" y="4" width="6" height="6" rx="1"/><rect x="14" y="4" width="6" height="6" rx="1"/><rect x="4" y="14" width="6" height="6" rx="1"/><rect x="14" y="14" width="6" height="6" rx="1"/></svg>';
  const launcherButton = document.createElement('button');
  launcherButton.id = 'zml-toggle'; launcherButton.innerHTML = gridIcon;
  launcherButton.title = '模组管理'; launcherButton.type = 'button';
  launcherButton.setAttribute('aria-label', '模组管理');
  launcherButton.setAttribute('aria-expanded', 'false'); launcherButton.setAttribute('data-clickable', 'true');
  const launchOption = document.createElement('label'); launchOption.id = 'zml-launch-option';
  launchOption.setAttribute('data-clickable', 'true');
  launchOption.innerHTML = '<input type="checkbox" aria-label="加载模组"><span class="zml-check-mark"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 8l2.5 2.5L12 5"/></svg></span><span>加载模组</span>';
  const loadMods = launchOption.querySelector('input');
  // Only our preference, never host/account storage. Default off preserves ordinary launch.
  try { loadMods.checked = localStorage.getItem('zml.loadMods.v1') === 'true'; } catch (_) {}
  loadMods.addEventListener('change', () => { try { localStorage.setItem('zml.loadMods.v1', String(loadMods.checked)); } catch (_) {} });
  const launchStatus = document.createElement('div'); launchStatus.id = 'zml-launch-status';
  launchStatus.setAttribute('role', 'status'); launchStatus.setAttribute('data-clickable', 'true');
  const layer = document.createElement('div'); layer.id = 'zml-layer'; layer.hidden = true;
  const panel = document.createElement('section'); panel.id = 'zml-panel'; panel.hidden = true;
  panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true');
  panel.setAttribute('aria-labelledby', 'zml-title'); panel.setAttribute('data-clickable', 'true');
  panel.innerHTML = '<header><div><h2 id="zml-title">模组管理</h2><p>终末地<span class="zml-heading-dot">·</span>管理已安装的模组</p></div><button class="zml-close" aria-label="关闭模组面板"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M12 4L4 12M4 4l8 8"/></svg></button></header>' +
    '<div class="zml-tools"><input type="search" placeholder="搜索名称、标签或作者" aria-label="搜索模组"><button data-action="refresh" aria-label="刷新模组">刷新</button><button data-action="folder">模组文件夹</button></div>' +
    '<nav class="zml-filters" aria-label="模组筛选"><button data-filter="all">全部</button><button data-filter="enabled">启用</button><button data-filter="disabled">禁用</button></nav>' +
    '<div class="zml-list"></div><div class="zml-status" role="status"></div>' +
    '<footer><small>模组开关在下次启动时生效。<br>勾选主按钮下方“加载模组”，直接点开始游戏。<br>第三方模组可能带来兼容及账号风险。</small></footer>';
  document.body.appendChild(launcherButton); layer.appendChild(panel); document.body.appendChild(layer);
  const list = panel.querySelector('.zml-list'), status = panel.querySelector('.zml-status'), search = panel.querySelector('input');
  let catalog = null, busy = false, lastRevision = '', filter = 'all', expandedId = null;
  let host = null, forwardNativeClick = false;
  let lastOptionsTheme = null, optionsRequest = false, optionsAttempt = 0;
  const readyLabels = new Set(['开始游戏', '启动游戏', '进入游戏', 'Start Game', 'Launch Game', 'Start']);
  function isEndfield() { return document.documentElement.classList.contains('theme_endfield'); }
  // Observed native Fv layout: 60px capsule, direct main action + 52px game-options trigger.
  // No fixed coordinates, account reads, React fields or unbounded click interception.
  function findHost() {
    if (!isEndfield()) return null;
    const capsules = [...document.querySelectorAll('#root [data-clickable]')].filter(n =>
      n.classList.contains('rounded-[100px]') && n.classList.contains('justify-between') && n.classList.contains('h-full'));
    const matches = capsules.map(pill => {
      const main = [...pill.children].find(n => n.classList.contains('flex-1') && n.classList.contains('h-full'));
      const text = main && main.querySelector('.clamp-text-2');
      const settings = [...pill.children].find(n => n.querySelector('[class~="w-[52px]"]'));
      const dx = [...pill.children].find(n => n.classList.contains('absolute') && n.querySelector('[class~="w-4"][class~="h-4"]'));
      return main && text && settings ? { pill, main, text, settings, dx, outer: pill.parentElement } : null;
    }).filter(Boolean);
    return matches.length === 1 ? matches[0] : null;
  }
  function mountEntry() {
    syncOptionsTheme();
    const next = findHost();
    if (host && (!next || host.pill !== next.pill)) {
      host.outer.classList.remove('zml-host-width'); host.outer.style.removeProperty('--zml-native-width'); host.pill.classList.remove('zml-host-pill');
      if (host.dx) host.dx.classList.remove('zml-host-dx');
    }
    host = next;
    launcherButton.hidden = !host; launchOption.hidden = !host; launchStatus.hidden = !host;
    if (!host) {
      if (!launcherButton.isConnected) document.body.appendChild(launcherButton);
      if (!panel.hidden) closePanel(false);
      return;
    }
    // Native Fv keeps its responsive width in the outer inline style. Preserve it
    // and add exactly one 52px button; never measure our expanded width as baseline.
    const nativeWidth = host.outer.style.width ||
      host.outer.style.getPropertyValue('--zml-native-width') || getComputedStyle(host.outer).width;
    if (/^\d+(?:\.\d+)?px$/.test(nativeWidth) && host.outer.style.getPropertyValue('--zml-native-width') !== nativeWidth) {
      host.outer.style.setProperty('--zml-native-width', nativeWidth);
    }
    if (!host.outer.classList.contains('zml-host-width')) host.outer.classList.add('zml-host-width');
    if (!host.pill.classList.contains('zml-host-pill')) host.pill.classList.add('zml-host-pill');
    if (host.dx && !host.dx.classList.contains('zml-host-dx')) host.dx.classList.add('zml-host-dx');
    // Shorten only the rendered DX11 caption, not its checkbox or host handler.
    if (host.dx) {
      const caption = [...host.dx.querySelectorAll('span')].find(n => /^DirectX\s*11/.test(n.textContent.trim()));
      if (caption) {
        const short = caption.textContent.replace(/\s*[（(][^）)]*[）)]\s*$/, '').trim();
        if (caption.textContent !== short) caption.textContent = short;
      }
    }
    if (launcherButton.parentNode !== host.pill) host.pill.insertBefore(launcherButton, host.settings);
    if (launchOption.parentNode !== host.pill) host.pill.appendChild(launchOption);
    if (launchStatus.parentNode !== host.pill) host.pill.appendChild(launchStatus);
    const top = host.dx ? host.dx.style.top : 'calc(100% + 10px)';
    if (launchOption.style.top !== top) launchOption.style.top = top;
    loadMods.disabled = busy;
  }
  mountEntry();
  const hostObserver = new MutationObserver(mountEntry);
  hostObserver.observe(document.getElementById('root') || document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class', 'style'] });
  new MutationObserver(mountEntry).observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  function say(message, error) {
    status.textContent = error ? message : ''; status.classList.toggle('zml-error', !!error);
    launchStatus.textContent = error ? message : ''; launchStatus.classList.toggle('zml-error', !!error);
  }
  async function api(path, data) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), path === '/prepare-launch' ? 90000 : 40000);
    try {
      const response = await fetch(cfg.endpoint + path, {
        method: data ? 'POST' : 'GET', cache: 'no-store', credentials: 'omit', signal: controller.signal,
        headers: { 'X-ZML-Token': cfg.token, ...(data ? { 'Content-Type': 'application/json' } : {}) },
        ...(data ? { body: JSON.stringify(data) } : {})
      });
      const body = await response.json();
      if (!response.ok) throw new Error(body.error || '操作失败');
      return body;
    } catch (error) {
      if (error instanceof TypeError) throw new Error('无法连接模组服务。请退出启动器，然后从安装目录的 Launcher.exe 重新打开；仍无效请运行安装器修复。');
      if (error.name === 'AbortError') throw new Error('操作超时，请刷新后确认结果，不要重复启动游戏。');
      throw error;
    } finally { clearTimeout(timeout); }
  }
  async function syncOptionsTheme() {
    const value = isEndfield();
    if (lastOptionsTheme === value || optionsRequest || Date.now() - optionsAttempt < 3000) return;
    optionsAttempt = Date.now();
    optionsRequest = true;
    try { await api('/options-theme', {endfield: value}); lastOptionsTheme = value; }
    catch (_) { /* Optional native extension failure must not break ordinary launch. */ }
    finally { optionsRequest = false; }
  }
  function node(tag, className, text) {
    const n = document.createElement(tag); if (className) n.className = className;
    if (text != null) n.textContent = text; return n;
  }
  function draw() {
    if (!catalog) return;
    const scroll = list.scrollTop; list.textContent = '';
    const q = search.value.toLocaleLowerCase();
    const rows = catalog.mods.filter(m => [m.name, m.id, m.authors, m.description, ...(m.tags || [])].join(' ').toLocaleLowerCase().includes(q) &&
      (filter === 'all' || (filter === 'enabled' ? m.enabled : !m.enabled)));
    panel.querySelectorAll('[data-filter]').forEach(button => {
      const kind = button.dataset.filter;
      const count = catalog.mods.filter(m => kind === 'all' || (kind === 'enabled' ? m.enabled : !m.enabled)).length;
      button.textContent = ({all: '全部', enabled: '启用', disabled: '禁用'})[kind] + ' (' + count + ')';
      button.setAttribute('aria-pressed', String(filter === kind));
    });
    if (!rows.length) list.appendChild(node('div', 'zml-empty', catalog.mods.length ? '没有匹配的模组。' : '尚未安装模组。点击“模组文件夹”，将模组目录放入其中，然后刷新。'));
    rows.forEach((mod, index) => {
      const row = node('article', 'zml-mod'); row.classList.toggle('zml-disabled', !mod.enabled);
      const summary = node('button', 'zml-summary'); summary.type = 'button';
      summary.setAttribute('aria-label', '查看 ' + mod.name + ' 详情'); summary.setAttribute('aria-expanded', String(expandedId === mod.id));
      summary.setAttribute('aria-controls', 'zml-detail-' + index);
      if (mod.icon && /^data:image\/png;base64,/.test(mod.icon)) {
        const image = node('img', 'zml-icon'); image.src = mod.icon; image.alt = ''; summary.appendChild(image);
      } else { const fallback = node('span', 'zml-icon zml-fallback'); fallback.innerHTML = gridIcon; summary.appendChild(fallback); }
      const info = node('span', 'zml-info');
      const title = node('span', 'zml-title'); title.appendChild(node('b', '', mod.name)); title.appendChild(node('span', 'zml-version', mod.version ? 'v' + mod.version : '无版本号')); info.appendChild(title);
      const line = node('span', 'zml-description');
      line.appendChild(node('span', 'zml-tags-inline', (mod.tags || []).join(' · ') + ((mod.tags || []).length ? '  ' : '')));
      line.appendChild(node('span', '', mod.error || mod.description || '此模组未提供简介。')); info.appendChild(line);
      summary.appendChild(info); row.appendChild(summary);
      const label = node('label', 'zml-switch');
      const toggle = node('input'); toggle.type = 'checkbox'; toggle.checked = mod.enabled; toggle.disabled = !!mod.error || busy;
      toggle.setAttribute('aria-label', '启用 ' + mod.name); label.appendChild(toggle);
      const mark = node('span', 'zml-check-mark'); mark.innerHTML = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 8l2.5 2.5L12 5"/></svg>'; label.appendChild(mark); row.appendChild(label);
      const detail = node('div', 'zml-detail'); detail.id = 'zml-detail-' + index; detail.hidden = expandedId !== mod.id;
      detail.appendChild(node('p', '', mod.description || '此模组未提供简介。'));
      detail.appendChild(node('div', 'zml-author', '作者：' + (mod.authors || '未提供') + ' · ID：' + mod.id));
      const tags = node('div', 'zml-tags'); (mod.tags || []).forEach(t => tags.appendChild(node('span', '', t))); detail.appendChild(tags);
      if (mod.depends && mod.depends.length) detail.appendChild(node('div', 'zml-deps', '依赖：' + mod.depends.join(' · ')));
      if (mod.error) detail.appendChild(node('div', 'zml-error', mod.error));
      row.appendChild(detail);
      summary.addEventListener('click', () => { expandedId = expandedId === mod.id ? null : mod.id; draw(); const target = [...list.querySelectorAll('.zml-summary')].find(n => n.getAttribute('aria-label') === '查看 ' + mod.name + ' 详情'); if (target) target.focus(); });
      toggle.addEventListener('change', () => change(mod, toggle.checked)); list.appendChild(row);
    });
    list.scrollTop = scroll;
  }
  async function refresh(force) {
    if (busy) return;
    try {
      const next = await api('/mods'); catalog = next;
      if (force || next.revision !== lastRevision) { lastRevision = next.revision; draw(); }
      if (force) say('');
    } catch (error) { say(error.message, true); }
  }
  function setBusy(value) {
    busy = value; panel.setAttribute('aria-busy', String(value));
    loadMods.disabled = value;
    panel.querySelectorAll('button,input').forEach(n => { if (!n.classList.contains('zml-close')) n.disabled = value; });
  }
  async function change(mod, enabled) {
    if (busy || !catalog) return;
    setBusy(true);
    try {
      const preview = await api('/toggle', { id: mod.id, enabled, revision: catalog.revision, apply: false });
      const other = preview.changes.filter(m => m.id !== mod.id);
      if (other.length && !window.confirm((enabled ? '同时启用必要依赖：\n' : '同时停用依赖此模组的模组：\n') + other.map(m => m.name).join('\n') + '\n\n继续？')) return;
      catalog = await api('/toggle', { id: mod.id, enabled, revision: preview.revision, apply: true });
      lastRevision = catalog.revision; say('');
    } catch (error) { say(error.message, true); }
    finally { setBusy(false); draw(); }
  }
  function closePanel(restoreFocus = true) { panel.hidden = true; layer.hidden = true; launcherButton.setAttribute('aria-expanded', 'false'); if (restoreFocus && !launcherButton.hidden) launcherButton.focus(); }
  launcherButton.addEventListener('click', () => {
    if (!panel.hidden) { closePanel(); return; }
    panel.hidden = false; layer.hidden = false; launcherButton.setAttribute('aria-expanded', 'true');
    refresh(true); search.focus();
  });
  panel.querySelector('.zml-close').addEventListener('click', () => closePanel());
  layer.addEventListener('click', e => { if (e.target === layer) closePanel(); });
  panel.querySelector('[data-action="refresh"]').addEventListener('click', () => refresh(true));
  panel.querySelector('[data-action="folder"]').addEventListener('click', async () => { try { await api('/open-folder', {}); } catch (error) { say(error.message, true); } });
  search.addEventListener('input', draw);
  panel.querySelectorAll('[data-filter]').forEach(button => button.addEventListener('click', () => { filter = button.dataset.filter; expandedId = null; draw(); }));
  async function launch() {
    if (busy) return; setBusy(true); say('');
    let ticket = null;
    try {
      const health = await api('/health');
      if (!health.nativeLaunch) throw new Error('桥接程序需要升级。请退出启动器，运行新版安装器的“安装 / 修复”；模组和配置会保留。');
      const prepared = await api('/prepare-launch', {}); ticket = prepared.ticket;
      // Replay only the original main action. The official handler still owns login,
      // update checks, launch arguments, game state, fade/minimize and process tracking.
      mountEntry();
      if (!host || !isEndfield() || !readyLabels.has(host.text.textContent.trim()) || host.main.classList.contains('cursor-not-allowed')) {
        await api('/cancel-launch', { ticket }); throw new Error('启动器状态已改变，请重新确认。');
      }
      forwardNativeClick = true;
      try { host.main.click(); } finally { forwardNativeClick = false; }
      for (;;) {
        await new Promise(resolve => setTimeout(resolve, 700));
        const result = await api('/launch-status', { ticket });
        if (result.state === 'loaded') break;
        if (result.state === 'cancelled') throw new Error('启动请求已取消。');
      }
      say('');
    } catch (error) {
      if (ticket) { try { await api('/cancel-launch', { ticket }); } catch (_) {} }
      say(error.message, true);
    } finally { setBusy(false); draw(); }
  }
  // Only the ready Endfield main action. Unchecked, non-ready and all other games
  // pass through unchanged, including download/update/login/options clicks.
  document.addEventListener('click', e => {
    if (forwardNativeClick || !loadMods.checked || !host || !isEndfield() || !host.main.contains(e.target)) return;
    if (!readyLabels.has(host.text.textContent.trim()) || host.main.classList.contains('cursor-not-allowed')) return;
    e.preventDefault(); e.stopImmediatePropagation(); launch();
  }, true);
  panel.addEventListener('keydown', e => {
    if (e.key === 'Escape') { closePanel(); e.stopPropagation(); return; }
    if (e.key === 'Tab') {
      const controls = [...panel.querySelectorAll('button,input')].filter(n => !n.disabled && n.getClientRects().length);
      const first = controls[0], last = controls[controls.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }
  });
  setInterval(() => { if (!panel.hidden && !busy) refresh(false); }, 4000);
  setInterval(syncOptionsTheme, 5000);
})();
