-- ZML Lua API v1 services.
if _G.ZML and _G.ZML.api_version == 1 then return _G.ZML end
local snapshot = __ZML_REGISTRY__
local api = { api_version = 1, config_menu_version = 1, runtime_toggle_version = 1 }
local FULL_NAMES = { "ZMDModLoader", "ZeroModLoader", "ZMLModLoader" }
function api.full_name()
    return FULL_NAMES[math.random(1, #FULL_NAMES)]
end
api.name = "ZML"
local listeners, entries = {}, {}
local lifecycles, runtimeListeners = {}, {}
local settling = {}
local applying, runtimeKey, runtimeElapsed = false, nil, 0
local function copy(value)
    if type(value) ~= "table" then return value end
    local result = {}; for k, v in pairs(value) do result[k] = copy(v) end; return result
end
local function call(path)
    -- Standard LoadLua implementation.
    local ok, result = pcall(function()
        local bytes = LuaManagerInst:LoadLua(path)
        assert(bytes ~= nil, "Loader service unavailable")
        local fn, err = loadstring(bytes, "@" .. path)
        assert(fn, err); return fn()
    end)
    if not ok then return { ok = false, error = tostring(result) } end
    if type(result) ~= "table" then return { ok = false, error = "Invalid loader service response" } end
    return result
end
local function find(id)
    for _, item in ipairs(snapshot) do if item.id == id then return item end end
end
local function hex(value)
    return (value:gsub(".", function(c) return string.format("%02x", string.byte(c)) end))
end
function api.mods() return copy(snapshot) end
function api.mod(id) return copy(find(id)) end
function api.is_enabled(id)
    local item = find(id); return item ~= nil and item.enabled ~= false
end
-- Lifecycle handlers run only on the Lua/Unity thread. prepare must be a
-- side-effect-free safe-point check; changed must be idempotent.
function api.register_lifecycle(id, handler)
    assert(find(id) and type(handler) == 'table' and type(handler.changed) == 'function')
    assert(not lifecycles[id], 'Duplicate lifecycle owner')
    lifecycles[id] = handler
end
function api.subscribe_runtime(fn)
    assert(type(fn) == 'function');local token={};runtimeListeners[token]=fn
    return function() runtimeListeners[token]=nil end
end
local function syncRuntime()
    for id,on in pairs(settling) do
        local ok,err=pcall(lifecycles[id].changed,on)
        if not ok then api.activation_error=id..': '..tostring(err);return false,api.activation_error end
        settling[id]=nil
    end
    local plan = call('ZML/Activation/Desired')
    if not plan.ok then api.activation_error=plan.error;return false,plan.error end
    local changes={}
    -- Registry is dependency-first: stop dependents first, start providers first.
    for i=#snapshot,1,-1 do
        local item=snapshot[i]
        if item.enabled~=false and plan.states[item.id]==false then changes[#changes+1]={id=item.id,on=false} end
    end
    for _,item in ipairs(snapshot) do
        if item.enabled==false and plan.states[item.id]==true then changes[#changes+1]={id=item.id,on=true} end
    end
    if #changes==0 then api.activation_error=nil;return true end
    for _,change in ipairs(changes) do
        local h=lifecycles[change.id]
        if h and h.prepare then
            local ok,allowed,err=pcall(h.prepare,change.on)
            if not ok or allowed==false then
                api.activation_error=ok and (err or 'Close the active mod page first') or tostring(allowed)
                return false,api.activation_error
            end
        end
    end
    local committed=call('ZML/Activation/Commit/'..plan.mask)
    if not committed.ok then api.activation_error=committed.error;return false,committed.error end
    for _,item in ipairs(snapshot) do item.enabled=committed.states[item.id] end
    local errors={}
    for _,change in ipairs(changes) do
        local h=lifecycles[change.id]
        if h then local ok,err=pcall(h.changed,change.on);if not ok then
            settling[change.id]=change.on;errors[#errors+1]=change.id..': '..tostring(err)
        end end
        for _,fn in pairs(runtimeListeners) do pcall(fn,change.id,change.on) end
    end
    api.activation_error=#errors>0 and table.concat(errors,'; ') or nil
    return #errors==0,api.activation_error
end
function api.sync_runtime()
    if applying then return false, 'Runtime activation is busy' end
    applying=true
    local ok,done,err=pcall(syncRuntime)
    applying=false
    if not ok then api.activation_error=tostring(done);return false,api.activation_error end
    return done,err
end
function api.attach_runtime()
    if runtimeKey or not LuaUpdate then return end
    runtimeKey=LuaUpdate:Add('Tick',function(dt)
        runtimeElapsed=runtimeElapsed+math.max(0,tonumber(dt) or 0)
        if runtimeElapsed<1 then return end
        runtimeElapsed=0
        local ok,err=pcall(api.sync_runtime)
        if not ok then api.activation_error=tostring(err) end
    end)
end
function api.icon_data(id)
    if not find(id) then return nil, "Mod is not loaded" end
    local response = call("ZML/Icon/" .. id)
    if not response.ok then return nil, response.error end
    return response.data
end
function api.get(id)
    local item = find(id); if not item then return nil, "Mod is not loaded" end
    local response = call("ZML/Get/" .. id)
    if not response.ok then return nil, response.error end
    item.values = copy(response.values); return copy(response.values)
end
function api.set(id, key, value)
    local item = find(id); if not item then return false, "Mod is not loaded" end
    local field
    for _, f in ipairs(item.config.fields) do if f.key == key then field = f; break end end
    if not field then return false, "Unknown config key" end
    value = tostring(value)
    if #value > 1024 then return false, "Config value too long" end
    local response = call("ZML/Set/" .. id .. "/" .. key .. "/" .. hex(value))
    if not response.ok then return false, response.error end
    item.values = copy(response.values)
    -- Notify configuration change listeners.
    local pending = {}; for token, listener in pairs(listeners) do if listener.id == id then pending[#pending + 1] = token end end
    for _, token in ipairs(pending) do
        local listener = listeners[token]
        if listener then pcall(listener.fn, key, value, copy(response.values), response.restart) end
    end
    return true, nil, response.restart
end
function api.subscribe(id, fn)
    assert(find(id), "Mod is not loaded"); assert(type(fn) == "function", "Listener must be a function")
    local token = {}; listeners[token] = { id = id, fn = fn }
    return function() listeners[token] = nil end
end
function api.config_entry(id)
    local item = find(id); if not item or item.config_menu ~= "custom" then return nil, "No custom config menu" end
    if entries[id] then return entries[id] end
    local response = call("ZML/Entry/" .. id)
    if response.ok == false then return nil, response.error end
    if response.api ~= 1 or type(response.create) ~= "function" then return nil, "Config entry must provide api=1 and create(context)" end
    entries[id] = response; return response
end
function api.report(id, event)
    if not find(id) or type(event) ~= "string" or #event > 64 or not event:match("^[a-z0-9_:-]+$") then return false end
    return call("ZML/Report/" .. id .. "/" .. event).ok == true
end
_G.ZML = api
return api
