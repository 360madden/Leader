-- Leader/main.lua
local addon, Private = ...

--[[
-- LEADER TELEMETRY ENGINE v1.2
-- High-frequency telemetry orchestration for machine-vision multiboxing.
-- Orchestrates data gathering, encoding, and optical rendering.
--]]

local state = {
    lastUpdate   = 0,
    updateInterval = 0.033, -- 30Hz target (~33ms)
    lastFrameTime = nil,
}

local registeredSlashCommands = {}

local function ChatPrint(text)
    if Command and Command.Console and Command.Console.Display then
        Command.Console.Display("general", true, text, true)
    else
        print(text)
    end
end

local function Colorize(text, hexColor)
    return string.format('<font color="%s">%s</font>', hexColor, text)
end

local function FormatSlashCommands()
    if #registeredSlashCommands == 0 then
        return "/leaderbridge"
    end

    local commands = {}
    for index, commandName in ipairs(registeredSlashCommands) do
        commands[index] = "/" .. commandName
    end

    return table.concat(commands, " or ")
end

local function PrintHelp()
    local prefix = FormatSlashCommands()
    print("🛰️ Leader Commands:")
    print("  " .. prefix .. " diag             — Toggle telemetry audit overlay")
    print("  " .. prefix .. " status           — Show runtime heartbeat status")
    print("  " .. prefix .. " transition       — Show loading/transition status")
    print("  " .. prefix .. " export           — Show debug export snapshot status")
    print("  " .. prefix .. " render           — Show renderer health status")
    print("  " .. prefix .. " capabilities     — Show addon capability status")
    print("  " .. prefix .. " timeline         — Show session timeline status")
    print("  " .. prefix .. " stats            — Show session statistics")
    print("  " .. prefix .. " packetaudit      — Show packet/encoder audit status")
    print("  " .. prefix .. " cadence          — Show update cadence timing")
    print("  " .. prefix .. " profile          — Show client profile snapshot")
    print("  " .. prefix .. " target           — Show target snapshot status")
    print("  " .. prefix .. " dumpcapture      — Show dump-write capture stats")
    print("  " .. prefix .. " badge            — Toggle mini in-game status badge")
    print("  " .. prefix .. " dump status      — Show dump-log status")
    print("  " .. prefix .. " dump on          — Enable telemetry dumping")
    print("  " .. prefix .. " dump off         — Disable telemetry dumping")
    print("  " .. prefix .. " dump toggle      — Toggle telemetry dumping")
    print("  " .. prefix .. " dump clear       — Clear recent dump entries")
    print("  " .. prefix .. " dump interval X  — Set dump throttle interval in seconds")
end

local function HandleDumpCommand(params)
    local subcommand, remainder = string.match(params or "", "^(%S+)%s*(.-)$")
    subcommand = string.lower(subcommand or "")
    remainder = string.lower(string.match(remainder or "", "^%s*(.-)%s*$"))

    if subcommand == "" or subcommand == "status" then
        Private.DumpLog.PrintStatus()
    elseif subcommand == "on" then
        Private.DumpLog.SetEnabled(true)
        print("🛰️ Leader: dump logging ON")
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
            Private.RuntimeStatus.RecordCommand("dump", "on")
        end
        if Private.SessionStats and Private.SessionStats.RecordCommand then
            Private.SessionStats.RecordCommand("dump", "on")
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordCommand then
            Private.SessionTimeline.RecordCommand("dump", "on")
        end
        Private.DumpLog.PrintStatus()
    elseif subcommand == "off" then
        Private.DumpLog.SetEnabled(false)
        print("🛰️ Leader: dump logging OFF")
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
            Private.RuntimeStatus.RecordCommand("dump", "off")
        end
        if Private.SessionStats and Private.SessionStats.RecordCommand then
            Private.SessionStats.RecordCommand("dump", "off")
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordCommand then
            Private.SessionTimeline.RecordCommand("dump", "off")
        end
        Private.DumpLog.PrintStatus()
    elseif subcommand == "toggle" then
        local enabled = Private.DumpLog.Toggle()
        print("🛰️ Leader: dump logging " .. (enabled and "ON" or "OFF"))
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
            Private.RuntimeStatus.RecordCommand("dump", enabled and "toggle_on" or "toggle_off")
        end
        if Private.SessionStats and Private.SessionStats.RecordCommand then
            Private.SessionStats.RecordCommand("dump", enabled and "toggle_on" or "toggle_off")
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordCommand then
            Private.SessionTimeline.RecordCommand("dump", enabled and "toggle_on" or "toggle_off")
        end
        Private.DumpLog.PrintStatus()
    elseif subcommand == "clear" then
        Private.DumpLog.Clear()
        print("🛰️ Leader: dump entries cleared")
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
            Private.RuntimeStatus.RecordCommand("dump", "clear")
        end
        if Private.SessionStats and Private.SessionStats.RecordCommand then
            Private.SessionStats.RecordCommand("dump", "clear")
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordCommand then
            Private.SessionTimeline.RecordCommand("dump", "clear")
        end
        Private.DumpLog.PrintStatus()
    elseif subcommand == "interval" then
        local seconds = tonumber(remainder)
        if not seconds or seconds <= 0 then
            print("🛰️ Leader: dump interval expects a positive number of seconds.")
            return
        end

        Private.DumpLog.SetIntervalSeconds(seconds)
        print(string.format("🛰️ Leader: dump interval set to %.2fs", seconds))
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
            Private.RuntimeStatus.RecordCommand("dump", "interval")
        end
        if Private.SessionStats and Private.SessionStats.RecordCommand then
            Private.SessionStats.RecordCommand("dump", "interval")
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordCommand then
            Private.SessionTimeline.RecordCommand("dump", "interval")
        end
        Private.DumpLog.PrintStatus()
    elseif subcommand == "help" then
        Private.DumpLog.PrintHelp(FormatSlashCommands())
    else
        print("🛰️ Leader: unknown dump command '" .. tostring(subcommand) .. "'.")
        Private.DumpLog.PrintHelp(FormatSlashCommands())
    end
