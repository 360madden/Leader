-- Leader/Modules/SessionStats.lua
local addon, Private = ...

local SessionStats = {}

local SESSION_STATS_SCHEMA_VERSION = 1

local statsState = {
    packetActive = false,
    lastRenderFrameSeqLogged = 0,
    lastLayoutSyncCountLogged = 0,
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
    LeaderConfig.sessionStats = LeaderConfig.sessionStats or {}

    local stats = LeaderConfig.sessionStats
    if stats.schemaVersion == nil then
        stats.schemaVersion = SESSION_STATS_SCHEMA_VERSION
    end
    if stats.sessionStartedAt == nil then
        stats.sessionStartedAt = 0
    end
    if stats.lastUpdatedAt == nil then
        stats.lastUpdatedAt = 0
    end
    if stats.validPacketCount == nil then
        stats.validPacketCount = 0
    end
    if stats.noPacketTickCount == nil then
        stats.noPacketTickCount = 0
    end
    if stats.packetRecoveryCount == nil then
        stats.packetRecoveryCount = 0
    end
    if stats.zoneChangeCount == nil then
        stats.zoneChangeCount = 0
    end
    if stats.renderIncompleteCount == nil then
        stats.renderIncompleteCount = 0
    end
    if stats.layoutResyncCount == nil then
        stats.layoutResyncCount = 0
    end
    if stats.commandCount == nil then
        stats.commandCount = 0
    end
    if stats.dumpCommandCount == nil then
        stats.dumpCommandCount = 0
    end
    if stats.badgeCommandCount == nil then
        stats.badgeCommandCount = 0
    end
    if stats.diagToggleCount == nil then
        stats.diagToggleCount = 0
    end
    if stats.lastCommand == nil then
        stats.lastCommand = ""
    end
    if stats.lastPlayerTag == nil then
        stats.lastPlayerTag = "____"
    end
    if stats.lastZoneHash == nil then
        stats.lastZoneHash = 0
    end

    return stats
end

function SessionStats.Init()
    local stats = EnsureConfig()
    local now = ReadNow()
    stats.sessionStartedAt = now
    stats.lastUpdatedAt = now
    stats.validPacketCount = 0
    stats.noPacketTickCount = 0
    stats.packetRecoveryCount = 0
    stats.zoneChangeCount = 0
    stats.renderIncompleteCount = 0
    stats.layoutResyncCount = 0
    stats.commandCount = 0
    stats.dumpCommandCount = 0
    stats.badgeCommandCount = 0
    stats.diagToggleCount = 0
    stats.lastCommand = ""
    stats.lastPlayerTag = "____"
    stats.lastZoneHash = 0
    statsState.packetActive = false
    statsState.lastRenderFrameSeqLogged = 0
    statsState.lastLayoutSyncCountLogged = 0
end

function SessionStats.RecordCommand(command, detail)
    local stats = EnsureConfig()
    local commandName = tostring(command or "unknown")
    stats.lastUpdatedAt = ReadNow()
    stats.commandCount = (tonumber(stats.commandCount) or 0) + 1
    stats.lastCommand = detail and (commandName .. ":" .. tostring(detail)) or commandName

    if commandName == "dump" then
        stats.dumpCommandCount = (tonumber(stats.dumpCommandCount) or 0) + 1
    elseif commandName == "badge" then
        stats.badgeCommandCount = (tonumber(stats.badgeCommandCount) or 0) + 1
    elseif commandName == "diag" then
        stats.diagToggleCount = (tonumber(stats.diagToggleCount) or 0) + 1
    end
end

function SessionStats.RecordNoPacket()
    local stats = EnsureConfig()
    stats.lastUpdatedAt = ReadNow()
    stats.noPacketTickCount = (tonumber(stats.noPacketTickCount) or 0) + 1
    statsState.packetActive = false
end

function SessionStats.RecordPacket(packet)
    local stats = EnsureConfig()
    local zoneHash = tonumber(packet and packet.zoneHash) or 0
    local previousZoneHash = tonumber(stats.lastZoneHash) or 0
    local previousValidPacketCount = tonumber(stats.validPacketCount) or 0

    stats.lastUpdatedAt = ReadNow()
    stats.validPacketCount = previousValidPacketCount + 1
    stats.lastPlayerTag = tostring(packet and packet.playerTag or stats.lastPlayerTag or "____")

    if not statsState.packetActive and (previousValidPacketCount > 0 or (tonumber(stats.noPacketTickCount) or 0) > 0) then
        stats.packetRecoveryCount = (tonumber(stats.packetRecoveryCount) or 0) + 1
    end

    if previousZoneHash ~= 0 and zoneHash ~= 0 and previousZoneHash ~= zoneHash then
        stats.zoneChangeCount = (tonumber(stats.zoneChangeCount) or 0) + 1
    end

    stats.lastZoneHash = zoneHash
    statsState.packetActive = true
end

function SessionStats.RecordRender()
    if not (Private.RenderHealth and Private.RenderHealth.GetStatus) then
        return
    end

    local stats = EnsureConfig()
    local render = Private.RenderHealth.GetStatus()
    local frameSeq = tonumber(render.lastFrameSeq) or 0
    local layoutSyncCount = tonumber(render.layoutSyncCount) or 0

    if not render.frameComplete and frameSeq > 0 and statsState.lastRenderFrameSeqLogged ~= frameSeq then
        statsState.lastRenderFrameSeqLogged = frameSeq
        stats.renderIncompleteCount = (tonumber(stats.renderIncompleteCount) or 0) + 1
    end

    if render.lastLayoutChanged and layoutSyncCount > 0 and statsState.lastLayoutSyncCountLogged ~= layoutSyncCount then
        statsState.lastLayoutSyncCountLogged = layoutSyncCount
        stats.layoutResyncCount = (tonumber(stats.layoutResyncCount) or 0) + 1
    end
end

function SessionStats.GetStatus()
    local stats = EnsureConfig()
    return {
        schemaVersion = tonumber(stats.schemaVersion) or SESSION_STATS_SCHEMA_VERSION,
        sessionStartedAt = tonumber(stats.sessionStartedAt) or 0,
        lastUpdatedAt = tonumber(stats.lastUpdatedAt) or 0,
        validPacketCount = tonumber(stats.validPacketCount) or 0,
        noPacketTickCount = tonumber(stats.noPacketTickCount) or 0,
        packetRecoveryCount = tonumber(stats.packetRecoveryCount) or 0,
        zoneChangeCount = tonumber(stats.zoneChangeCount) or 0,
        renderIncompleteCount = tonumber(stats.renderIncompleteCount) or 0,
        layoutResyncCount = tonumber(stats.layoutResyncCount) or 0,
        commandCount = tonumber(stats.commandCount) or 0,
        dumpCommandCount = tonumber(stats.dumpCommandCount) or 0,
        badgeCommandCount = tonumber(stats.badgeCommandCount) or 0,
        diagToggleCount = tonumber(stats.diagToggleCount) or 0,
        lastCommand = tostring(stats.lastCommand or ""),
        lastPlayerTag = tostring(stats.lastPlayerTag or "____"),
        lastZoneHash = tonumber(stats.lastZoneHash) or 0,
    }
end

function SessionStats.PrintStatus()
    local stats = SessionStats.GetStatus()
    print(string.format(
        "🛰️ Leader Stats: packets=%d | no-packet=%d | recoveries=%d | zones=%d | render-issues=%d | layouts=%d | commands=%d | tag=%s",
        stats.validPacketCount,
        stats.noPacketTickCount,
        stats.packetRecoveryCount,
        stats.zoneChangeCount,
        stats.renderIncompleteCount,
        stats.layoutResyncCount,
        stats.commandCount,
        stats.lastPlayerTag))
end

EnsureConfig()

Private.SessionStats = SessionStats
