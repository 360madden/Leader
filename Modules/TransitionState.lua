-- Leader/Modules/TransitionState.lua
local addon, Private = ...

local TransitionState = {}

local TRANSITION_SCHEMA_VERSION = 1
local DEFAULT_MAX_HISTORY = 20
local NO_PACKET_ACTIVATION_STREAK = 5

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
    LeaderConfig.transition = LeaderConfig.transition or {}

    local transition = LeaderConfig.transition
    if transition.schemaVersion == nil then
        transition.schemaVersion = TRANSITION_SCHEMA_VERSION
    end
    if transition.active == nil then
        transition.active = false
    end
    if transition.reason == nil then
        transition.reason = "idle"
    end
    if transition.startedAt == nil then
        transition.startedAt = 0
    end
    if transition.lastRecoveredAt == nil then
        transition.lastRecoveredAt = 0
    end
    if transition.nilPacketStreak == nil then
        transition.nilPacketStreak = 0
    end
    if transition.lastZoneHash == nil then
        transition.lastZoneHash = 0
    end
    if transition.transitionCount == nil then
        transition.transitionCount = 0
    end
    if transition.recoveryCount == nil then
        transition.recoveryCount = 0
    end
    if transition.maxHistory == nil then
        transition.maxHistory = DEFAULT_MAX_HISTORY
    end
    if type(transition.history) ~= "table" then
        transition.history = {}
    end

    return transition
end

local function TrimHistory(transition)
    local maxHistory = tonumber(transition.maxHistory) or DEFAULT_MAX_HISTORY
    if maxHistory < 1 then
        maxHistory = DEFAULT_MAX_HISTORY
    end

    while #transition.history > maxHistory do
        table.remove(transition.history, 1)
    end
end

local function AppendHistory(transition, kind, message, details)
    transition.history[#transition.history + 1] = {
        ts = ReadNow(),
        kind = tostring(kind or "info"),
        message = tostring(message or ""),
        details = details,
    }
    TrimHistory(transition)
end

local function Activate(transition, reason, details)
    if transition.active and transition.reason == reason then
        return
    end

    transition.active = true
    transition.reason = tostring(reason or "unknown")
    transition.startedAt = ReadNow()
    transition.transitionCount = (tonumber(transition.transitionCount) or 0) + 1
    AppendHistory(transition, "transition", "transition active: " .. transition.reason, details)
end

local function Recover(transition, message, details)
    if not transition.active then
        return
    end

    transition.active = false
    transition.lastRecoveredAt = ReadNow()
    transition.recoveryCount = (tonumber(transition.recoveryCount) or 0) + 1
    AppendHistory(transition, "recovery", tostring(message or "transition recovered"), details)
    transition.reason = "idle"
    transition.startedAt = 0
end

function TransitionState.Init()
    EnsureConfig()
end

function TransitionState.RecordNoPacket()
    local transition = EnsureConfig()
    transition.nilPacketStreak = (tonumber(transition.nilPacketStreak) or 0) + 1

    if transition.nilPacketStreak >= NO_PACKET_ACTIVATION_STREAK then
        Activate(transition, "no_packet", {
            nilPacketStreak = transition.nilPacketStreak,
        })
    end
end

function TransitionState.RecordPacket(packet)
    local transition = EnsureConfig()
    local previousZoneHash = tonumber(transition.lastZoneHash) or 0
    local currentZoneHash = tonumber(packet and packet.zoneHash) or 0

    if transition.active and transition.reason == "no_packet" then
        Recover(transition, "valid packet stream resumed", {
            nilPacketStreak = transition.nilPacketStreak,
        })
    end

    if previousZoneHash ~= 0 and currentZoneHash ~= 0 and previousZoneHash ~= currentZoneHash then
        Activate(transition, "zone_change", {
            fromZoneHash = previousZoneHash,
            toZoneHash = currentZoneHash,
        })
    elseif transition.active and transition.reason == "zone_change" then
        Recover(transition, "zone change stabilized", {
            zoneHash = currentZoneHash,
        })
    end

    transition.lastZoneHash = currentZoneHash
    transition.nilPacketStreak = 0
end

function TransitionState.GetStatus()
    local transition = EnsureConfig()
    return {
        schemaVersion = tonumber(transition.schemaVersion) or TRANSITION_SCHEMA_VERSION,
        active = transition.active and true or false,
        reason = tostring(transition.reason or "idle"),
        startedAt = tonumber(transition.startedAt) or 0,
        lastRecoveredAt = tonumber(transition.lastRecoveredAt) or 0,
        nilPacketStreak = tonumber(transition.nilPacketStreak) or 0,
        lastZoneHash = tonumber(transition.lastZoneHash) or 0,
        transitionCount = tonumber(transition.transitionCount) or 0,
        recoveryCount = tonumber(transition.recoveryCount) or 0,
        historyCount = #(transition.history or {}),
    }
end

function TransitionState.PrintStatus()
    local status = TransitionState.GetStatus()
    print(string.format(
        "🛰️ Leader Transition: active=%s | reason=%s | nil-streak=%d | zone=%d | transitions=%d | recoveries=%d | history=%d",
        status.active and "YES" or "no",
        status.reason,
        status.nilPacketStreak,
        status.lastZoneHash,
        status.transitionCount,
        status.recoveryCount,
        status.historyCount))
end

EnsureConfig()

Private.TransitionState = TransitionState
