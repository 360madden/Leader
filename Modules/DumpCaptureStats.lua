-- Leader/Modules/DumpCaptureStats.lua
local addon, Private = ...

local DumpCaptureStats = {}

local DUMP_CAPTURE_SCHEMA_VERSION = 1

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
    LeaderConfig.dumpCaptureStats = LeaderConfig.dumpCaptureStats or {}

    local stats = LeaderConfig.dumpCaptureStats
    if stats.schemaVersion == nil then
        stats.schemaVersion = DUMP_CAPTURE_SCHEMA_VERSION
    end
    if stats.sessionStartedAt == nil then
        stats.sessionStartedAt = 0
    end
    if stats.lastUpdatedAt == nil then
        stats.lastUpdatedAt = 0
    end
    if stats.lastRecordedAt == nil then
        stats.lastRecordedAt = 0
    end
    if stats.dumpEnabled == nil then
        stats.dumpEnabled = false
    end
    if stats.attemptedRecordCount == nil then
        stats.attemptedRecordCount = 0
    end
    if stats.recordedEntryCount == nil then
        stats.recordedEntryCount = 0
    end
    if stats.throttledEntryCount == nil then
        stats.throttledEntryCount = 0
    end
    if stats.lastSeq == nil then
        stats.lastSeq = 0
    end
    if stats.entryCount == nil then
        stats.entryCount = 0
    end

    return stats
end

function DumpCaptureStats.Init()
    local stats = EnsureConfig()
    local now = ReadNow()
    stats.sessionStartedAt = now
    stats.lastUpdatedAt = now
    stats.lastRecordedAt = 0
    stats.dumpEnabled = false
    stats.attemptedRecordCount = 0
    stats.recordedEntryCount = 0
    stats.throttledEntryCount = 0
    stats.lastSeq = 0
    stats.entryCount = 0
end

function DumpCaptureStats.Sync(recorded)
    local stats = EnsureConfig()
    local now = ReadNow()
    stats.lastUpdatedAt = now

    if Private.DumpLog and Private.DumpLog.GetStatus then
        local dumpStatus = Private.DumpLog.GetStatus()
        stats.dumpEnabled = dumpStatus.enabled and true or false
        stats.lastSeq = tonumber(dumpStatus.lastSeq) or 0
        stats.entryCount = tonumber(dumpStatus.entryCount) or 0
    end

    if not stats.dumpEnabled then
        return
    end

    stats.attemptedRecordCount = (tonumber(stats.attemptedRecordCount) or 0) + 1

    if recorded then
        stats.recordedEntryCount = (tonumber(stats.recordedEntryCount) or 0) + 1
        stats.lastRecordedAt = now
    else
        stats.throttledEntryCount = (tonumber(stats.throttledEntryCount) or 0) + 1
    end
end

function DumpCaptureStats.GetStatus()
    local stats = EnsureConfig()
    return {
        schemaVersion = tonumber(stats.schemaVersion) or DUMP_CAPTURE_SCHEMA_VERSION,
        sessionStartedAt = tonumber(stats.sessionStartedAt) or 0,
        lastUpdatedAt = tonumber(stats.lastUpdatedAt) or 0,
        lastRecordedAt = tonumber(stats.lastRecordedAt) or 0,
        dumpEnabled = stats.dumpEnabled and true or false,
        attemptedRecordCount = tonumber(stats.attemptedRecordCount) or 0,
        recordedEntryCount = tonumber(stats.recordedEntryCount) or 0,
        throttledEntryCount = tonumber(stats.throttledEntryCount) or 0,
        lastSeq = tonumber(stats.lastSeq) or 0,
        entryCount = tonumber(stats.entryCount) or 0,
    }
end

function DumpCaptureStats.PrintStatus()
    local status = DumpCaptureStats.GetStatus()
    print(string.format(
        "🛰️ Leader DumpCapture: enabled=%s | attempts=%d | written=%d | throttled=%d | entries=%d | seq=%d",
        status.dumpEnabled and "ON" or "OFF",
        status.attemptedRecordCount,
        status.recordedEntryCount,
        status.throttledEntryCount,
        status.entryCount,
        status.lastSeq))
end

EnsureConfig()

Private.DumpCaptureStats = DumpCaptureStats
