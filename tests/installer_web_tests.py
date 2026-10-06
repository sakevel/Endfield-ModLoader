"""Owned Chromium file-origin fixture against the real local bridge; not Qt game UI proof."""
import argparse
import atexit
import json
import queue
import subprocess
import threading
from pathlib import Path
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument("fixture", type=Path, nargs="?")
parser.add_argument("--start", nargs=3, metavar=("TEST_EXE", "PAYLOAD_ZIP", "NATIVE_FIXTURE"))
args = parser.parse_args()
if args.start:
    exe, payload, native = (str(Path(s).resolve()) for s in args.start)
    process = subprocess.Popen([exe, "--serve", payload, native], stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, encoding="utf-8",
                               creationflags=subprocess.CREATE_NO_WINDOW)
    def stop_owned():
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)
    atexit.register(stop_owned)
    result = queue.Queue()
    threading.Thread(target=lambda: result.put(process.stdout.readline()), daemon=True).start()
    line = result.get(timeout=45)
    if not line.startswith("FIXTURE "):
        raise RuntimeError("Owned bridge fixture failed: " + line)
    args.fixture = Path(line.strip()[8:])
if args.fixture is None:
    parser.error("Specify a fixture JSON or --start")
info = json.loads(args.fixture.read_text(encoding="utf-8"))
root = Path(info["root"])
passed = []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    # Separate transport-mocked startup fixture. The production script and DOM
    # observers run unchanged; no Qt/game window or real account is touched.
    startup = browser.new_page()
    startup.add_init_script("""(() => {
      const realFetch = window.fetch;
      window.optionsFixture = {calls: [], active: 0, maxActive: 0, mode: 'startup'};
      window.fetch = async (url, args) => {
        if (!String(url).endsWith('/options-theme')) return realFetch(url, args);
        const f = window.optionsFixture;
        f.calls.push({value: JSON.parse(args.body).endfield, time: performance.now()});
        f.active++; f.maxActive = Math.max(f.maxActive, f.active);
        try {
          if (f.mode === 'hold' || f.mode === 'holdFail') {
            const after = f.mode === 'holdFail' ? 'failOnce' : 'ok';
            await new Promise(resolve => { f.release = () => {f.mode = after; resolve();}; });
          }
          const failed = f.mode === 'fail' || f.mode === 'failOnce' || (f.mode === 'startup' && f.calls.length === 1);
          if (f.mode === 'failOnce') f.mode = 'ok';
          return new Response(JSON.stringify(failed ? {error: 'fixture not ready'} : {ok: true}), {status: failed ? 400 : 200});
        } finally { f.active--; }
      };
    })();""")
    startup.goto(info['url'])
    startup.wait_for_function('optionsFixture.calls.length === 2', timeout=2000)
    calls = startup.evaluate('optionsFixture.calls')
    assert [v['value'] for v in calls] == [False, False]
    assert 200 <= calls[1]['time'] - calls[0]['time'] < 1800
    passed.append('startup not-ready failure retried near 250ms, not first 5-second poll (mock transport)')
    startup.evaluate("optionsFixture.mode='hold'; document.documentElement.className='theme_endfield'")
    startup.wait_for_function("typeof optionsFixture.release === 'function'")
    startup.evaluate("document.documentElement.className='theme_arknights'; optionsFixture.release()")
    startup.wait_for_function('optionsFixture.calls.length === 4', timeout=1000)
    assert startup.evaluate('optionsFixture.calls.map(c=>c.value)') == [False, False, True, False]
    assert startup.evaluate('optionsFixture.maxActive') == 1
    passed.append('theme switched during pending response immediately corrected; requests serialized (mock transport)')
    startup.evaluate("optionsFixture.mode='fail'; document.documentElement.className='theme_endfield'")
    startup.wait_for_function('optionsFixture.calls.length === 5')
    startup.evaluate("""() => {
      for(let i=0;i<40;i++) {const n=document.createElement('div');document.getElementById('root').appendChild(n);n.remove();}
    }""")
    startup.wait_for_function('optionsFixture.calls.length === 6', timeout=1500)
    calls = startup.evaluate('optionsFixture.calls')
    assert calls[5]['time'] - calls[4]['time'] >= 200
    startup.evaluate("optionsFixture.mode='ok'")
    startup.wait_for_function('optionsFixture.calls.length === 7', timeout=1500)
    assert startup.evaluate('optionsFixture.maxActive') == 1
    assert startup.locator('#zml-toggle').count() == 1
    startup.evaluate("delete optionsFixture.release; optionsFixture.mode='holdFail'; document.documentElement.className='theme_arknights'")
    startup.wait_for_function("typeof optionsFixture.release === 'function'")
    startup.evaluate("document.documentElement.className='theme_endfield'; optionsFixture.release()")
    startup.wait_for_function('optionsFixture.calls.length === 9', timeout=1000)
    assert startup.evaluate('optionsFixture.calls.slice(-2).map(c=>c.value)') == [False, True]
    startup.close()
    passed.append('DOM churn preserves backoff; failed in-flight toggle invalidates stale theme cache (mock transport)')
    page = browser.new_page(viewport={"width": 1280, "height": 800})
    errors = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    page.goto(info["url"])
    assert page.locator("#root").inner_text() == "fixture"
    # Own structural scaffold matching observed native Fv; no copied launcher source.
    fixture_setup = """() => {
      document.documentElement.classList.add('theme_endfield');
      document.body.style.cssText = 'font-family:"Microsoft YaHei",sans-serif;background:#302f33';
      const style = document.createElement('style'); style.textContent = `
        .fixture-host {position:fixed;right:32px;bottom:65px;width:220px;height:60px}
        .fixture-pill {display:flex;align-items:center;justify-content:space-between;height:100%;border-radius:100px;background:#fff}
        .fixture-main {display:flex;align-items:center;justify-content:center;flex:1;height:100%;cursor:pointer;font-size:16px;font-weight:bold}
        .fixture-options {width:52px;height:52px;border-radius:50%;background:#18171ac7;color:#fff;display:flex;align-items:center;justify-content:center;margin-right:4px;cursor:pointer}
        .fixture-dx {position:absolute;left:0;right:0;color:white;font-size:12px;display:flex;justify-content:center}
        .fixture-dx-mark {width:16px;height:16px;background:#ffffff40;border-radius:4px;margin-right:4px}
        .fixture-dx .opacity-0 {opacity:0} .fixture-dx .opacity-100 {opacity:1}
      `; document.head.appendChild(style);
      const outer = document.createElement('div'); outer.className = 'fixture-host';
      outer.innerHTML = '<div data-clickable="true" class="fixture-pill w-full h-full flex justify-between rounded-[100px]">' +
        '<div class="fixture-main h-full flex-1"><div class="clamp-text-2">开始游戏</div></div>' +
        '<div class="fixture-dx absolute" style="top:calc(100% + 10px)"><div data-clickable="true" class="fixture-dx-mark w-4 h-4"><div class="opacity-0">✓</div></div><span>DirectX 11 启动（游戏异常时使用）</span></div>' +
        '<div><div data-clickable="true" class="fixture-options w-[52px]" aria-label="fixture options">☰</div></div></div>';
      outer.querySelector('.fixture-main').onclick = () => window.fixtureStartClicks = (window.fixtureStartClicks || 0) + 1;
      outer.querySelector('.fixture-options').onclick = () => window.fixtureOptionsClicks = (window.fixtureOptionsClicks || 0) + 1;
      outer.querySelector('.fixture-dx').onclick = () => { const tick=outer.querySelector('.fixture-dx-mark div'); tick.className=tick.className==='opacity-0'?'opacity-100':'opacity-0'; };
      document.getElementById('root').appendChild(outer);
    }"""
    page.evaluate(fixture_setup)
    entry = page.get_by_role('button', name='模组管理', exact=True)
    page.wait_for_function("document.querySelector('.fixture-pill #zml-toggle') !== null")
    assert page.locator('.fixture-host').bounding_box()['width'] == 220 + 52
    # Follow native width changes, without adding the extension repeatedly.
    page.locator('.fixture-host').evaluate("n=>n.style.width='240px'")
    page.wait_for_function("document.querySelector('.fixture-host').getBoundingClientRect().width===292")
    page.locator('.fixture-host').evaluate("n=>n.style.width='220px'")
    page.wait_for_function("document.querySelector('.fixture-host').getBoundingClientRect().width===272")
    assert page.locator('.fixture-dx').bounding_box()['x'] + page.locator('.fixture-dx').bounding_box()['width'] < page.locator('#zml-launch-option').bounding_box()['x']
    assert page.locator('.fixture-pill #zml-toggle').count() == 1
    load = page.get_by_role('checkbox', name='加载模组', exact=True)
    assert not load.is_checked()
    assert page.locator('.fixture-dx span').inner_text() == 'DirectX 11 启动'
    assert page.locator('#zml-launch-status').inner_text() == ''
    requests = []
    page.on('request', lambda r: requests.append(json.loads(r.post_data)) if r.url.endswith('/prepare-launch') else None)
    page.locator('.fixture-main').click()
    assert page.evaluate('window.fixtureStartClicks') == 1 and not requests
    load.check()
    page.locator('.fixture-options').click()
    assert page.evaluate('window.fixtureOptionsClicks') == 1 and not requests
    page.locator('.clamp-text-2').evaluate("n=>n.textContent='下载游戏'")
    page.locator('.fixture-main').click()
    assert page.evaluate('window.fixtureStartClicks') == 2 and not requests
    page.locator('.clamp-text-2').evaluate("n=>n.textContent='开始游戏'")
    page.locator('.fixture-main').evaluate("n=>n.classList.add('cursor-not-allowed')")
    page.locator('.fixture-main').click()
    assert page.evaluate('window.fixtureStartClicks') == 3 and not requests
    page.locator('.fixture-main').evaluate("n=>n.classList.remove('cursor-not-allowed')")
    page.locator('html').evaluate("n=>n.className='theme_arknights'")
    page.wait_for_function("document.querySelector('#zml-toggle').hidden")
    assert not page.locator('.fixture-host').evaluate("n=>n.classList.contains('zml-host-width')")
    assert page.locator('.fixture-host').bounding_box()['width']==220
    page.locator('.fixture-main').click()
    assert page.evaluate('window.fixtureStartClicks') == 4 and not requests
    page.locator('html').evaluate("n=>n.className='theme_endfield'")
    page.wait_for_function("!document.querySelector('#zml-toggle').hidden")
    passed.append('wider native capsule and standalone persistent opt-in; ordinary/non-ready/disabled/other-game/options actions unchanged')
    page.screenshot(path=str(args.fixture.parent / 'launch-capsule.png'))
    entry.click()
    page.locator('.zml-mod').first.wait_for()
    assert page.locator('.zml-mod').count() == 2
    assert 'v1.2.3' in page.locator('.zml-mod').first.inner_text()
    assert page.locator('.zml-detail').first.is_hidden()
    assert page.locator('.zml-info script').count() == 0
    assert '<script>not executable</script>' in page.locator('.zml-description').first.inner_text()
    assert page.locator('.zml-description').first.evaluate("n=>getComputedStyle(n).textOverflow") == 'ellipsis'
    assert page.locator('.zml-summary').first.bounding_box()['height'] == 58
    page.get_by_role('button', name='查看 测试 core 详情').click()
    assert page.locator('.zml-detail').first.is_visible()
    assert '<script>not executable</script>' in page.locator('.zml-detail p').first.inner_text()
    page.get_by_role('button', name='查看 测试 core 详情').click()
    assert page.locator('.zml-detail').first.is_hidden()
    passed.append('compact 58px rows, full version/tags, ellipsis description and accessible XSS-safe expanded detail')
    assert page.locator('#zml-panel').evaluate("n=>getComputedStyle(n).backgroundColor") == 'rgba(24, 23, 26, 0.95)'
    assert page.locator('#zml-panel').evaluate("n=>getComputedStyle(n).borderRadius") == '16px'
    assert page.locator('#zml-panel').evaluate("n=>getComputedStyle(n).fontFamily === getComputedStyle(document.body).fontFamily")
    page.wait_for_timeout(250)
    box = page.locator('#zml-panel').bounding_box()
    assert box['x'] > 600 and box['x']+box['width'] == 1268
    page.screenshot(path=str(args.fixture.parent / 'mod-sidebar.png'))
    passed.append('right-edge sliding sidebar with launcher-native white/gray glass/radius/font tokens')
    search = page.get_by_role('searchbox', name='搜索模组')
    search.fill('sample'); assert page.locator('.zml-mod').count() == 1
    search.fill('fixture'); assert page.locator('.zml-mod').count() == 2
    search.fill('nothing'); assert page.locator('.zml-empty').is_visible()
    search.fill('')
    passed.append('search name/author/description/empty results')
    dialogs = []
    def accept(dialog):
        dialogs.append(dialog.message); dialog.accept()
    page.on('dialog', accept)
    page.get_by_role('checkbox', name='启用 测试 core').uncheck()
    page.wait_for_function("[...document.querySelectorAll('.zml-switch input')].every(x=>!x.checked && !x.disabled)")
    assert len(dialogs) == 1 and 'sample' in dialogs[-1]
    assert 'enabled=false' in (root / 'ZML/mods/core/mod.ini').read_text(encoding='utf-8')
    assert 'enabled=false' in (root / 'ZML/mods/sample/mod.ini').read_text(encoding='utf-8')
    page.locator('[data-filter="enabled"]').click(); assert page.locator('.zml-mod').count() == 0
    page.locator('[data-filter="disabled"]').click(); assert page.locator('.zml-mod').count() == 2
    assert page.locator('[data-filter="disabled"]').inner_text() == '禁用 (2)'
    page.locator('[data-filter="all"]').click()
    page.get_by_role('checkbox', name='启用 测试 sample').check()
    page.wait_for_function("[...document.querySelectorAll('.zml-switch input')].every(x=>x.checked && !x.disabled)")
    assert len(dialogs) == 2 and 'core' in dialogs[-1]
    assert page.locator('.zml-status').inner_text() == ''
    assert page.locator('#zml-launch-status').inner_text() == ''
    passed.append('actual checkbox save, dependency confirmation/cascade, enabled/disabled tabs and counts')
    page.set_viewport_size({'width':800,'height':600})
    box=page.locator('#zml-panel').bounding_box()
    assert box['x']>=0 and box['y']>=0 and box['x']+box['width']<=800 and box['y']+box['height']<=600
    page.screenshot(path=str(args.fixture.parent / 'mod-sidebar-800x600.png'))
    passed.append('responsive sidebar bounds and scroll viewport')
    page.get_by_role('button', name='关闭模组面板').focus(); page.keyboard.press('Shift+Tab')
    assert page.locator('.zml-switch input').last.evaluate('n=>n===document.activeElement')
    page.keyboard.press('Escape'); assert page.locator('#zml-layer').is_hidden()
    assert entry.evaluate('n=>n===document.activeElement')
    entry.click(); page.mouse.click(10,300); assert page.locator('#zml-layer').is_hidden()
    # Adapter remount after host re-render, unique controls, mode preserved.
    page.evaluate("""() => {
      const old=document.querySelector('.fixture-host'); const copy=old.cloneNode(true);
      copy.querySelectorAll('[id^="zml-"]').forEach(n=>n.remove()); old.replaceWith(copy);
      copy.querySelector('.fixture-main').onclick=()=>window.fixtureStartClicks++;
    }""")
    page.wait_for_function("document.querySelector('.fixture-pill #zml-toggle') !== null")
    assert page.locator('#zml-toggle').count()==1 and load.is_checked()
    assert page.locator('.fixture-host').bounding_box()['width']==272
    assert page.evaluate("localStorage.getItem('zml.loadMods.v1')") == 'true'
    passed.append('host re-render remount; preserved opt-in; focus/Escape/backdrop; no duplicate controls')
    # Real native-owned CreateProcessW flow, armed by the production bridge.
    import psutil, time
    source=Path(args.start[0]).resolve().parent.parent/'tests/Release/NativeLaunchFixture.exe'
    import shutil
    native_exe=root/'1.6.0/Games.exe'; shutil.copyfile(source,native_exe)
    child_exe=root/'games/ZmlInstallerFixture.exe'
    unrelated=root/'games/ZmlUnrelatedWebFixture.exe';shutil.copyfile(child_exe,unrelated)
    marker=root/'native-web-marker.txt'
    native_process=subprocess.Popen([str(native_exe),str(child_exe),str(marker),str(unrelated)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,text=True,creationflags=subprocess.CREATE_NO_WINDOW)
    def stop_native():
        if native_process.poll() is None: native_process.terminate();native_process.wait(timeout=5)
    atexit.register(stop_native)
    observed={}; children=[]
    def original_native_start(dx):
        native_process.stdin.write('d\n' if dx else 'v\n');native_process.stdin.flush()
        line=native_process.stdout.readline().split();pid=int(line[1]);assert line[2]=='1',line
        children.append(pid);observed[pid]=psutil.Process(pid).cmdline()
        return pid
    page.expose_function('fixtureNativeStart',original_native_start)
    page.evaluate("""() => {
      document.querySelector('.fixture-main').onclick=()=>{
        window.fixtureStartClicks++;
        window.fixtureNativeStart(!!document.querySelector('.fixture-dx .opacity-100'));
      };
    }""")
    page.locator('.fixture-dx-mark div').evaluate("n=>n.className='opacity-100'")
    page.locator('.fixture-main').click()
    page.wait_for_function("!document.querySelector('#zml-launch-option input').disabled",timeout=65000)
    assert len(children)==1 and '-force-d3d11' in observed[children[-1]]
    assert requests[-1]=={} and page.evaluate('window.fixtureStartClicks')==5
    assert marker.read_text().startswith('native_started ')
    assert any(Path(m.path).name=='ZMLRuntime.dll' for m in psutil.Process(children[-1]).memory_maps())
    assert page.locator('#zml-launch-status').inner_text()=='' and page.locator('#zml-panel').is_hidden()
    passed.append('original native main handler exactly once; native-owned DX11 child injected; native post-start handler runs itself')
    try: psutil.Process(children[-1]).wait(timeout=10)
    except psutil.NoSuchProcess: pass
    page.locator('.fixture-dx-mark div').evaluate("n=>n.className='opacity-0'")
    page.locator('.fixture-main').click()
    page.wait_for_function("!document.querySelector('#zml-launch-option input').disabled",timeout=65000)
    assert len(children)==2 and '-force-d3d11' not in observed[children[-1]]
    assert page.evaluate('window.fixtureStartClicks')==6 and requests[-1]=={}
    assert page.locator('#zml-launch-status').inner_text()==''
    try: psutil.Process(children[-1]).wait(timeout=10)
    except psutil.NoSuchProcess: pass
    passed.append('default native arguments preserved; no bridge-created second game and no synthetic minimize')
    stop_native()
    # Reopen document: preference survives and mode can still turn off without altering manifests.
    manifest=(root/'ZML/mods/core/mod.ini').read_bytes()
    page.reload()
    assert page.evaluate("localStorage.getItem('zml.loadMods.v1')")=='true'
    page.evaluate(fixture_setup)
    page.wait_for_function("document.querySelector('.fixture-pill #zml-toggle') !== null")
    assert load.is_checked()
    load.uncheck(); page.locator('.fixture-main').click()
    assert page.evaluate('window.fixtureStartClicks')==1
    assert (root/'ZML/mods/core/mod.ini').read_bytes()==manifest
    assert not errors,errors
    passed.append('preference survives document reopen without touching enabled Mods; zero page errors')
    browser.close()
print(json.dumps({"passed": passed, "artifacts": str(args.fixture.parent)}, ensure_ascii=True, indent=2))
