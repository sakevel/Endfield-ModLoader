"""Test runtime activation lifecycle and service communication."""
import argparse, subprocess, sys
p=argparse.ArgumentParser();p.add_argument('--lupa-dir');p.add_argument('--services',required=True);a=p.parse_args()
sys.path.insert(0,a.lupa_dir)
from lupa.lua54 import LuaRuntime
server=subprocess.Popen([a.services,'--serve'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
def route(_,path):
    server.stdin.write((path+'\n').encode());server.stdin.flush()
    n=int(server.stdout.readline());return server.stdout.read(n).decode()
try:
    lua=LuaRuntime(unpack_returned_tuples=True);lua.globals().native_route=route
    lua.execute('''loadstring=load;LuaManagerInst={LoadLua=function(self,p)return native_route(self,p)end}
    local updates={};LuaUpdate={Add=function(self,stage,fn)assert(stage=='Tick');updates[#updates+1]=fn;return #updates end}
    function tick(dt) for _,fn in ipairs(updates)do fn(dt)end end
    function disk(id,on) assert(load(native_route(nil,'test/'..id..'/'..(on and '1' or '0')))().ok) end
    ''')
    lua.execute(route(None,'ZML/Api'))
    lua.execute('''
    local calls={},{}
    local busy=true
    ZML.register_lifecycle('library',{changed=function(on)calls[#calls+1]='library:'..tostring(on)end})
    ZML.register_lifecycle('consumer',{
        prepare=function()return not busy,'editor open'end,
        changed=function(on)
            local done,err=ZML.sync_runtime();assert(not done and err=='Runtime activation is busy')
            if failCleanup then failCleanup=false;error('fixture cleanup retry')end
            calls[#calls+1]='consumer:'..tostring(on)
        end})
    local notified=0;local cancel=ZML.subscribe_runtime(function()notified=notified+1 end)
    ZML.attach_runtime();ZML.attach_runtime()
    disk('library',false);disk('consumer',false)
    tick(.5);assert(ZML.is_enabled('consumer') and #calls==0)
    tick(.5);assert(ZML.is_enabled('consumer') and #calls==0 and ZML.activation_error=='editor open')
    busy=false;tick(1)
    assert(not ZML.is_enabled('library') and not ZML.is_enabled('consumer'))
    assert(calls[1]=='consumer:false' and calls[2]=='library:false' and notified==2)
    for i=1,20 do
        calls={};disk('library',true);disk('consumer',true);assert(ZML.sync_runtime())
        assert(calls[1]=='library:true' and calls[2]=='consumer:true')
        calls={};disk('library',false);disk('consumer',false);assert(ZML.sync_runtime())
        assert(calls[1]=='consumer:false' and calls[2]=='library:false')
    end
    cancel();local before=notified
    disk('consumer',true);assert(not ZML.sync_runtime());assert(not ZML.is_enabled('consumer'))
    disk('library',true);assert(ZML.sync_runtime());assert(notified==before)
    local mods=ZML.mods();mods[1].enabled=false;assert(ZML.is_enabled('library'))
    assert(ZML.mod('consumer').enabled and not ZML.is_enabled('absent'))
    disk('legacy',false);assert(not ZML.sync_runtime());assert(ZML.is_enabled('legacy'))
    disk('legacy',true);disk('library',false);disk('consumer',false);failCleanup=true;calls={}
    assert(not ZML.sync_runtime());assert(ZML.activation_error:find('fixture cleanup retry',1,true))
    assert(not ZML.is_enabled('consumer') and not ZML.is_enabled('library') and #calls==1)
    assert(ZML.sync_runtime());assert(calls[2]=='consumer:false' and #calls==2)
    assert(ZML.activation_error==nil) -- no repeated provider cleanup; reentrant retry remains guarded

    ''')
    print('PASS real native activation + deferred safe-point, ordering, dependency rollback, 20 cycles, one poller')
finally:
    server.stdin.close();server.wait(timeout=5)
    if server.returncode:raise RuntimeError(server.stderr.read().decode(errors='replace'))
