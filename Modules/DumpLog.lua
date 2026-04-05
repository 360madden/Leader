-- Leader/Modules/DumpLog.lua
local addon, Private = ...

local DumpLog = {}

local DEFAULT_MAX_ENTRIES = 120
local DEFAULT_INTERVAL_SECONDS = 0.25

local dumpState = {
    lastSampleAt = 0,
}

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

local function EnsureConfig()
    LeaderConfig = LeaderConfig or {}
    LeaderConfig.dump = LeaderConfig.dump or {}

    local dump = LeaderConfig.dump
    if dump.enabled == nil then
        dump.enabled = false
    end
    if dump.intervalSeconds == nil then
        dump.intervalSeconds = DEFAULT_INTERVAL_SECONDS
    end
    if dump.maxEntries == nil then
        dump.maxEntries = DEFAULT_MAX_ENTRIES
    end
    if type(dump.entries) ~= "table" then
        dump.entries = {}
    end
    if dump.schemaVersion == nil then
        dump.schemaVersion = 1
    end
    if dump.lastSeq == nil then
        dump.lastSeq = 0
    end

    return dump
end

local function ClonePacket(packet)
    return {
        seq = packet.seq,
        capturedAt = packet.capturedAt,
        playerHP = packet.playerHP,
        targetHP = packet.targetHP,
        flags = packet.flags,
        coordX = packet.coordX,
        coordY = packet.coordY,
        coordZ = packet.coordZ,
        facing = packet.facing,
        zoneHash = packet.zoneHash,
        targetID = packet.targetID,
        playerTag = packet.playerTag,
        motionSpeed = packet.motionSpeed,
        isCombat = packet.isCombat,
        hasTarget = packet.hasTarget,
        isMoving = packet.isMoving,
        isAlive = packet.isAlive,
        isMounted = packet.isMounted,
    }
end

local function AppendEntry(entry)
    local dump = EnsureConfig()
    local entries = dump.entries

    entries[#entries + 1] = entry

    local maxEntries = tonumber(dump.maxEntries) or DEFAULT_MAX_ENTRIES
    if maxEntries < 1 then
        maxEntries = DEFAULT_MAX_ENTRIES
    end

    while #entries > maxEntries do
        table.remove(entries, 1)
    end
end

function DumpLog.Record(packet)
    local dump = EnsureConfig()
    if not dump.enabled or not packet then
        return false
    end

    local now = ReadNow()
    local interval = tonumber(dump.intervalSeconds) or DEFAULT_INTERVAL_SECONDS
    if interval < 0 then
        interval = DEFAULT_INTERVAL_SECONDS
    end

    if dumpState.lastSampleAt > 0 and now > 0 and (now - dumpState.lastSampleAt) < interval then
        return false
    end

    dumpState.lastSampleAt = now
    dump.lastSeq = (tonumber(dump.lastSeq) or 0) + 1

    local entry = ClonePacket(packet)
    entry.seq = dump.lastSeq
    entry.capturedAt = now
    AppendEntry(entry)
    return true
end

function DumpLog.Clear()
    local dump = EnsureConfig()
    dump.entries = {}
    dump.lastSeq = 0
    dumpState.lastSampleAt = 0
end

function DumpLog.SetEnabled(enabled)
    local dump = EnsureConfig()
    dump.enabled = enabled and true or false
end

function DumpLog.Toggle()
    local dump = EnsureConfig()
    dump.enabled = not dump.enabled
    return dump.enabled
end

function DumpLog.SetIntervalSeconds(value)
    local dump = EnsureConfig()
    value = tonumber(value)
    if not value or value <= 0 then
        return false
    end

    dump.intervalSeconds = value
    return true
end

function DumpLog.GetStatus()
    local dump = EnsureConfig()
    return {
        enabled = dump.enabled and true or false,
        intervalSeconds = tonumber(dump.intervalSeconds) or DEFAULT_INTERVAL_SECONDS,
        maxEntries = tonumber(dump.maxEntries) or DEFAULT_MAX_ENTRIES,
        entryCount = #(dump.entries or {}),
        lastSeq = tonumber(dump.lastSeq) or 0,
        schemaVersion = tonumber(dump.schemaVersion) or 1,
    }
end

function DumpLog.PrintStatus()
    local status = DumpLog.GetStatus()
    print(string.format(
        "🛰️ Leader DumpLog: %s | entries=%d/%d | interval=%.2fs | seq=%d | schema=%d",
        status.enabled and "ON" or "OFF",
        status.entryCount,
        status.maxEntries,
        status.intervalSeconds,
        status.lastSeq,
        status.schemaVersion))
end

function DumpLog.PrintHelp(commandPrefix)
    print("🛰️ Leader dump commands:")
    print("  " .. commandPrefix .. " dump status   — show dump-log status")
    print("  " .. commandPrefix .. " dump on       — enable telemetry dumping")
    print("  " .. commandPrefix .. " dump off      — disable telemetry dumping")
    print("  " .. commandPrefix .. " dump clear    — clear recent dump entries")
    print("  " .. commandPrefix .. " dump interval <seconds> — set throttle interval (optional)")
end

EnsureConfig()

Private.DumpLog = DumpLog
