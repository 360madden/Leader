-- Leader/Modules/Gatherer.lua
local addon, Private = ...
local Gatherer = {}

--[[
-- LEADER TELEMETRY GATHERER v1.2
-- High-frequency RIFT Inspect API polling with robust error handling.
-- Returns a fully populated telemetry packet or nil on failure.
--
-- Notes:
-- RIFT's documented Inspect.Unit.Detail payload exposes coordinates and zone ID,
-- but not direct facing / moving / mounted members. We therefore derive motion
-- heading from successive coordinate samples and infer mount state from sustained
-- travel speed.
--]]

-- Zone hash: computes a stable 0-255 byte from the zone descriptor (name + ID when available).
local function HashZone(zoneStr)
    if not zoneStr then return 0 end
    local s = tostring(zoneStr)
    local h = 0
    for i = 1, #s do
        h = (h * 31 + string.byte(s, i)) % 256
    end
    return h
end

local function ClampByte(value)
    value = tonumber(value) or 0
    if value < 0 then
        return 0
    end

    if value > 255 then
        return 255
    end

    return math.floor(value)
end

local motionState = {
    lastCoordX = nil,
    lastCoordZ = nil,
    lastSampleTime = nil,
    lastHeading = 0,
    speedEma = 0,
    mounted = false,
    mountedHoldUntil = 0,
    lastZoneHash = nil,
}

local motionConfig = {
    minDistance = 0.05,       -- metres per sample; filters idle jitter
    minSpeed = 0.75,          -- metres per second; confirms meaningful movement
    headingAlpha = 0.35,      -- angle smoothing for inferred heading
    speedAlpha = 0.20,        -- EMA smoothing for movement speed
    maxDeltaTime = 0.50,      -- ignore stale samples after loading hitches
    mountedSetSpeed = 8.00,   -- sustained speed indicating mount travel
    mountedClearSpeed = 5.50, -- hysteresis floor before clearing mounted state
    mountedHoldSeconds = 4.00 -- keeps mount state while briefly stationary
}

local TWO_PI = math.pi * 2

local function NormalizeAngle(angle)
    while angle > math.pi do
        angle = angle - TWO_PI
    end

    while angle <= -math.pi do
        angle = angle + TWO_PI
    end

    return angle
end

local function NormalizeProtocolAngle(angle)
    if angle == nil then
        return 0
    end

    while angle < 0 do
        angle = angle + TWO_PI
    end

    while angle > TWO_PI do
        angle = angle - TWO_PI
    end

    return angle
end

local function Atan2(y, x)
    if x > 0 then
        return math.atan(y / x)
    end

    if x < 0 then
        if y >= 0 then
            return math.atan(y / x) + math.pi
        end

        return math.atan(y / x) - math.pi
    end

    if y > 0 then
        return math.pi / 2
    end

    if y < 0 then
        return -math.pi / 2
    end

    return 0
end

local function ReadNow()
    if Inspect and Inspect.Time and Inspect.Time.Real then
        local ok, now = pcall(Inspect.Time.Real)
        now = tonumber(now)
        if ok and now and now > 0 then
            return now
        end
    end

    if Inspect and Inspect.Time and Inspect.Time.Frame then
        local ok, now = pcall(Inspect.Time.Frame)
        now = tonumber(now)
        if ok and now and now > 0 then
            return now
        end
    end

    return 0
end

local function ResolveZoneInfo(unitDetail)
    local zoneId = unitDetail and unitDetail.zone
    if zoneId == nil then
        return 0, "", ""
    end

    local zoneDescriptor = tostring(zoneId)
    local zoneName = ""
    if Inspect and Inspect.Zone and Inspect.Zone.Detail then
        local ok, zoneDetail = pcall(Inspect.Zone.Detail, zoneId)
        if ok and zoneDetail then
            local resolvedZoneName = zoneDetail.name or zoneDetail.id
            if resolvedZoneName ~= nil then
                zoneName = tostring(resolvedZoneName)
                zoneDescriptor = zoneName .. "|" .. tostring(zoneDetail.id or zoneId)
            end
        end
    end

    return HashZone(zoneDescriptor), tostring(zoneId), zoneName
end

local function BuildPlayerTag(unitDetail)
    local raw = nil
    if unitDetail then
        raw = unitDetail.name or unitDetail.id
    end

    raw = string.upper(tostring(raw or "____"))

    local chars = {}
    for i = 1, #raw do
        local ch = string.sub(raw, i, i)
        if string.match(ch, "[A-Z0-9_]") then
            table.insert(chars, ch)
        end

        if #chars == 4 then
            break
        end
    end

    while #chars < 4 do
        table.insert(chars, "_")
    end

    return table.concat(chars)
end

local function ResetMotionState(zoneHash)
    motionState.lastCoordX = nil
    motionState.lastCoordZ = nil
    motionState.lastSampleTime = nil
    motionState.lastHeading = 0
    motionState.speedEma = 0
    motionState.mounted = false
    motionState.mountedHoldUntil = 0
    motionState.lastZoneHash = zoneHash
