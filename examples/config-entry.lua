-- Declare config_menu=custom and config_entry=config-entry.lua.
-- config=<schema.ini> defines every persistable key, even with a custom UI.
return {
    api = 1,
    create = function(ctx)
        local values, err = ctx.get(); assert(values, err)
        local width = ctx.width - 24
        local root = ctx.panel(ctx.parent, "Example", 12, 12, width, 600)
        ctx.text(root, "模组自己实现的复杂配置页", 32, 24, width - 64, 72, 36)
        ctx.text(root, "可以创建原生 Unity 控件、订阅配置变化，并管理自己的对象。", 32, 114, width - 64, 110, 26)
        local state = ctx.text(root, "功能开关：" .. values.enabled, 32, 256, width - 64, 72, 28)
        local unsubscribe = ctx.subscribe(function(_, _, changed) state.text = "功能开关：" .. changed.enabled end)
        ctx.button(root, "切换功能", 32, 374, 460, function()
            local current = assert(ctx.get())
            local ok, message = ctx.set("enabled", current.enabled ~= "true"); assert(ok, message)
        end)
        ctx.button(root, "返回模组详情", 32, 478, 460, ctx.back)
        return function() unsubscribe() end -- context also cleans subscriptions
    end,
}
