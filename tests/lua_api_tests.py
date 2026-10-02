"""Lua/API/native-adapter behavioral checks; not client/visual acceptance."""
import argparse, pathlib, subprocess, sys
p=argparse.ArgumentParser();p.add_argument('--lupa-dir');p.add_argument('--services',required=True);args=p.parse_args()
if args.lupa_dir:sys.path.insert(0,args.lupa_dir)
from lupa.lua54 import LuaRuntime
root=pathlib.Path(__file__).resolve().parents[1]
server=subprocess.Popen([str(pathlib.Path(args.services).resolve()),'--serve'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
def route(_, path):
    server.stdin.write((path+'\n').encode());server.stdin.flush()
    length=int(server.stdout.readline());data=server.stdout.read(length)
    assert len(data)==length
    return data.decode('utf-8')
try:
    lua=LuaRuntime(unpack_returned_tuples=True)
    lua.globals().native_route=route
    lua.execute('loadstring=load; LuaManagerInst={LoadLua=function(self,p)return native_route(self,p)end}')
    api=lua.execute(route(None,'ZML/Api'))
    lua.execute('''
        assert(#ZML.mods()==3 and ZML.mod('disabled')==nil)
        assert(ZML.mod('demo').config_menu=='standard' and ZML.config_entry('demo')==nil)
        assert(ZML.mod('custom-demo').config_menu=='custom')
        local first=ZML.mods();first[1].name='modified';assert(ZML.mods()[1].name~='modified')
        local v=ZML.get('demo');v.enabled='false';assert(ZML.get('demo').enabled=='true')
        local events=0
        local unsub=ZML.subscribe('demo',function(key,value,values,restart)
            assert(key=='amount' and value=='4' and values.amount=='4' and restart);events=events+1
        end)
        assert(not ZML.set('demo','amount',3));assert(events==0)
        assert(ZML.set('demo','amount',4));assert(events==1);unsub()
        assert(ZML.set('demo','amount',2));assert(events==1)
        assert(ZML.config_entry('custom-demo').api==1 and ZML.config_entry('custom-demo')==ZML.config_entry('custom-demo'))
        assert(ZML.config_entry('fixture')==nil)
        assert(ZML.report('fixture','page_open') and not ZML.report('fixture','../../private'))
    ''')
    print('PASS: standalone loader Lua API and real native services')

finally:
    server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.kill();server.wait()
    if server.returncode:raise RuntimeError(server.stderr.read().decode(errors='replace'))
