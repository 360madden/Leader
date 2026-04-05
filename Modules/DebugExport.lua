-- Leader/Modules/DebugExport.lua
local addon, Private = ...

local DebugExport = {}

local DEBUG_EXPORT_SCHEMA_VERSION = 1

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
    LeaderConfig.debugExport = LeaderConfig.debugExport or {}

    local export = LeaderConfig.debugExport
    if export.schemaVersion == nil then
        export.schemaVersion = DEBUG_EXPORT_SCHEMA_VERSION
    end
    if export.lastUpdatedAt == nil then
        export.lastUpdatedAt = 0
    end
    if export.packetValid == nil then
        export.packetValid = false
    end
    if type(export.packet) ~= "table" then
        export.packet = {}
    end
    if type(export.runtime) ~= "table" then
        export.runtime = {}
    end
    if type(export.transition) ~= "table" then
        export.transition = {}
    end
    if type(export.capabilities) ~= "table" then
        export.capabilities = {}
    end
    if type(export.timeline) ~= "table" then
        export.timeline = {}
    end
    if type(export.sessionStats) ~= "table" then
        export.sessionStats = {}
    end
    if type(export.packetAudit) ~= "table" then
        export.packetAudit = {}
    end
    if type(export.dump) ~= "table" then
        export.dump = {}
    end

    return export
end

local function UpdateFromRuntime(export)
    if not (Private.RuntimeStatus and Private.RuntimeStatus.GetStatus) then
        return
    end

    local status = Private.RuntimeStatus.GetStatus()
    export.runtime = {
        lastHeartbeatAt = status.lastHeartbeatAt,
        lastPacketAt = status.lastPacketAt,
        lastPacketAgeSeconds = status.lastPacketAgeSeconds,
        lastSlashCommand = status.lastSlashCommand,
        leaderRegistered = status.leaderRegistered,
        leaderBridgeRegistered = status.leaderBridgeRegistered,
        dumpEnabled = status.dumpEnabled,
        rendererHealthy = status.rendererHealthy,
        nilPacketStreak = status.nilPacketStreak,
        playerTag = status.playerTag,
        zoneHash = status.zoneHash,
        eventCount = status.eventCount,
    }
end

local function UpdateFromTransition(export)
    if not (Private.TransitionState and Private.TransitionState.GetStatus) then
        return
    end

    local status = Private.TransitionState.GetStatus()
    export.transition = {
        active = status.active,
        reason = status.reason,
        startedAt = status.startedAt,
        lastRecoveredAt = status.lastRecoveredAt,
        nilPacketStreak = status.nilPacketStreak,
        lastZoneHash = status.lastZoneHash,
        transitionCount = status.transitionCount,
        recoveryCount = status.recoveryCount,
        historyCount = status.historyCount,
    }
end

local function UpdateFromCapabilities(export)
    if not (Private.CapabilityStatus and Private.CapabilityStatus.GetStatus) then
        return
    end

    local status = Private.CapabilityStatus.GetStatus()
    export.capabilities = {
        primarySlashCommand = status.primarySlashCommand,
        leaderRegistered = status.leaderRegistered,
        leaderBridgeRegistered = status.leaderBridgeRegistered,
        runtimeReady = status.runtimeReady,
        transitionReady = status.transitionReady,
        debugExportReady = status.debugExportReady,
        renderHealthReady = status.renderHealthReady,
        sessionTimelineReady = status.sessionTimelineReady,
        sessionStatsReady = status.sessionStatsReady,
        packetAuditReady = status.packetAuditReady,
        rendererReady = status.rendererReady,
        statusBadgeReady = status.statusBadgeReady,
        diagReady = status.diagReady,
        dumpReady = status.dumpReady,
        historyCount = status.historyCount,
    }
end

local function UpdateFromTimeline(export)
    if not (Private.SessionTimeline and Private.SessionTimeline.GetStatus) then
        return
    end

    local status = Private.SessionTimeline.GetStatus()
    export.timeline = {
        lastSeq = status.lastSeq,
        entryCount = status.entryCount,
        lastUpdatedAt = status.lastUpdatedAt,
        lastZoneHash = status.lastZoneHash,
        lastKind = status.lastKind,
        lastMessage = status.lastMessage,
    }
end

