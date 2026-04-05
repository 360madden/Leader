-- Leader/main.lua
local addon, Private = ...

--[[
-- LEADER TELEMETRY ENGINE v1.1
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
    print("  " .. prefix .. " dump status      — Show dump-log status")
    print("  " .. prefix .. " dump on          — Enable telemetry dumping")
    print("  " .. prefix .. " dump off         — Disable telemetry dumping")
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
        Private.DumpLog.PrintStatus()
    elseif subcommand == "off" then
        Private.DumpLog.SetEnabled(false)
        print("🛰️ Leader: dump logging OFF")
        Private.DumpLog.PrintStatus()
    elseif subcommand == "clear" then
        Private.DumpLog.Clear()
        print("🛰️ Leader: dump entries cleared")
        Private.DumpLog.PrintStatus()
    elseif subcommand == "interval" then
        local seconds = tonumber(remainder)
        if not seconds or seconds <= 0 then
            print("🛰️ Leader: dump interval expects a positive number of seconds.")
            return
        end

        Private.DumpLog.SetIntervalSeconds(seconds)
        print(string.format("🛰️ Leader: dump interval set to %.2fs", seconds))
        Private.DumpLog.PrintStatus()
    elseif subcommand == "help" then
        Private.DumpLog.PrintHelp(FormatSlashCommands())
    else
        print("🛰️ Leader: unknown dump command '" .. tostring(subcommand) .. "'.")
        Private.DumpLog.PrintHelp(FormatSlashCommands())
    end
end

local function HandleSlashCommand(_, params)
    params = string.lower(string.match(params or "", "^%s*(.-)%s*$"))
    local command = string.match(params, "^(%S+)") or ""

    if command == "diag" then
        Private.DiagUI.Toggle()
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

--- Orchestrates a single telemetry update.
local function UpdateTelemetry()
    Private.Renderer.SyncLayout()

    local now = Inspect.Time.Frame()
    local lastFrameTime = state.lastFrameTime or now
    local delta = now - lastFrameTime
    state.lastFrameTime = now

    state.lastUpdate = state.lastUpdate + delta
    if state.lastUpdate < state.updateInterval then return end
    state.lastUpdate = 0

    -- 1. Gather raw game data (guarded: returns nil during loading screens)
    local packet = Private.Gatherer.GetPacket()
    if not packet then return end

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

    -- 10. Persist recent telemetry samples for helper-app consumption via SavedVariables.
    Private.DumpLog.Record(packet)
end

--- Initializes the telemetry engine.
local function Init()
    LeaderConfig = LeaderConfig or {}
    Private.Renderer.Init()
    Private.DiagUI.Create()
    if Private.DumpLog and Private.DumpLog.GetStatus then
        Private.DumpLog.GetStatus()
    end

    local leaderRegistered = RegisterSlashCommand("leader")
    local leaderBridgeRegistered = RegisterSlashCommand("leaderbridge")
    Private.PrimarySlashCommand = registeredSlashCommands[1]

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
