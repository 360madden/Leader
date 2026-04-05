-- Leader/Modules/TargetSnapshot.lua
local addon, Private = ...

local TargetSnapshot = {}

local TARGET_SNAPSHOT_SCHEMA_VERSION = 1

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
    LeaderConfig.targetSnapshot = LeaderConfig.targetSnapshot or {}

    local snapshot = LeaderConfig.targetSnapshot
    if snapshot.schemaVersion == nil then
        snapshot.schemaVersion = TARGET_SNAPSHOT_SCHEMA_VERSION
    end
    if snapshot.lastUpdatedAt == nil then
        snapshot.lastUpdatedAt = 0
    end
    if snapshot.hasTarget == nil then
        snapshot.hasTarget = false
    end
    if snapshot.targetId == nil then
        snapshot.targetId = ""
    end
    if snapshot.targetName == nil then
        snapshot.targetName = ""
    end
    if snapshot.targetTag == nil then
        snapshot.targetTag = "____"
    end
    if snapshot.acquiredAt == nil then
        snapshot.acquiredAt = 0
    end
    if snapshot.lastLostAt == nil then
        snapshot.lastLostAt = 0
    end
    if snapshot.acquireCount == nil then
        snapshot.acquireCount = 0
    end
    if snapshot.lossCount == nil then
        snapshot.lossCount = 0
    end
    if snapshot.switchCount == nil then
        snapshot.switchCount = 0
    end
    if snapshot.lastTargetId == nil then
        snapshot.lastTargetId = ""
    end
    if snapshot.lastTargetName == nil then
        snapshot.lastTargetName = ""
    end
    if snapshot.lastTargetTag == nil then
        snapshot.lastTargetTag = "____"
    end

    return snapshot
end

function TargetSnapshot.Init()
    local snapshot = EnsureConfig()
    snapshot.lastUpdatedAt = 0
    snapshot.hasTarget = false
    snapshot.targetId = ""
    snapshot.targetName = ""
    snapshot.targetTag = "____"
    snapshot.acquiredAt = 0
    snapshot.lastLostAt = 0
    snapshot.acquireCount = 0
    snapshot.lossCount = 0
    snapshot.switchCount = 0
    snapshot.lastTargetId = ""
    snapshot.lastTargetName = ""
    snapshot.lastTargetTag = "____"
end

function TargetSnapshot.Sync(packet)
    local snapshot = EnsureConfig()
    local now = ReadNow()
    local nextTargetId = tostring(packet and packet.targetID or "")
    local nextTargetName = tostring(packet and packet.targetName or "")
    local nextTargetTag = tostring(packet and packet.targetTag or "____")
    local hadTarget = snapshot.hasTarget and true or false
    local previousTargetId = tostring(snapshot.targetId or "")

    snapshot.lastUpdatedAt = now

    if nextTargetId ~= "" then
        if not hadTarget then
            snapshot.acquireCount = (tonumber(snapshot.acquireCount) or 0) + 1
            snapshot.acquiredAt = now
        elseif previousTargetId ~= nextTargetId then
            snapshot.switchCount = (tonumber(snapshot.switchCount) or 0) + 1
            snapshot.acquiredAt = now
        end

        snapshot.hasTarget = true
        snapshot.targetId = nextTargetId
        snapshot.targetName = nextTargetName
        snapshot.targetTag = nextTargetTag
        snapshot.lastTargetId = nextTargetId
        snapshot.lastTargetName = nextTargetName
        snapshot.lastTargetTag = nextTargetTag
        return
    end

    if hadTarget then
        snapshot.lossCount = (tonumber(snapshot.lossCount) or 0) + 1
        snapshot.lastLostAt = now
    end

    snapshot.hasTarget = false
    snapshot.targetId = ""
    snapshot.targetName = ""
    snapshot.targetTag = "____"
end

function TargetSnapshot.GetStatus()
    local snapshot = EnsureConfig()
    return {
        schemaVersion = tonumber(snapshot.schemaVersion) or TARGET_SNAPSHOT_SCHEMA_VERSION,
        lastUpdatedAt = tonumber(snapshot.lastUpdatedAt) or 0,
        hasTarget = snapshot.hasTarget and true or false,
        targetId = tostring(snapshot.targetId or ""),
        targetName = tostring(snapshot.targetName or ""),
        targetTag = tostring(snapshot.targetTag or "____"),
        acquiredAt = tonumber(snapshot.acquiredAt) or 0,
        lastLostAt = tonumber(snapshot.lastLostAt) or 0,
        acquireCount = tonumber(snapshot.acquireCount) or 0,
        lossCount = tonumber(snapshot.lossCount) or 0,
        switchCount = tonumber(snapshot.switchCount) or 0,
        lastTargetId = tostring(snapshot.lastTargetId or ""),
        lastTargetName = tostring(snapshot.lastTargetName or ""),
        lastTargetTag = tostring(snapshot.lastTargetTag or "____"),
    }
end

function TargetSnapshot.PrintStatus()
    local status = TargetSnapshot.GetStatus()
    local targetDisplay = status.hasTarget and ((status.targetName ~= "" and status.targetName or status.targetId) .. " (" .. status.targetTag .. ")") or "(none)"
    print(string.format(
        "🛰️ Leader Target: current=%s | acquires=%d | losses=%d | switches=%d | last=%s",
        targetDisplay,
        status.acquireCount,
        status.lossCount,
        status.switchCount,
        status.lastTargetName ~= "" and status.lastTargetName or (status.lastTargetId ~= "" and status.lastTargetId or "(none)")))
end

EnsureConfig()

Private.TargetSnapshot = TargetSnapshot
