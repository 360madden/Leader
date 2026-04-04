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
}

--- Orchestrates a single telemetry update.
local function UpdateTelemetry(handle, delta)
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

    -- Slash command: /leader diag
    table.insert(Command.Slash.Register("leader"), {
        function(params)
            if params == "diag" then
                Private.DiagUI.Toggle()
            elseif params == "help" or params == "" then
                print("🛰️ Leader Commands:")
                print("  /leader diag   — Toggle telemetry audit overlay")
            end
        end,
        "Leader", "main"
    })

    Command.Event.Attach(Event.System.Update.Begin, UpdateTelemetry, "LeaderUpdate")
    print("🛰️ Leader Telemetry Bridge v1.1 loaded. Type /leader help for commands.")
end

Init()
