-- Leader/Modules/StatusBadge.lua
local addon, Private = ...

local StatusBadge = {}

local panel = nil
local text = nil
local visible = false

local colors = {
    ok = { 0.25, 0.95, 0.45, 1 },
    wait = { 1.00, 0.82, 0.20, 1 },
    warn = { 1.00, 0.45, 0.35, 1 },
}

local function EnsureFrames()
    if panel then
        return
    end

    local context = UI.CreateContext("LeaderStatusBadgeCtx")
    panel = UI.CreateFrame("Frame", "LeaderStatusBadgePanel", context)
    panel:SetBackgroundColor(0.04, 0.04, 0.04, 0.78)
    panel:SetWidth(320)
    panel:SetHeight(24)
    panel:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 8, 190)
    panel:SetVisible(false)

    text = UI.CreateFrame("Text", "LeaderStatusBadgeText", panel)
    text:SetFontSize(11)
    text:SetFontColor(0.9, 0.9, 0.9, 1)
    text:SetPoint("TOPLEFT", panel, "TOPLEFT", 8, 4)
end

local function GetPacketDisplay(runtime)
    if runtime and runtime.packetValid then
        return "pkt live"
    end

    local age = runtime and tonumber(runtime.lastPacketAgeSeconds) or 0
    if age > 0 then
        return string.format("pkt %.1fs", age)
    end

    return "pkt --"
end

local function ResolveBadgeState(runtime, transition, render)
    if transition and transition.active then
        return "WARN", colors.wait, tostring(transition.reason or "transition")
    end

    if render and not render.frameComplete then
        return "WARN", colors.warn, "render"
    end

    if runtime and runtime.packetValid then
        return "OK", colors.ok, "idle"
    end

    return "WAIT", colors.wait, "no_packet"
end

function StatusBadge.Create()
    EnsureFrames()
end

function StatusBadge.SetVisible(value)
    EnsureFrames()
    visible = value and true or false
    panel:SetVisible(visible)

    if visible then
        StatusBadge.Refresh()
    end
end

function StatusBadge.Toggle()
    StatusBadge.SetVisible(not visible)
    return visible
end

function StatusBadge.GetStatus()
    return {
        visible = visible and true or false,
    }
end

function StatusBadge.PrintStatus()
    local status = StatusBadge.GetStatus()
    print(string.format(
        "🛰️ Leader Badge: %s",
        status.visible and "ON" or "OFF"))
end

function StatusBadge.Refresh()
    if not visible or not text then
        return
    end

    local runtime = Private.RuntimeStatus and Private.RuntimeStatus.GetStatus and Private.RuntimeStatus.GetStatus() or nil
    local transition = Private.TransitionState and Private.TransitionState.GetStatus and Private.TransitionState.GetStatus() or nil
    local render = Private.RenderHealth and Private.RenderHealth.GetStatus and Private.RenderHealth.GetStatus() or nil

    local stateLabel, stateColor, mode = ResolveBadgeState(runtime, transition, render)
    local playerTag = runtime and tostring(runtime.playerTag or "____") or "____"
    local renderWrites = render and tonumber(render.lastPixelWrites) or 0

    text:SetFontColor(stateColor[1], stateColor[2], stateColor[3], stateColor[4])
    text:SetText(string.format(
        "LEADER %s | %s | render %d/7 | %s | %s",
        stateLabel,
        GetPacketDisplay(runtime),
        renderWrites,
        tostring(mode or "idle"),
        playerTag))
end

Private.StatusBadge = StatusBadge
