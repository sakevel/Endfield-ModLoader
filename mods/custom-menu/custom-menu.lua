-- Independent ZML example mod. All game objects are created at runtime.
-- The controller source is supplied by the running client, never archived here.
do
    if not _G.ZMLCustomMenu then
        local mod = { mount = function() end, phase = nil, font = nil }
        _G.ZMLCustomMenu = mod
        local reportFile = __ZML_STATUS_FILE__
        local function report(event, detail)
            mod.state = event
            mod.error = detail
            pcall(function()
                CS.System.IO.File.AppendAllText(reportFile, event .. (detail and (" | " .. tostring(detail)) or "") .. "\n")
            end)
        end
        report("controller_loaded")
        local ready, failure = xpcall(function()
            local Unity = CS.UnityEngine
            local Palette = {
                background = Unity.Color(0.045, 0.065, 0.085, 1),
                surface = Unity.Color(0.10, 0.14, 0.18, 1),
                ink = Unity.Color(0.94, 0.96, 0.98, 1),
                muted = Unity.Color(0.63, 0.72, 0.80, 1),
                accent = Unity.Color(0.98, 0.82, 0.32, 1),
            }
            local function exists(object) return object ~= nil and NotNull(object) end
            local function node(parent, name, x, y, width, height)
                local object = Unity.GameObject(name)
                local transform = object:AddComponent(typeof(Unity.RectTransform))
                transform:SetParent(parent, false)
                transform.anchorMin = Unity.Vector2(0, 1)
                transform.anchorMax = Unity.Vector2(0, 1)
                transform.pivot = Unity.Vector2(0, 1)
                transform.anchoredPosition = Unity.Vector2(x, -y)
                transform.sizeDelta = Unity.Vector2(width, height)
                return object, transform
            end
            local function paint(parent, name, x, y, w, h, color)
                local object, transform = node(parent, name, x, y, w, h)
                local image = object:AddComponent(typeof(Unity.UI.Image))
                image.color = color
                return object, transform, image
            end
            local function text(parent, value, x, y, w, h, size, color)
                local object = node(parent, "Label", x, y, w, h)
                local label = object:AddComponent(typeof(CS.TMPro.TextMeshProUGUI))
                label.font = mod.font
                label.text, label.fontSize, label.color = value, size, color or Palette.ink
                label.raycastTarget = false
                return label
            end
            local function control(parent, title, x, y, width, clicked)
                local object, transform, image = paint(parent, title, x, y, width, 76, Palette.surface)
                local button = object:AddComponent(typeof(Unity.UI.Button))
                button.targetGraphic = image
                button.onClick:AddListener(clicked)
                local label = text(transform, title, 16, 12, width - 32, 52, 28)
                label.alignment = CS.TMPro.TextAlignmentOptions.Center
            end
            local Base = require_ex("Phase/Core/PhaseBase").PhaseBase
            local Page = HL.Class("PhaseZMLCustomMenu", Base)
            Page.s_messages = HL.StaticField(HL.Table) << {}
            Page.pageObject = HL.Field(HL.Any)
            Page.inputGroup = HL.Field(HL.Number) << -1
            Page.backAction = HL.Field(HL.Number) << -1
            Page.counter = HL.Field(HL.Number) << 0
            local function back(self)
                if PhaseManager:GetTopPhaseId() == self.phaseId then PhaseManager:PopPhase(self.phaseId) end
            end
            Page._OnActivated = HL.Override() << function(self)
                local opened, err = xpcall(function()
                    if not exists(self.pageObject) then
                        self.pageObject = Unity.GameObject("ZML.CustomMenu.Page")
                        self.pageObject:AddComponent(typeof(Unity.RectTransform))
                        local canvas = self.pageObject:AddComponent(typeof(Unity.Canvas))
                        canvas.renderMode = Unity.RenderMode.ScreenSpaceOverlay
                        canvas.sortingOrder = 29000
                        local scaler = self.pageObject:AddComponent(typeof(Unity.UI.CanvasScaler))
                        scaler.uiScaleMode = Unity.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize
                        scaler.referenceResolution = Unity.Vector2(1920, 1080)
                        scaler.matchWidthOrHeight = 0.5
                        self.pageObject:AddComponent(typeof(Unity.UI.GraphicRaycaster))
                        local _, layout = paint(self.pageObject.transform, "Background", 0, 0, 1920, 1080, Palette.background)
                        layout.anchorMin, layout.anchorMax = Unity.Vector2.zero, Unity.Vector2.one
                        layout.offsetMin, layout.offsetMax = Unity.Vector2.zero, Unity.Vector2.zero
                        paint(layout, "Accent", 112, 120, 92, 7, Palette.accent)
                        text(layout, "自定义界面", 112, 155, 1520, 84, 56)
                        text(layout, "ENDFIELD MOD LOADER  /  CUSTOM MENU", 116, 248, 1450, 52, 24, Palette.muted)
                        local _, card = paint(layout, "Card", 112, 354, 1696, 426, Palette.surface)
                        text(card, "这是加载器中的一个独立 Mod", 48, 40, 1510, 68, 36)
                        text(card, "原生 ESC 菜单按钮 → PhaseManager → 游戏内 Unity UI\n关闭本 Mod 后，菜单保持原样。\n文件、存档或游戏数值。", 48, 135, 1510, 182, 28, Palette.muted)
                        local feedback = text(card, "点击次数：0", 48, 332, 960, 64, 29)
                        control(card, "点击测试", 1280, 314, 350, function()
                            self.counter = self.counter + 1
                            feedback.text = "点击次数：" .. tostring(self.counter)
                            report("interaction:" .. tostring(self.counter))
                        end)
                        control(layout, "返回 ESC 菜单", 112, 868, 380, function() back(self) end)
                        text(layout, "ESC 返回 · Custom Menu Mod", 540, 888, 1170, 60, 26, Palette.muted)
                    end
                    self.pageObject:SetActive(true)
                    if self.inputGroup < 0 then
                        self.inputGroup = InputManagerInst:CreateGroup(UIManager.uiInputBindingGroupMonoTarget.groupId)
                        self.backAction = UIUtils.bindInputPlayerAction("common_back", function() back(self) end, self.inputGroup)
                    end
                    InputManagerInst:ToggleGroup(self.inputGroup, true)
                    report("page_open")
                end, debug.traceback)
                if not opened then
                    report("page_error", err)
                    if exists(self.pageObject) then self.pageObject:SetActive(false) end
                    self:_StartCoroutine(function() coroutine.step(); back(self) end)
                end
            end
            Page._OnDeActivated = HL.Override() << function(self)
                if exists(self.pageObject) then self.pageObject:SetActive(false) end
                if self.inputGroup >= 0 then InputManagerInst:ToggleGroup(self.inputGroup, false) end
                report("page_returned")
            end
            Page._OnDestroy = HL.Override() << function(self)
                if self.backAction >= 0 then InputManagerInst:DeleteBinding(self.backAction) end
                if self.inputGroup >= 0 then InputManagerInst:DeleteGroup(self.inputGroup) end
                if exists(self.pageObject) then Unity.Object.Destroy(self.pageObject) end
                self.pageObject, self.backAction, self.inputGroup = nil, -1, -1
                report("page_destroyed")
            end
            HL.Commit(Page)
            local moduleName = "Phase/ZMLCustomMenu/PhaseZMLCustomMenu"
            hg.loadedModules[moduleName] = { name = moduleName, env = { PhaseZMLCustomMenu = Page } }
            hg.loadedModuleNameList[#hg.loadedModuleNameList + 1] = moduleName

            mod.mount = function(controller)
                local initialSize = controller.view.scrollViewContent.sizeDelta
                local groupObject, attached = nil, false
                local success, err = xpcall(function()
                    assert(controller.m_btnData[93] == nil, "Custom slot 93 is already occupied")
                    if not mod.phase then
                        local manager = PhaseManager
                        local id = 1
                        for other in pairs(manager.m_cfgs) do id = math.max(id, other + 1) end
                        local data = { name = "ZMLCustomMenu", panels = {}, fov = UIManager:GetUICameraFOV() }
                        manager.m_cfgs[id] = setmetatable({ id = id, name = data.name, data = data }, { __index = data })
                        manager.phaseIds[data.name], manager.phaseId2Names[id] = id, data.name
                        mod.phase = id
                    end
                    local parent = controller.view.rightList
                    local rows = {}
                    for i = 0, parent.childCount - 1 do
                        local row = parent:GetChild(i)
                        if row.name:sub(1, 5) == "Group" then rows[#rows + 1] = row end
                    end
                    assert(#rows > 1, "Menu row contract changed")
                    local dy = rows[2].anchoredPosition.y - rows[1].anchoredPosition.y
                    groupObject = Unity.Object.Instantiate(rows[1].gameObject, parent, false)
                    groupObject.name = "GroupZMLCustomMenu"
                    local row = groupObject.transform
                    for i = row.childCount - 1, 0, -1 do Unity.Object.DestroyImmediate(row:GetChild(i).gameObject) end
                    row.anchoredPosition = Unity.Vector2(rows[1].anchoredPosition.x, rows[#rows].anchoredPosition.y + dy)
                    local copy = Unity.Object.Instantiate(controller.view.gameToolBtn.gameObject, row, false)
                    copy.name = "ZMLCustomMenuButton"
                    copy:SetActive(true)
                    local view = Utils.wrapLuaNode(copy)
                    assert(view.btn and view.text, "Button LuaReference contract changed")
                    view.text.text = "自定义界面"
                    mod.font = view.text:GetComponent(typeof(CS.TMPro.TMP_Text)).font
                    for _, key in ipairs({ "lockIcon", "forbidIcon", "redDot" }) do
                        if view[key] then view[key].gameObject:SetActive(false) end
                    end
                    controller.m_btnData[93] = { view = view, phaseId = mod.phase, needRefreshUnlock = false, needShowRedDot = false }
                    attached = true
                    controller.view.scrollViewContent.sizeDelta = Unity.Vector2(initialSize.x, initialSize.y + math.abs(dy) * parent.localScale.y)
                    report("button_attached")
                end, debug.traceback)
                if not success then
                    if attached then controller.m_btnData[93] = nil end
                    controller.view.scrollViewContent.sizeDelta = initialSize
                    if exists(groupObject) then Unity.Object.DestroyImmediate(groupObject) end
                    report("button_error", err)
                end
            end
        end, debug.traceback)
        if not ready then report("bootstrap_error", failure) end
    end
end