end

local function HandleBadgeCommand(params)
    local subcommand = string.lower(string.match(params or "", "^%s*(.-)%s*$"))

    if subcommand == "" or subcommand == "toggle" then
        local isVisible = Private.StatusBadge and Private.StatusBadge.Toggle and Private.StatusBadge.Toggle()
        print("🛰️ Leader: status badge " .. (isVisible and "ON" or "OFF"))
    elseif subcommand == "on" then
        if Private.StatusBadge and Private.StatusBadge.SetVisible then
            Private.StatusBadge.SetVisible(true)
        end
        print("🛰️ Leader: status badge ON")
    elseif subcommand == "off" then
        if Private.StatusBadge and Private.StatusBadge.SetVisible then
            Private.StatusBadge.SetVisible(false)
        end
        print("🛰️ Leader: status badge OFF")
    elseif subcommand == "status" then
        if Private.StatusBadge and Private.StatusBadge.PrintStatus then
            Private.StatusBadge.PrintStatus()
        end
    else
        print("🛰️ Leader: badge commands are on | off | toggle | status")
    end

    if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
        Private.RuntimeStatus.RecordCommand("badge", subcommand ~= "" and subcommand or "toggle")
    end
    if Private.SessionStats and Private.SessionStats.RecordCommand then
        Private.SessionStats.RecordCommand("badge", subcommand ~= "" and subcommand or "toggle")
    end
    if Private.SessionTimeline and Private.SessionTimeline.RecordCommand then
        Private.SessionTimeline.RecordCommand("badge", subcommand ~= "" and subcommand or "toggle")
    end
end

