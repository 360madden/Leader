-- Leader/Modules/DiagnosticUI.lua
local addon, Private = ...

--[[
-- LEADER TELEMETRY AUDIT UI v1.2
-- Toggleable on-screen overlay showing live encoded values.
-- Type the registered Leader slash command with "diag" to toggle.
-- Aligned with Gatherer.lua v1.2 packet schema.
--]]

local DiagUI = {}
local _panel  = nil
local _text   = nil
local _visible = false

--- Creates the diagnostic panel (called once from main.lua Init).
function DiagUI.Create()
    if _panel then return end

    -- Attach to a dedicated context so it has no parent scaling
    local ctx = UI.CreateContext("LeaderDiagCtx")

    _panel = UI.CreateFrame("Frame", "LeaderDiagPanel", ctx)
    _panel:SetBackgroundColor(0.05, 0.05, 0.05, 0.82)
    _panel:SetWidth(230)
    _panel:SetHeight(176)
    _panel:SetPoint("TOPLEFT", UIParent, "TOPLEFT", 8, 8)
    _panel:SetVisible(false)

    -- Title bar
    local title = UI.CreateFrame("Text", "LeaderDiagTitle", _panel)
    title:SetText("  🛰️ LEADER  TELEMETRY AUDIT")
    title:SetFontSize(12)
    title:SetFontColor(0, 0.9, 0.9, 1)
    title:SetPoint("TOPLEFT", _panel, "TOPLEFT", 0, 6)

    -- Divider
    local div = UI.CreateFrame("Frame", "LeaderDiagDiv", _panel)
    div:SetHeight(1)
    div:SetWidth(230)
    div:SetBackgroundColor(0, 0.6, 0.6, 0.6)
    div:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, 4)

    -- Content text
    _text = UI.CreateFrame("Text", "LeaderDiagText", _panel)
    _text:SetFontSize(11)
    _text:SetFontColor(0.9, 0.9, 0.9, 1)
    _text:SetPoint("TOPLEFT", div, "BOTTOMLEFT", 6, 6)
end

--- Updates the displayed values. Called every tick from main.lua.
-- @param packet The raw packet table from Gatherer.GetPacket().
function DiagUI.Update(packet)
    if not _visible or not _text or not packet then return end

    local flags = packet.flags or 0
    local isCombat  = (flags % 2)        ~= 0 and "YES" or "no"
    local hasTarget = (math.floor(flags / 2) % 2)  ~= 0 and "YES" or "no"
    local isMoving  = (math.floor(flags / 4) % 2)  ~= 0 and "YES" or "no"
    local isAlive   = (math.floor(flags / 8) % 2)  ~= 0 and "YES" or "no"
    local isMounted = (math.floor(flags / 16) % 2) ~= 0 and "MT"  or "  "

    _text:SetText(string.format(
        "X:  %9.2f    Z: %9.2f\n"..
        "Y (elev): %6.2f\n"..
        "Heading:  %6.4f rad\n"..
        "Speed:    %6.2f m/s\n"..
        "Tag:      %s\n"..
        "Zone:     %3d   %s\n"..
        "HP:  %3d%%   Tgt HP: %3d%%\n"..
        "Combat: %-3s  Target: %-3s\n"..
        "Moving: %-3s  Alive: %-3s",
        packet.coordX or 0, packet.coordZ or 0,
        packet.coordY or 0,
        packet.facing or 0,
        packet.motionSpeed or 0,
        packet.playerTag or "____",
        packet.zoneHash or 0, isMounted,
        math.floor(((packet.playerHP or 0) / 255) * 100),
        math.floor(((packet.targetHP or 0) / 255) * 100),
        isCombat, hasTarget,
        isMoving, isAlive
    ))
end

--- Clears the displayed values when no valid packet is available.
function DiagUI.Clear()
    if not _text then return end

    _text:SetText("Waiting for telemetry...")
end

--- Toggles the panel visibility.
function DiagUI.Toggle()
    _visible = not _visible
    if _panel then _panel:SetVisible(_visible) end
    if Private.PrimarySlashCommand then
        print("🛰️ Leader: Audit UI " .. (_visible and "ON" or "OFF") .. " — /" .. Private.PrimarySlashCommand .. " diag to toggle")
    else
        print("🛰️ Leader: Audit UI " .. (_visible and "ON" or "OFF") .. ". No slash command is currently registered.")
    end
end

Private.DiagUI = DiagUI
