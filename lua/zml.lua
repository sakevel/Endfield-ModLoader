-- ZML Lua API v1 services.
if _G.ZML and _G.ZML.api_version == 1 then return _G.ZML end
local snapshot = __ZML_REGISTRY__
local api = { api_version = 1, config_menu_version = 1 }
local FULL_NAMES = { "ZMDModLoader", "ZeroModLoader", "ZMLModLoader" }
function api.full_name()
    return FULL_NAMES[math.random(1, #FULL_NAMES)]
end
api.name = "ZML"
local listeners, entries = {}, {}
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