local function HandleSlashCommand(_, params)
    params = string.lower(string.match(params or "", "^%s*(.-)%s*$"))
    local command = string.match(params, "^(%S+)") or ""

    if command == "diag" then
        Private.DiagUI.Toggle()
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordCommand then
            Private.RuntimeStatus.RecordCommand("diag", "toggle")
        end
        if Private.SessionStats and Private.SessionStats.RecordCommand then
            Private.SessionStats.RecordCommand("diag", "toggle")
        end
    elseif command == "status" then
        if Private.RuntimeStatus and Private.RuntimeStatus.PrintStatus then
            Private.RuntimeStatus.PrintStatus()
        end
    elseif command == "transition" then
        if Private.TransitionState and Private.TransitionState.PrintStatus then
            Private.TransitionState.PrintStatus()
        end
    elseif command == "export" then
        if Private.DebugExport and Private.DebugExport.PrintStatus then
            Private.DebugExport.PrintStatus()
        end
    elseif command == "render" then
        if Private.RenderHealth and Private.RenderHealth.PrintStatus then
            Private.RenderHealth.PrintStatus()
        end
    elseif command == "capabilities" or command == "caps" then
        if Private.CapabilityStatus and Private.CapabilityStatus.PrintStatus then
            Private.CapabilityStatus.PrintStatus()
        end
    elseif command == "timeline" then
        if Private.SessionTimeline and Private.SessionTimeline.PrintStatus then
            Private.SessionTimeline.PrintStatus()
        end
    elseif command == "stats" then
        if Private.SessionStats and Private.SessionStats.PrintStatus then
            Private.SessionStats.PrintStatus()
        end
    elseif command == "packetaudit" or command == "packet" then
        if Private.PacketAudit and Private.PacketAudit.PrintStatus then
            Private.PacketAudit.PrintStatus()
        end
    elseif command == "cadence" then
        if Private.UpdateCadence and Private.UpdateCadence.PrintStatus then
            Private.UpdateCadence.PrintStatus()
        end
    elseif command == "profile" then
        if Private.ClientProfile and Private.ClientProfile.PrintStatus then
            Private.ClientProfile.PrintStatus()
        end
    elseif command == "target" then
        if Private.TargetSnapshot and Private.TargetSnapshot.PrintStatus then
            Private.TargetSnapshot.PrintStatus()
        end
    elseif command == "dumpcapture" then
        if Private.DumpCaptureStats and Private.DumpCaptureStats.PrintStatus then
            Private.DumpCaptureStats.PrintStatus()
        end
    elseif command == "badge" then
        HandleBadgeCommand(string.match(params, "^%S+%s*(.-)$") or "")
    elseif command == "dump" then
        HandleDumpCommand(string.match(params, "^%S+%s*(.-)$") or "")
    elseif command == "help" or command == "" then
        PrintHelp()
    end
end

local function RegisterSlashCommand(commandName)
    local slashEvent = Command.Slash.Register(commandName)
    if not slashEvent then
        return false
    end

    Command.Event.Attach(slashEvent, HandleSlashCommand, "LeaderSlash_" .. commandName)
    table.insert(registeredSlashCommands, commandName)
    return true
end

local function PrintLoadBanner()
    local addonName = (Name and Name.English) or tostring(addon or "Leader")
    local addonVersion = tostring(Version or "unknown")

    ChatPrint(
        Colorize(addonName, "#33D1FF")
        .. " "
        .. Colorize("loaded ok", "#FFFFFF")
        .. " "
        .. Colorize("v" .. addonVersion, "#FFD100")
    )
end

local function ClearTelemetryFrame()
    for i = 0, 6 do
        Private.Renderer.SetPixel(i, 0, 0, 0)
    end

    if Private.DiagUI and Private.DiagUI.Clear then
        Private.DiagUI.Clear()
    end
end

local function IsDumpEnabled()
    if Private.DumpLog and Private.DumpLog.GetStatus then
        local status = Private.DumpLog.GetStatus()
        return status and status.enabled and true or false
    end

    return false
end

