/* Independent extension for the launcher's local Qt WebEngine page.
 * No access to HGJsBridge, account state or React internals. Ready-state DOM adapter only. */
(function () {
  'use strict';
  if (document.getElementById('zml-panel') || !window.ZML_LAUNCHER) return;
  const cfg = window.ZML_LAUNCHER;
  // Monochrome glyph icons
  const gridIcon = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false" fill="currentColor" fill-rule="evenodd"><path d="M15.63 16.63 L11.76 14.34 L11.43 14.36 L7.36 16.69 L7.36 20.94 L7.72 21.09 L10.64 19.39 L10.66 16.3 L11.5 15.77 L11.54 18.98 L15.1 21.09 L15.41 21.07 L15.65 20.91 Z M2.0 14.52 L2.02 18.45 L2.77 18.91 L2.86 19.4 L6.06 21.33 L6.55 21.11 L6.51 16.82 L2.51 14.32 L2.35 14.28 Z M19.91 8.94 L19.76 8.96 L17.85 10.06 L17.71 10.19 L17.71 10.65 L17.76 10.72 L19.74 11.9 L19.87 11.88 L21.94 10.69 L21.98 10.61 L21.94 10.14 Z M6.66 6.01 L6.29 6.27 L6.29 10.61 L9.05 12.25 L9.14 13.04 L5.91 11.33 L2.92 13.02 L2.84 13.59 L6.75 16.01 L10.73 13.72 L10.75 8.5 Z M11.49 2.63 L7.19 4.96 L7.16 5.28 L11.52 7.97 L11.54 12.85 L16.33 15.73 L16.35 21.07 L16.88 21.35 L20.17 19.4 L20.22 18.94 L21.03 18.43 L21.05 14.39 L20.18 13.79 L20.11 13.02 L17.12 11.33 L13.87 13.11 L13.14 12.71 L16.72 10.65 L16.73 6.23 L15.89 5.66 L15.83 4.94 Z M20.07 14.91 L20.09 18.34 L16.95 20.1 L16.94 16.71 Z M15.71 6.71 L15.71 10.17 L12.53 11.94 L12.5 8.52 Z"/></svg>';
  const fullNames = ['ZMDModLoader', 'ZeroModLoader', 'ZMLModLoader'];
  function getRandomFullName() { return fullNames[Math.floor(Math.random() * fullNames.length)]; }
  const launcherButton = document.createElement('button');
  launcherButton.id = 'zml-toggle'; launcherButton.innerHTML = gridIcon;
  launcherButton.title = '模组管理'; launcherButton.type = 'button';
  launcherButton.setAttribute('aria-label', '模组管理');
  launcherButton.setAttribute('aria-expanded', 'false'); launcherButton.setAttribute('data-clickable', 'true');
  const launchOption = document.createElement('label'); launchOption.id = 'zml-launch-option';
  launchOption.setAttribute('data-clickable', 'true');
  launchOption.innerHTML = '<input type="checkbox" aria-label="加载模组"><span class="zml-check-mark"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 8l2.5 2.5L12 5"/></svg></span><span>加载模组</span>';
  const loadMods = launchOption.querySelector('input');
  // Launch preference state
  try { loadMods.checked = localStorage.getItem('zml.loadMods.v1') === 'true'; } catch (_) {}
  loadMods.addEventListener('change', () => { try { localStorage.setItem('zml.loadMods.v1', String(loadMods.checked)); } catch (_) {} });
  const launchStatus = document.createElement('div'); launchStatus.id = 'zml-launch-status';
  launchStatus.setAttribute('role', 'status'); launchStatus.setAttribute('data-clickable', 'true');
  const layer = document.createElement('div'); layer.id = 'zml-layer'; layer.hidden = true;
  const panel = document.createElement('section'); panel.id = 'zml-panel'; panel.hidden = true;
  panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-modal', 'true');
  panel.setAttribute('aria-labelledby', 'zml-title'); panel.setAttribute('data-clickable', 'true');
  panel.innerHTML = '<header><h2 id="zml-title">' + getRandomFullName() + '</h2><button class="zml-close" aria-label="关闭模组面板"><svg viewBox="0 0 16 16" aria-hidden="true"><path d="M12 4L4 12M4 4l8 8"/></svg></button></header>' +
    '<div class="zml-tools"><input type="search" placeholder="搜索名称、标签或作者" aria-label="搜索模组"><button data-action="get-mods" aria-label="获取模组">获取模组</button><button data-action="refresh" aria-label="刷新模组">刷新</button><button data-action="folder">模组文件夹</button></div>' +
    '<nav class="zml-filters" aria-label="模组筛选"><button data-filter="all">全部</button><button data-filter="enabled">启用</button><button data-filter="disabled">禁用</button></nav>' +
    '<div class="zml-list"></div><div class="zml-status" role="status"></div>';
  document.body.appendChild(launcherButton); layer.appendChild(panel); document.body.appendChild(layer);
  const list = panel.querySelector('.zml-list'), status = panel.querySelector('.zml-status'), search = panel.querySelector('input');
  let catalog = null, busy = false, lastRevision = '', filter = 'all', expandedId = null;
  let viewMode = 'installed', remoteIndex = null, remoteFilter = 'all';
  const downloadingIds = new Set();
  let host = null, forwardNativeClick = false;
  let lastOptionsTheme = null, optionsRequest = false, optionsRetry = 0, optionsRetryTheme = null, optionsDelay = 250;
  const readyLabels = new Set(['开始游戏', '启动游戏', '进入游戏', 'Start Game', 'Launch Game', 'Start']);
  function isEndfield() { return document.documentElement.classList.contains('theme_endfield'); }
  // Adapt launcher action capsule layout
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
    // Preserve responsive capsule width and add 52px button
    const nativeWidth = host.outer.style.width ||
      host.outer.style.getPropertyValue('--zml-native-width') || getComputedStyle(host.outer).width;
    if (/^\d+(?:\.\d+)?px$/.test(nativeWidth) && host.outer.style.getPropertyValue('--zml-native-width') !== nativeWidth) {
      host.outer.style.setProperty('--zml-native-width', nativeWidth);
    }
    if (!host.outer.classList.contains('zml-host-width')) host.outer.classList.add('zml-host-width');
    if (!host.pill.classList.contains('zml-host-pill')) host.pill.classList.add('zml-host-pill');
    if (host.dx && !host.dx.classList.contains('zml-host-dx')) host.dx.classList.add('zml-host-dx');
    // Shorten DX11 caption text
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
    if (lastOptionsTheme === value || optionsRequest) return;
    if (optionsRetry && optionsRetryTheme === value) return;
    if (optionsRetry) { clearTimeout(optionsRetry); optionsRetry = 0; }
    optionsRequest = true;
    try { await api('/options-theme', {endfield: value}); lastOptionsTheme = value; optionsDelay = 250; }
    catch (_) {
      // Sync launch options with server
      lastOptionsTheme = null;
    }
    finally {
      optionsRequest = false;
      if (lastOptionsTheme !== isEndfield()) {
        // Retry polling with backoff
        optionsRetryTheme = isEndfield();
        const delay = value !== optionsRetryTheme ? 0 : optionsDelay;
        optionsDelay = Math.min(optionsDelay * 2, 5000);
        optionsRetry = setTimeout(() => { optionsRetry = 0; syncOptionsTheme(); }, delay);
      }
    }
  }
  function node(tag, className, text) {
    const n = document.createElement(tag); if (className) n.className = className;
    if (text != null) n.textContent = text; return n;
  }
  function draw() {
    if (viewMode === 'installed') drawInstalled();
    else drawRemote();
  }
  function drawInstalled() {
    if (!catalog) return;
    const scroll = list.scrollTop; list.textContent = '';
    const q = search.value.trim().toLocaleLowerCase();
    const rows = catalog.mods.filter(m => [m.name, m.id, m.authors, m.description, ...(m.tags || [])].join(' ').toLocaleLowerCase().includes(q) &&
      (filter === 'all' || (filter === 'enabled' ? m.enabled : !m.enabled)));
    const filterNav = panel.querySelector('.zml-filters');
    filterNav.innerHTML = '<button data-filter="all">全部</button><button data-filter="enabled">启用</button><button data-filter="disabled">禁用</button>';
    filterNav.querySelectorAll('[data-filter]').forEach(button => {
      const kind = button.dataset.filter;
      const count = catalog.mods.filter(m => kind === 'all' || (kind === 'enabled' ? m.enabled : !m.enabled)).length;
      button.textContent = ({all: '全部', enabled: '启用', disabled: '禁用'})[kind] + ' (' + count + ')';
      button.setAttribute('aria-pressed', String(filter === kind));
    });
    if (!rows.length) list.appendChild(node('div', 'zml-empty', catalog.mods.length ? '没有匹配的模组。' : '尚未安装模组。点击“模组文件夹”，将模组放入其中，或点击“获取模组”在线安装。'));
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
  function drawRemote() {
    const scroll = list.scrollTop; list.textContent = '';
    const q = search.value.trim().toLocaleLowerCase();
    const installedMap = new Map((catalog ? catalog.mods : []).map(m => [m.id, m]));
    const remoteMods = (remoteIndex && remoteIndex.mods) || [];
    const filterNav = panel.querySelector('.zml-filters');
    filterNav.innerHTML = '<button data-filter="all">全部</button><button data-filter="uninstalled">未安装</button><button data-filter="installed">已安装</button>';
    const allCount = remoteMods.length;
    const installedCount = remoteMods.filter(m => installedMap.has(m.id)).length;
    const uninstalledCount = remoteMods.filter(m => !installedMap.has(m.id)).length;
    filterNav.querySelectorAll('[data-filter]').forEach(button => {
      const kind = button.dataset.filter;
      const count = kind === 'all' ? allCount : kind === 'installed' ? installedCount : uninstalledCount;
      button.textContent = ({all: '全部', uninstalled: '未安装', installed: '已安装'})[kind] + ' (' + count + ')';
      button.setAttribute('aria-pressed', String(remoteFilter === kind));
    });
    if (!remoteIndex) {
      list.appendChild(node('div', 'zml-empty', busy ? '正在获取模组索引...' : '未能获取模组列表，请点击“刷新”重试。'));
      return;
    }
    const rows = remoteMods.filter(m => {
      const isInst = installedMap.has(m.id);
      if (remoteFilter === 'installed' && !isInst) return false;
      if (remoteFilter === 'uninstalled' && isInst) return false;
      if (!q) return true;
      const hay = [m.name, m.display_title || '', m.id, m.authors || '', m.description || '', ...(m.tags || [])].join(' ').toLocaleLowerCase();
      return hay.includes(q);
    });
    if (!rows.length) {
      list.appendChild(node('div', 'zml-empty', '没有匹配的模组。'));
      return;
    }
    rows.forEach((mod, index) => {
      const row = node('article', 'zml-mod zml-mod-remote');
      const summary = node('button', 'zml-summary'); summary.type = 'button';
      summary.setAttribute('aria-label', '查看 ' + mod.name + ' 详情'); summary.setAttribute('aria-expanded', String(expandedId === mod.id));
      summary.setAttribute('aria-controls', 'zml-remote-detail-' + index);
      if (mod.icon && /^data:image\/png;base64,/.test(mod.icon)) {
        const image = node('img', 'zml-icon'); image.src = mod.icon; image.alt = ''; summary.appendChild(image);
      } else { const fallback = node('span', 'zml-icon zml-fallback'); fallback.innerHTML = gridIcon; summary.appendChild(fallback); }
      const info = node('span', 'zml-info');
      const title = node('span', 'zml-title');
      const modTitle = mod.name + (mod.display_title ? ' · ' + mod.display_title : '');
      title.appendChild(node('b', '', modTitle));
      title.appendChild(node('span', 'zml-version', mod.version ? 'v' + mod.version : '无版本号'));
      info.appendChild(title);
      const line = node('span', 'zml-description');
      line.appendChild(node('span', 'zml-tags-inline', (mod.tags || []).join(' · ') + ((mod.tags || []).length ? '  ' : '')));
      line.appendChild(node('span', '', mod.description || '此模组未提供简介。'));
      info.appendChild(line);
      summary.appendChild(info);
      row.appendChild(summary);

      const localMod = installedMap.get(mod.id);
      if (downloadingIds.has(mod.id)) {
        const btn = node('button', 'zml-btn-action', '安装中...');
        btn.disabled = true; row.appendChild(btn);
      } else if (localMod) {
        if (localMod.version === mod.version) {
          const badge = node('span', 'zml-badge-installed', '已安装');
          row.appendChild(badge);
        } else {
          const btn = node('button', 'zml-btn-action', '更新');
          btn.addEventListener('click', (e) => { e.stopPropagation(); installRemoteMod(mod); });
          row.appendChild(btn);
        }
      } else {
        const btn = node('button', 'zml-btn-action', '下载');
        btn.addEventListener('click', (e) => { e.stopPropagation(); installRemoteMod(mod); });
        row.appendChild(btn);
      }

      const detail = node('div', 'zml-detail'); detail.id = 'zml-remote-detail-' + index; detail.hidden = expandedId !== mod.id;
      detail.appendChild(node('p', '', mod.description || '此模组未提供简介。'));
      detail.appendChild(node('div', 'zml-author', '作者：' + (mod.authors || '未提供') + ' · ID：' + mod.id));
      const tags = node('div', 'zml-tags'); (mod.tags || []).forEach(t => tags.appendChild(node('span', '', t))); detail.appendChild(tags);
      if (mod.depends && mod.depends.length) detail.appendChild(node('div', 'zml-deps', '依赖：' + mod.depends.join(' · ')));
      if (mod.repo_url) {
        const link = node('a', 'zml-link', '查看仓库 ↗');
        link.href = mod.repo_url; link.target = '_blank'; link.rel = 'noopener noreferrer';
        detail.appendChild(link);
      }
      row.appendChild(detail);

      summary.addEventListener('click', () => {
        expandedId = expandedId === mod.id ? null : mod.id; draw();
        const target = [...list.querySelectorAll('.zml-summary')].find(n => n.getAttribute('aria-label') === '查看 ' + mod.name + ' 详情');
        if (target) target.focus();
      });
      list.appendChild(row);
    });
    list.scrollTop = scroll;
  }
  async function installRemoteMod(mod) {
    if (busy || downloadingIds.has(mod.id)) return;
    downloadingIds.add(mod.id);
    draw();
    say('正在下载并安装 ' + mod.name + '...');
    try {
      catalog = await api('/install-remote', { id: mod.id, asset_url: mod.asset_url, sha256: mod.sha256 });
      lastRevision = catalog.revision;
      say('已成功安装 ' + mod.name + '！');
    } catch (error) {
      say(error.message, true);
    } finally {
      downloadingIds.delete(mod.id);
      draw();
    }
  }
  async function refreshRemoteIndex(force) {
    if (busy) return;
    setBusy(true);
    say('正在加载模组索引...');
    try {
      remoteIndex = await api('/index');
      say('');
    } catch (error) {
      say(error.message, true);
    } finally {
      setBusy(false);
      draw();
    }
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

  const getModsBtn = panel.querySelector('[data-action="get-mods"]');
  const refreshBtn = panel.querySelector('[data-action="refresh"]');
  const folderBtn = panel.querySelector('[data-action="folder"]');

  getModsBtn.addEventListener('click', async () => {
    if (viewMode === 'installed') {
      viewMode = 'remote';
      getModsBtn.textContent = '已安装模组';
      getModsBtn.classList.add('zml-btn-active');
      search.placeholder = '搜索索引模组名称、标签或作者';
      search.value = '';
      expandedId = null;
      if (!remoteIndex) {
        await refreshRemoteIndex(true);
      } else {
        draw();
      }
    } else {
      viewMode = 'installed';
      getModsBtn.textContent = '获取模组';
      getModsBtn.classList.remove('zml-btn-active');
      search.placeholder = '搜索名称、标签或作者';
      search.value = '';
      expandedId = null;
      draw();
    }
  });

  refreshBtn.addEventListener('click', () => {
    if (viewMode === 'installed') refresh(true);
    else refreshRemoteIndex(true);
  });
  folderBtn.addEventListener('click', async () => { try { await api('/open-folder', {}); } catch (error) { say(error.message, true); } });
  search.addEventListener('input', draw);
  panel.querySelector('.zml-filters').addEventListener('click', (e) => {
    const button = e.target.closest('button[data-filter]');
    if (!button) return;
    if (viewMode === 'installed') filter = button.dataset.filter;
    else remoteFilter = button.dataset.filter;
    expandedId = null;
    draw();
  });
  async function launch() {
    if (busy) return; setBusy(true); say('');
    let ticket = null;
    try {
      const health = await api('/health');
      if (!health.nativeLaunch) throw new Error('桥接程序需要升级。请退出启动器，运行新版安装器的“安装 / 修复”；模组和配置会保留。');
      const prepared = await api('/prepare-launch', {}); ticket = prepared.ticket;
      // Trigger official launch action
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
  // Pass through non-mod launch actions
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
