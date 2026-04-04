-- Leader/Modules/Gatherer.lua
local addon, Private = ...
local Gatherer = {}

--[[
-- LEADER TELEMETRY GATHERER v1.1
-- High-frequency RIFT Inspect API polling with robust error handling.
-- Returns a fully populated telemetry packet or nil on failure.
--]]

-- Zone hash: computes a stable 0-255 byte from the zone name string.
local function HashZone(zoneStr)
    if not zoneStr then return 0 end
    local s = tostring(zoneStr)
    local h = 0
    for i = 1, #s do
        h = (h * 31 + string.byte(s, i)) % 256
    end
    return h
end

--- Gathers the current game state for the telemetry bridge.
-- @return Packet table, or nil if the player unit is unavailable.
function Gatherer.GetPacket()
    -- pcall guard: Inspect can error during loading screens / transitions
    local ok, p = pcall(Inspect.Unit.Detail, "player")
    if not ok or not p then return nil end

    local t = nil
    if p.target then
        local tok, tu = pcall(Inspect.Unit.Detail, p.target)
        if tok and tu then t = tu end
    end

    -- Coordinates: RIFT Inspect uses (coordX, coordY, coordZ)
    -- coordY is elevation, coordZ is one horizontal axis
    local packet = {
        playerHP = 0,
        targetHP = 0,
        flags    = 0,
        coordX   = p.coordX   or 0,
        coordY   = p.coordY   or 0,
        coordZ   = p.coordZ   or 0,
        facing   = p.facing   or 0,
        zoneHash = 0,
        targetID = p.target   or nil,
    }

    -- Zone hash — we use a hash over the zone string since it can be nil during transitions
    local zoneOk, zoneVal = pcall(Inspect.System.Zone)
    packet.zoneHash = HashZone(zoneOk and zoneVal or nil)

    -- Normalized HP [0-255]
    if p.health and p.healthMax and p.healthMax > 0 then
        packet.playerHP = math.floor((p.health / p.healthMax) * 255)
    end
    if t and t.health and t.healthMax and t.healthMax > 0 then
        packet.targetHP = math.floor((t.health / t.healthMax) * 255)
    end

    -- Flags bitfield (Pixel 1, B Channel):
    --   bit 0 = IsCombat
    --   bit 1 = HasTarget
    --   bit 2 = IsMoving
    --   bit 3 = IsAlive
    --   bit 4 = IsMounted
    if p.combat  then packet.flags = packet.flags + 1  end
    if p.target  then packet.flags = packet.flags + 2  end
    if p.move    then packet.flags = packet.flags + 4  end
    if not p.dead then packet.flags = packet.flags + 8 end
    if p.mounted then packet.flags = packet.flags + 16 end

    return packet
end

Private.Gatherer = Gatherer