--- Orchestrates a single telemetry update.
local function UpdateTelemetry()
    Private.Renderer.SyncLayout()

    local now = Inspect.Time.Frame()
    local lastFrameTime = state.lastFrameTime or now
    local delta = now - lastFrameTime
    state.lastFrameTime = now

    state.lastUpdate = state.lastUpdate + delta
    if state.lastUpdate < state.updateInterval then
        if Private.UpdateCadence and Private.UpdateCadence.RecordFrame then
            Private.UpdateCadence.RecordFrame(delta, false)
        end
        return
    end
    state.lastUpdate = 0
    if Private.UpdateCadence and Private.UpdateCadence.RecordFrame then
        Private.UpdateCadence.RecordFrame(delta, true)
    end

    if Private.Renderer and Private.Renderer.BeginFrame then
        Private.Renderer.BeginFrame()
    end

    -- 1. Gather raw game data (guarded: returns nil during loading screens)
    local packet = Private.Gatherer.GetPacket()
    if not packet then
        ClearTelemetryFrame()
        if Private.Renderer and Private.Renderer.EndFrame then
            Private.Renderer.EndFrame()
        end
        if Private.RuntimeStatus and Private.RuntimeStatus.RecordNoPacket then
            Private.RuntimeStatus.RecordNoPacket(true, IsDumpEnabled())
        end
        if Private.SessionStats and Private.SessionStats.RecordNoPacket then
            Private.SessionStats.RecordNoPacket()
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordNoPacket and Private.RuntimeStatus and Private.RuntimeStatus.GetStatus then
            Private.SessionTimeline.RecordNoPacket(Private.RuntimeStatus.GetStatus().nilPacketStreak)
        end
        if Private.TransitionState and Private.TransitionState.RecordNoPacket then
            Private.TransitionState.RecordNoPacket()
        end
        if Private.RenderHealth and Private.RenderHealth.Sync then
            Private.RenderHealth.Sync()
        end
        if Private.SessionTimeline and Private.SessionTimeline.RecordRender then
            Private.SessionTimeline.RecordRender()
        end
        if Private.SessionStats and Private.SessionStats.RecordRender then
            Private.SessionStats.RecordRender()
        end
        if Private.DebugExport and Private.DebugExport.SyncNoPacket then
            Private.DebugExport.SyncNoPacket()
        end
        if Private.StatusBadge and Private.StatusBadge.Refresh then
            Private.StatusBadge.Refresh()
        end
        return
    end

    -- 2. Pixel 0: Sync beacon (Static Magenta: R=1, G=0, B=1)
    Private.Renderer.SetPixel(0, 1, 0, 1)

    -- 3. Pixel 1: Status (HP + Flags)
    local r1, g1, b1 = Private.Encoder.PackState(packet.playerHP, packet.targetHP, packet.flags)
    Private.Renderer.SetPixel(1, r1, g1, b1)

    -- 4. Pixel 2: Coord X
    local r2, g2, b2 = Private.Encoder.PackCoord(packet.coordX)
    Private.Renderer.SetPixel(2, r2, g2, b2)

    -- 5. Pixel 3: Coord Y (Elevation)
    local r3, g3, b3 = Private.Encoder.PackCoord(packet.coordY)
    Private.Renderer.SetPixel(3, r3, g3, b3)

    -- 6. Pixel 4: Coord Z
    local r4, g4, b4 = Private.Encoder.PackCoord(packet.coordZ)
    Private.Renderer.SetPixel(4, r4, g4, b4)

    -- 7. Pixel 5: Movement heading (derived from coordinate deltas) + Zone hash (B channel)
    local r5, g5, b5 = Private.Encoder.PackHeading(packet.facing, packet.zoneHash)
    Private.Renderer.SetPixel(5, r5, g5, b5)

    -- 8. Pixel 6: Player identity tag
    local r6, g6, b6 = Private.Encoder.PackPlayerTag(packet.playerTag)
    Private.Renderer.SetPixel(6, r6, g6, b6)

    -- 9. Update in-game Diagnostic UI (only if visible)
    Private.DiagUI.Update(packet)

    if Private.Renderer and Private.Renderer.EndFrame then
        Private.Renderer.EndFrame()
    end

    -- 10. Persist recent telemetry samples for helper-app consumption via SavedVariables.
    local dumpRecorded = Private.DumpLog.Record(packet)

    if Private.RuntimeStatus and Private.RuntimeStatus.RecordPacket then
        Private.RuntimeStatus.RecordPacket(packet, IsDumpEnabled())
    end
    if Private.SessionStats and Private.SessionStats.RecordPacket then
        Private.SessionStats.RecordPacket(packet)
    end
    if Private.PacketAudit and Private.PacketAudit.Sync then
        Private.PacketAudit.Sync(packet)
    end
    if Private.ClientProfile and Private.ClientProfile.Sync then
        Private.ClientProfile.Sync(packet)
    end
    if Private.TargetSnapshot and Private.TargetSnapshot.Sync then
        Private.TargetSnapshot.Sync(packet)
    end
    if Private.DumpCaptureStats and Private.DumpCaptureStats.Sync then
        Private.DumpCaptureStats.Sync(dumpRecorded)
    end

    if Private.TransitionState and Private.TransitionState.RecordPacket then
        Private.TransitionState.RecordPacket(packet)
    end

    if Private.RenderHealth and Private.RenderHealth.Sync then
        Private.RenderHealth.Sync()
    end
    if Private.SessionTimeline and Private.SessionTimeline.RecordPacket then
        Private.SessionTimeline.RecordPacket(packet)
    end
    if Private.SessionTimeline and Private.SessionTimeline.RecordRender then
        Private.SessionTimeline.RecordRender()
    end
    if Private.SessionStats and Private.SessionStats.RecordRender then
        Private.SessionStats.RecordRender()
    end

    if Private.DebugExport and Private.DebugExport.Sync then
        Private.DebugExport.Sync(packet)
    end

    if Private.StatusBadge and Private.StatusBadge.Refresh then
        Private.StatusBadge.Refresh()
    end
