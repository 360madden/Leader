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

local function HandleSlashCommand(_, params)
    params = string.lower(string.match(params or "", "^%s*(.-)%s*$"))
    local command = string.match(params, "^(%S+)") or ""

    if command == "diag" then
        Private.DiagUI.Toggle()
    elseif command == "help" or command == "" then
        print("🛰️ Leader Commands:")
        print("  " .. FormatSlashCommands() .. " diag   — Toggle telemetry audit overlay")
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

    -- 7. Pixel 5: Heading (facing radians) + Zone hash (B channel)
    local r5, g5, b5 = Private.Encoder.PackHeading(packet.facing, packet.zoneHash)
    Private.Renderer.SetPixel(5, r5, g5, b5)

    -- 8. Pixel 6: Target identity hash
    local r6, g6, b6 = Private.Encoder.PackHash(packet.targetID)
    Private.Renderer.SetPixel(6, r6, g6, b6)

    -- 9. Update in-game Diagnostic UI (only if visible)
    Private.DiagUI.Update(packet)
end

--- Initializes the telemetry engine.
local function Init()
    Private.Renderer.Init()
    Private.DiagUI.Create()

    local leaderRegistered = RegisterSlashCommand("leader")
    local leaderBridgeRegistered = RegisterSlashCommand("leaderbridge")
    Private.PrimarySlashCommand = registeredSlashCommands[1]

    Command.Event.Attach(Event.System.Update.Begin, UpdateTelemetry, "LeaderUpdate")

    if Private.PrimarySlashCommand then
        print("🛰️ Leader Telemetry Bridge v1.1 loaded. Type /" .. Private.PrimarySlashCommand .. " help for commands.")
        if not leaderRegistered and leaderBridgeRegistered then
            print("🛰️ Leader: /leader is unavailable on this client, so /leaderbridge was registered instead.")
        end
    else
        print("🛰️ Leader Telemetry Bridge v1.1 loaded, but no slash command could be registered.")
    end
end

Init()
