-- Leader/Modules/RuntimeStatus.lua
local addon, Private = ...

local RuntimeStatus = {}

local DEFAULT_MAX_EVENTS = 60
local STATUS_SCHEMA_VERSION = 1

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
    LeaderConfig.runtime = LeaderConfig.runtime or {}

    local runtime = LeaderConfig.runtime
    if runtime.schemaVersion == nil then
        runtime.schemaVersion = STATUS_SCHEMA_VERSION
    end
    if runtime.maxEvents == nil then
        runtime.maxEvents = DEFAULT_MAX_EVENTS
    end
    if type(runtime.events) ~= "table" then
        runtime.events = {}
    end
    if runtime.nilPacketStreak == nil then
        runtime.nilPacketStreak = 0
    end
    if runtime.sessionStartedAt == nil then
        runtime.sessionStartedAt = 0
    end
    if runtime.lastHeartbeatAt == nil then
        runtime.lastHeartbeatAt = 0
    end
    if runtime.lastPacketAt == nil then
        runtime.lastPacketAt = 0
    end
    if runtime.lastPacketAgeSeconds == nil then
        runtime.lastPacketAgeSeconds = 0
    end
    if runtime.lastEventAt == nil then
        runtime.lastEventAt = 0
    end
    if runtime.lastSlashCommand == nil then
        runtime.lastSlashCommand = ""
    end
    if runtime.dumpEnabled == nil then
        runtime.dumpEnabled = false
    end
    if runtime.rendererHealthy == nil then
        runtime.rendererHealthy = false
    end
    if runtime.packetValid == nil then
        runtime.packetValid = false
    end
    if runtime.playerTag == nil then
        runtime.playerTag = "____"
    end
    if runtime.zoneHash == nil then
        runtime.zoneHash = 0
    end

    return runtime
end

local function TrimEvents(runtime)
    local maxEvents = tonumber(runtime.maxEvents) or DEFAULT_MAX_EVENTS
    if maxEvents < 1 then
        maxEvents = DEFAULT_MAX_EVENTS
    end

    while #runtime.events > maxEvents do
        table.remove(runtime.events, 1)
    end
end

local function AppendEvent(kind, message, details)
    local runtime = EnsureConfig()
    local now = ReadNow()

    runtime.events[#runtime.events + 1] = {
        ts = now,
        kind = tostring(kind or "info"),
        message = tostring(message or ""),
        details = details,
    }
    runtime.lastEventAt = now
    TrimEvents(runtime)
end

function RuntimeStatus.InitSession()
    local runtime = EnsureConfig()
    local now = ReadNow()

    if runtime.sessionStartedAt == 0 then
        runtime.sessionStartedAt = now
    end

    runtime.lastHeartbeatAt = now
    AppendEvent("session", "runtime session initialized")
end

function RuntimeStatus.RecordSlashRegistration(primarySlashCommand, leaderRegistered, leaderBridgeRegistered)
    local runtime = EnsureConfig()
    runtime.lastSlashCommand = tostring(primarySlashCommand or "")
    runtime.leaderRegistered = leaderRegistered and true or false
    runtime.leaderBridgeRegistered = leaderBridgeRegistered and true or false

    local message
    if runtime.lastSlashCommand ~= "" then
        message = "slash command ready: /" .. runtime.lastSlashCommand
    else
        message = "no slash command registered"
    end

    AppendEvent("slash", message)
end

function RuntimeStatus.RecordCommand(command, detail)
    AppendEvent("command", tostring(command or "unknown"), detail)
end

function RuntimeStatus.RecordPacket(packet, dumpEnabled)
    local runtime = EnsureConfig()
    local now = ReadNow()

    runtime.lastHeartbeatAt = now
    runtime.lastPacketAt = now
    runtime.lastPacketAgeSeconds = 0
    runtime.packetValid = true
    runtime.rendererHealthy = true
    runtime.dumpEnabled = dumpEnabled and true or false
    runtime.nilPacketStreak = 0
    runtime.playerTag = tostring(packet and packet.playerTag or runtime.playerTag or "____")
    runtime.zoneHash = tonumber(packet and packet.zoneHash) or runtime.zoneHash or 0
end

function RuntimeStatus.RecordNoPacket(rendererHealthy, dumpEnabled)
    local runtime = EnsureConfig()
    local now = ReadNow()

    runtime.lastHeartbeatAt = now
    runtime.packetValid = false
    runtime.rendererHealthy = rendererHealthy and true or false
    runtime.dumpEnabled = dumpEnabled and true or false
    runtime.nilPacketStreak = (tonumber(runtime.nilPacketStreak) or 0) + 1

    local lastPacketAt = tonumber(runtime.lastPacketAt) or 0
    if lastPacketAt > 0 and now > 0 then
        runtime.lastPacketAgeSeconds = math.max(0, now - lastPacketAt)
    end

    if runtime.nilPacketStreak == 1 or runtime.nilPacketStreak % 30 == 0 then
        AppendEvent("packet", "no valid telemetry packet", {
            nilPacketStreak = runtime.nilPacketStreak,
            rendererHealthy = runtime.rendererHealthy,
        })
    end
end

function RuntimeStatus.GetStatus()
    local runtime = EnsureConfig()
    return {
        schemaVersion = tonumber(runtime.schemaVersion) or STATUS_SCHEMA_VERSION,
        sessionStartedAt = tonumber(runtime.sessionStartedAt) or 0,
        lastHeartbeatAt = tonumber(runtime.lastHeartbeatAt) or 0,
        lastPacketAt = tonumber(runtime.lastPacketAt) or 0,
        lastPacketAgeSeconds = tonumber(runtime.lastPacketAgeSeconds) or 0,
        lastEventAt = tonumber(runtime.lastEventAt) or 0,
        lastSlashCommand = tostring(runtime.lastSlashCommand or ""),
        leaderRegistered = runtime.leaderRegistered and true or false,
        leaderBridgeRegistered = runtime.leaderBridgeRegistered and true or false,
        dumpEnabled = runtime.dumpEnabled and true or false,
        rendererHealthy = runtime.rendererHealthy and true or false,
        packetValid = runtime.packetValid and true or false,
        nilPacketStreak = tonumber(runtime.nilPacketStreak) or 0,
        playerTag = tostring(runtime.playerTag or "____"),
        zoneHash = tonumber(runtime.zoneHash) or 0,
        eventCount = #(runtime.events or {}),
    }
end

function RuntimeStatus.PrintStatus()
    local status = RuntimeStatus.GetStatus()
    local slashDisplay = status.lastSlashCommand ~= "" and ("/" .. status.lastSlashCommand) or "(none)"
    print(string.format(
        "🛰️ Leader Runtime: packet=%s | renderer=%s | dump=%s | nil-streak=%d | tag=%s | zone=%d | slash=%s | events=%d",
        status.packetValid and "OK" or "WAIT",
        status.rendererHealthy and "OK" or "WAIT",
        status.dumpEnabled and "ON" or "OFF",
        status.nilPacketStreak,
        status.playerTag,
        status.zoneHash,
        slashDisplay,
        status.eventCount))
end

EnsureConfig()

Private.RuntimeStatus = RuntimeStatus