local function UpdateFromSessionStats(export)
    if not (Private.SessionStats and Private.SessionStats.GetStatus) then
        return
    end

    local stats = Private.SessionStats.GetStatus()
    export.sessionStats = {
        sessionStartedAt = stats.sessionStartedAt,
        lastUpdatedAt = stats.lastUpdatedAt,
        validPacketCount = stats.validPacketCount,
        noPacketTickCount = stats.noPacketTickCount,
        packetRecoveryCount = stats.packetRecoveryCount,
        zoneChangeCount = stats.zoneChangeCount,
        renderIncompleteCount = stats.renderIncompleteCount,
        layoutResyncCount = stats.layoutResyncCount,
        commandCount = stats.commandCount,
        dumpCommandCount = stats.dumpCommandCount,
        badgeCommandCount = stats.badgeCommandCount,
        diagToggleCount = stats.diagToggleCount,
        lastCommand = stats.lastCommand,
        lastPlayerTag = stats.lastPlayerTag,
        lastZoneHash = stats.lastZoneHash,
    }
end

local function UpdateFromPacketAudit(export)
    if not (Private.PacketAudit and Private.PacketAudit.GetStatus) then
        return
    end

    local status = Private.PacketAudit.GetStatus()
    export.packetAudit = {
        checkedPacketCount = status.checkedPacketCount,
        validPacketCount = status.validPacketCount,
        invalidPacketCount = status.invalidPacketCount,
        lastCheckedAt = status.lastCheckedAt,
        lastIssueAt = status.lastIssueAt,
        lastIssueKind = status.lastIssueKind,
        lastIssue = status.lastIssue,
        historyCount = status.historyCount,
    }
end

local function UpdateFromDump(export)
    if not (Private.DumpLog and Private.DumpLog.GetStatus) then
        return
    end

    local status = Private.DumpLog.GetStatus()
    export.dump = {
        enabled = status.enabled,
        intervalSeconds = status.intervalSeconds,
        maxEntries = status.maxEntries,
        entryCount = status.entryCount,
        lastSeq = status.lastSeq,
        schemaVersion = status.schemaVersion,
    }
end

function DebugExport.Init()
    EnsureConfig()
end

function DebugExport.Sync(packet)
    local export = EnsureConfig()
    export.lastUpdatedAt = ReadNow()
    export.packetValid = packet and true or false
    export.packet = {
        playerTag = packet and packet.playerTag or "____",
        zoneHash = packet and packet.zoneHash or 0,
        flags = packet and packet.flags or 0,
        coordX = packet and packet.coordX or 0,
        coordY = packet and packet.coordY or 0,
        coordZ = packet and packet.coordZ or 0,
        facing = packet and packet.facing or 0,
        motionSpeed = packet and packet.motionSpeed or 0,
        isCombat = packet and packet.isCombat or false,
        hasTarget = packet and packet.hasTarget or false,
        isMoving = packet and packet.isMoving or false,
        isAlive = packet and packet.isAlive or false,
        isMounted = packet and packet.isMounted or false,
    }

    UpdateFromRuntime(export)
    UpdateFromTransition(export)
    UpdateFromCapabilities(export)
    UpdateFromTimeline(export)
    UpdateFromSessionStats(export)
    UpdateFromPacketAudit(export)
    UpdateFromDump(export)
end

function DebugExport.SyncNoPacket()
    local export = EnsureConfig()
    export.lastUpdatedAt = ReadNow()
    export.packetValid = false

    UpdateFromRuntime(export)
    UpdateFromTransition(export)
    UpdateFromCapabilities(export)
    UpdateFromTimeline(export)
    UpdateFromSessionStats(export)
    UpdateFromPacketAudit(export)
    UpdateFromDump(export)
end

function DebugExport.PrintStatus()
    local export = EnsureConfig()
    local playerTag = export.packet and export.packet.playerTag or "____"
    local zoneHash = export.packet and export.packet.zoneHash or 0
    print(string.format(
        "🛰️ Leader Export: packet=%s | tag=%s | zone=%d | updated=%.2f",
        export.packetValid and "OK" or "WAIT",
        playerTag,
        zoneHash,
        tonumber(export.lastUpdatedAt) or 0))
end

EnsureConfig()

Private.DebugExport = DebugExport