end

--- Initializes the telemetry engine.
local function Init()
    LeaderConfig = LeaderConfig or {}
    if Private.RuntimeStatus and Private.RuntimeStatus.InitSession then
        Private.RuntimeStatus.InitSession()
    end
    if Private.SessionTimeline and Private.SessionTimeline.Init then
        Private.SessionTimeline.Init()
    end
    if Private.SessionStats and Private.SessionStats.Init then
        Private.SessionStats.Init()
    end
    if Private.PacketAudit and Private.PacketAudit.Init then
        Private.PacketAudit.Init()
    end
    if Private.UpdateCadence and Private.UpdateCadence.Init then
        Private.UpdateCadence.Init()
    end
    if Private.ClientProfile and Private.ClientProfile.Init then
        Private.ClientProfile.Init()
    end
    if Private.TargetSnapshot and Private.TargetSnapshot.Init then
        Private.TargetSnapshot.Init()
    end
    if Private.DumpCaptureStats and Private.DumpCaptureStats.Init then
        Private.DumpCaptureStats.Init()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.Init then
        Private.CapabilityStatus.Init()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("runtime", Private.RuntimeStatus and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("sessionTimeline", Private.SessionTimeline and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("sessionStats", Private.SessionStats and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("packetAudit", Private.PacketAudit and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("updateCadence", Private.UpdateCadence and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("clientProfile", Private.ClientProfile and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("targetSnapshot", Private.TargetSnapshot and true or false)
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("dumpCaptureStats", Private.DumpCaptureStats and true or false)
    end
    if Private.TransitionState and Private.TransitionState.Init then
        Private.TransitionState.Init()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("transition", Private.TransitionState and true or false)
    end
    if Private.DebugExport and Private.DebugExport.Init then
        Private.DebugExport.Init()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("debugExport", Private.DebugExport and true or false)
    end
    if Private.RenderHealth and Private.RenderHealth.Init then
        Private.RenderHealth.Init()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("renderHealth", Private.RenderHealth and true or false)
    end
    Private.Renderer.Init()
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("renderer", true)
    end
    if Private.StatusBadge and Private.StatusBadge.Create then
        Private.StatusBadge.Create()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("statusBadge", Private.StatusBadge and true or false)
    end
    Private.DiagUI.Create()
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("diag", Private.DiagUI and true or false)
    end
    if Private.DumpLog and Private.DumpLog.GetStatus then
        Private.DumpLog.GetStatus()
    end
    if Private.CapabilityStatus and Private.CapabilityStatus.MarkModuleReady then
        Private.CapabilityStatus.MarkModuleReady("dump", Private.DumpLog and true or false)
    end

    local leaderRegistered = RegisterSlashCommand("leader")
    local leaderBridgeRegistered = RegisterSlashCommand("leaderbridge")
    Private.PrimarySlashCommand = registeredSlashCommands[1]

    if Private.CapabilityStatus and Private.CapabilityStatus.RecordSlashRegistration then
        Private.CapabilityStatus.RecordSlashRegistration(Private.PrimarySlashCommand, leaderRegistered, leaderBridgeRegistered)
    end
    if Private.ClientProfile and Private.ClientProfile.RecordSlashRegistration then
        Private.ClientProfile.RecordSlashRegistration(Private.PrimarySlashCommand, leaderRegistered, leaderBridgeRegistered)
    end
    if Private.RuntimeStatus and Private.RuntimeStatus.RecordSlashRegistration then
        Private.RuntimeStatus.RecordSlashRegistration(Private.PrimarySlashCommand, leaderRegistered, leaderBridgeRegistered)
    end

    Command.Event.Attach(Event.System.Update.Begin, UpdateTelemetry, "LeaderUpdate")

    if Private.PrimarySlashCommand then
        PrintLoadBanner()
        print("🛰️ Type /" .. Private.PrimarySlashCommand .. " help for commands.")
        if not leaderRegistered and leaderBridgeRegistered then
            print("🛰️ Leader: /leader is unavailable on this client, so /leaderbridge was registered instead.")
        end
    else
        PrintLoadBanner()
        print("🛰️ Leader Telemetry Bridge loaded, but no slash command could be registered.")
    end
end

Init()