end

local function InferMotion(coordX, coordZ)
    local now = ReadNow()
    local heading = motionState.lastHeading or 0
    local speed = 0
    local isMoving = false

    if motionState.lastCoordX ~= nil
        and motionState.lastCoordZ ~= nil
        and motionState.lastSampleTime ~= nil
        and now > motionState.lastSampleTime then
        local dx = coordX - motionState.lastCoordX
        local dz = coordZ - motionState.lastCoordZ
        local dt = now - motionState.lastSampleTime
        local distance = math.sqrt(dx * dx + dz * dz)

        if dt > 0 and dt <= motionConfig.maxDeltaTime then
            speed = distance / dt
            if distance >= motionConfig.minDistance and speed >= motionConfig.minSpeed then
                local measuredHeading = Atan2(dx, dz)
                local delta = NormalizeAngle(measuredHeading - heading)
                heading = NormalizeAngle(heading + delta * motionConfig.headingAlpha)
                isMoving = true
            end
        end
    end

    motionState.speedEma = motionState.speedEma
        + (speed - motionState.speedEma) * motionConfig.speedAlpha

    if isMoving and motionState.speedEma >= motionConfig.mountedSetSpeed then
        motionState.mounted = true
        motionState.mountedHoldUntil = now + motionConfig.mountedHoldSeconds
    elseif motionState.mounted then
        local holdExpired = now > 0 and now >= (motionState.mountedHoldUntil or 0)
        if holdExpired and motionState.speedEma <= motionConfig.mountedClearSpeed then
            motionState.mounted = false
            motionState.mountedHoldUntil = 0
        end
    end

    motionState.lastCoordX = coordX
    motionState.lastCoordZ = coordZ
    motionState.lastSampleTime = now
    motionState.lastHeading = heading

    return heading, isMoving, speed, motionState.mounted
end

--- Gathers the current game state for the telemetry bridge.
-- @return Packet table, or nil if the player unit is unavailable.
function Gatherer.GetPacket()
    -- pcall guard: Inspect can error during loading screens / transitions
    local ok, p = pcall(Inspect.Unit.Detail, "player")
    if not ok or not p then return nil end

    local targetSpec = "player.target"
    local targetUnitId = nil
    if Inspect.Unit and Inspect.Unit.Lookup then
        local targetIdOk, targetId = pcall(Inspect.Unit.Lookup, targetSpec)
        if targetIdOk then
            targetUnitId = targetId
        end
    end

    local t = nil
    if targetUnitId then
        local tok, tu = pcall(Inspect.Unit.Detail, targetUnitId)
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
        facing   = 0,
        zoneHash = 0,
        targetID = targetUnitId or (t and t.id) or nil,
        targetName = tostring(t and t.name or ""),
        targetTag = t and BuildPlayerTag(t) or "____",
        playerTag = BuildPlayerTag(p),
        playerName = tostring(p.name or ""),
        playerId = tostring(p.id or ""),
        zoneId = "",
        zoneName = "",
        motionSpeed = 0,
        isCombat = false,
        hasTarget = false,
        isMoving = false,
        isAlive = false,
        isMounted = false,
    }

    packet.zoneHash, packet.zoneId, packet.zoneName = ResolveZoneInfo(p)
    if motionState.lastZoneHash ~= nil and motionState.lastZoneHash ~= packet.zoneHash then
        ResetMotionState(packet.zoneHash)
    elseif motionState.lastZoneHash == nil then
        motionState.lastZoneHash = packet.zoneHash
    end

    local inferredHeading, isMoving, speed, isMounted = InferMotion(packet.coordX, packet.coordZ)
    packet.facing = NormalizeProtocolAngle(inferredHeading)
    packet.motionSpeed = speed

    -- Normalized HP [0-255]
    if p.health and p.healthMax and p.healthMax > 0 then
        packet.playerHP = ClampByte((p.health / p.healthMax) * 255)
    end
    if t and t.health and t.healthMax and t.healthMax > 0 then
        packet.targetHP = ClampByte((t.health / t.healthMax) * 255)
    end

    -- Flags bitfield (Pixel 1, B Channel):
    --   bit 0 = IsCombat
    --   bit 1 = HasTarget
    --   bit 2 = IsMoving
    --   bit 3 = IsAlive
    --   bit 4 = IsMounted
    if p.combat  then packet.flags = packet.flags + 1; packet.isCombat = true end
    if packet.targetID then packet.flags = packet.flags + 2; packet.hasTarget = true end
    if isMoving  then packet.flags = packet.flags + 4; packet.isMoving = true end
    if (p.health or 0) > 0 then packet.flags = packet.flags + 8; packet.isAlive = true end
    if isMounted then packet.flags = packet.flags + 16; packet.isMounted = true end

    return packet
end

Private.Gatherer = Gatherer
