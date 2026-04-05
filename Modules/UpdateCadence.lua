-- Leader/Modules/UpdateCadence.lua
local addon, Private = ...

local UpdateCadence = {}

local UPDATE_CADENCE_SCHEMA_VERSION = 1
local LONG_FRAME_THRESHOLD_SECONDS = 0.10

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
    LeaderConfig.updateCadence = LeaderConfig.updateCadence or {}

    local cadence = LeaderConfig.updateCadence
    if cadence.schemaVersion == nil then
        cadence.schemaVersion = UPDATE_CADENCE_SCHEMA_VERSION
    end
    if cadence.sessionStartedAt == nil then
        cadence.sessionStartedAt = 0
    end
    if cadence.lastUpdatedAt == nil then
        cadence.lastUpdatedAt = 0
    end
    if cadence.lastTelemetryAt == nil then
        cadence.lastTelemetryAt = 0
    end
    if cadence.frameCount == nil then
        cadence.frameCount = 0
    end
    if cadence.telemetryTickCount == nil then
        cadence.telemetryTickCount = 0
    end
    if cadence.throttledFrameCount == nil then
        cadence.throttledFrameCount = 0
    end
    if cadence.longFrameCount == nil then
        cadence.longFrameCount = 0
    end
    if cadence.maxDeltaSeconds == nil then
        cadence.maxDeltaSeconds = 0
    end
    if cadence.totalDeltaSeconds == nil then
        cadence.totalDeltaSeconds = 0
    end
    if cadence.averageDeltaSeconds == nil then
        cadence.averageDeltaSeconds = 0
    end
    if cadence.lastDeltaSeconds == nil then
        cadence.lastDeltaSeconds = 0
    end

    return cadence
end

function UpdateCadence.Init()
    local cadence = EnsureConfig()
    local now = ReadNow()
    cadence.sessionStartedAt = now
    cadence.lastUpdatedAt = now
    cadence.lastTelemetryAt = 0
    cadence.frameCount = 0
    cadence.telemetryTickCount = 0
    cadence.throttledFrameCount = 0
    cadence.longFrameCount = 0
    cadence.maxDeltaSeconds = 0
    cadence.totalDeltaSeconds = 0
    cadence.averageDeltaSeconds = 0
    cadence.lastDeltaSeconds = 0
end

function UpdateCadence.RecordFrame(deltaSeconds, emittedTelemetry)
    local cadence = EnsureConfig()
    local delta = tonumber(deltaSeconds) or 0
    if delta < 0 then
        delta = 0
    end

    cadence.lastUpdatedAt = ReadNow()
    cadence.lastDeltaSeconds = delta
    cadence.frameCount = (tonumber(cadence.frameCount) or 0) + 1
    cadence.totalDeltaSeconds = (tonumber(cadence.totalDeltaSeconds) or 0) + delta

    if delta > (tonumber(cadence.maxDeltaSeconds) or 0) then
        cadence.maxDeltaSeconds = delta
    end

    if delta >= LONG_FRAME_THRESHOLD_SECONDS then
        cadence.longFrameCount = (tonumber(cadence.longFrameCount) or 0) + 1
    end

    local frameCount = tonumber(cadence.frameCount) or 0
    if frameCount > 0 then
        cadence.averageDeltaSeconds = (tonumber(cadence.totalDeltaSeconds) or 0) / frameCount
    end

    if emittedTelemetry then
        cadence.telemetryTickCount = (tonumber(cadence.telemetryTickCount) or 0) + 1
        cadence.lastTelemetryAt = cadence.lastUpdatedAt
    else
        cadence.throttledFrameCount = (tonumber(cadence.throttledFrameCount) or 0) + 1
    end
end

function UpdateCadence.GetStatus()
    local cadence = EnsureConfig()
    return {
        schemaVersion = tonumber(cadence.schemaVersion) or UPDATE_CADENCE_SCHEMA_VERSION,
        sessionStartedAt = tonumber(cadence.sessionStartedAt) or 0,
        lastUpdatedAt = tonumber(cadence.lastUpdatedAt) or 0,
        lastTelemetryAt = tonumber(cadence.lastTelemetryAt) or 0,
        frameCount = tonumber(cadence.frameCount) or 0,
        telemetryTickCount = tonumber(cadence.telemetryTickCount) or 0,
        throttledFrameCount = tonumber(cadence.throttledFrameCount) or 0,
        longFrameCount = tonumber(cadence.longFrameCount) or 0,
        maxDeltaSeconds = tonumber(cadence.maxDeltaSeconds) or 0,
        averageDeltaSeconds = tonumber(cadence.averageDeltaSeconds) or 0,
        lastDeltaSeconds = tonumber(cadence.lastDeltaSeconds) or 0,
    }
end

function UpdateCadence.PrintStatus()
    local status = UpdateCadence.GetStatus()
    print(string.format(
        "🛰️ Leader Cadence: frames=%d | ticks=%d | throttled=%d | long=%d | avg=%.4fs | max=%.4fs | last=%.4fs",
        status.frameCount,
        status.telemetryTickCount,
        status.throttledFrameCount,
        status.longFrameCount,
        status.averageDeltaSeconds,
        status.maxDeltaSeconds,
        status.lastDeltaSeconds))
end

EnsureConfig()

Private.UpdateCadence = UpdateCadence
