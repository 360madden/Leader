-- Leader/Modules/SessionTimeline.lua
local addon, Private = ...

local SessionTimeline = {}

local TIMELINE_SCHEMA_VERSION = 1
local DEFAULT_MAX_ENTRIES = 80

local timelineState = {
    noPacketActive = false,
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
    LeaderConfig.timeline = LeaderConfig.timeline or {}

    local timeline = LeaderConfig.timeline
    if timeline.schemaVersion == nil then
        timeline.schemaVersion = TIMELINE_SCHEMA_VERSION
    end
    if timeline.maxEntries == nil then
        timeline.maxEntries = DEFAULT_MAX_ENTRIES
    end
    if type(timeline.entries) ~= "table" then
        timeline.entries = {}
    end
    if timeline.lastSeq == nil then
        timeline.lastSeq = 0
    end
    if timeline.lastZoneHash == nil then
        timeline.lastZoneHash = 0
    end
    if timeline.lastUpdatedAt == nil then
        timeline.lastUpdatedAt = 0
    end

    return timeline
end

local function TrimEntries(timeline)
    local maxEntries = tonumber(timeline.maxEntries) or DEFAULT_MAX_ENTRIES
    if maxEntries < 1 then
        maxEntries = DEFAULT_MAX_ENTRIES
    end

    while #timeline.entries > maxEntries do
        table.remove(timeline.entries, 1)
    end
end

local function AppendEntry(kind, message, details)
    local timeline = EnsureConfig()
    timeline.lastSeq = (tonumber(timeline.lastSeq) or 0) + 1
    timeline.lastUpdatedAt = ReadNow()
    timeline.entries[#timeline.entries + 1] = {
        seq = timeline.lastSeq,
        ts = timeline.lastUpdatedAt,
        kind = tostring(kind or "info"),
        message = tostring(message or ""),
        details = details,
    }
    TrimEntries(timeline)
end

function SessionTimeline.Init()
    local timeline = EnsureConfig()
    timeline.entries = {}
    timeline.lastSeq = 0
    timeline.lastZoneHash = 0
    timeline.lastUpdatedAt = 0
    timelineState.noPacketActive = false
    timelineState.lastRenderFrameSeqLogged = 0
    timelineState.lastLayoutSyncCountLogged = 0
    AppendEntry("session", "timeline initialized")
end

function SessionTimeline.RecordCommand(command, detail)
    AppendEntry("command", tostring(command or "unknown"), detail)
end

function SessionTimeline.RecordNoPacket(nilPacketStreak)
    local streak = tonumber(nilPacketStreak) or 0
    if streak <= 0 then
        return
    end

    if not timelineState.noPacketActive or streak == 1 or streak % 30 == 0 then
        AppendEntry("packet", "no valid telemetry packet", {
            nilPacketStreak = streak,
        })
    end

    timelineState.noPacketActive = true
end

function SessionTimeline.RecordPacket(packet)
    local timeline = EnsureConfig()
    local zoneHash = tonumber(packet and packet.zoneHash) or 0
    local playerTag = tostring(packet and packet.playerTag or "____")
    local previousZoneHash = tonumber(timeline.lastZoneHash) or 0

    if timelineState.noPacketActive then
        AppendEntry("packet", "valid packet stream resumed", {
            playerTag = playerTag,
            zoneHash = zoneHash,
        })
        timelineState.noPacketActive = false
    end

    if previousZoneHash ~= 0 and zoneHash ~= 0 and previousZoneHash ~= zoneHash then
        AppendEntry("zone", "zone hash changed", {
            fromZoneHash = previousZoneHash,
            toZoneHash = zoneHash,
            playerTag = playerTag,
        })
    end

    timeline.lastZoneHash = zoneHash
    timeline.lastUpdatedAt = ReadNow()
end

function SessionTimeline.RecordRender()
    if not (Private.RenderHealth and Private.RenderHealth.GetStatus) then
        return
    end

    local status = Private.RenderHealth.GetStatus()
    local frameSeq = tonumber(status.lastFrameSeq) or 0
    local layoutSyncCount = tonumber(status.layoutSyncCount) or 0

    if not status.frameComplete and frameSeq > 0 and timelineState.lastRenderFrameSeqLogged ~= frameSeq then
        timelineState.lastRenderFrameSeqLogged = frameSeq
        AppendEntry("render", "render frame incomplete", {
            frameSeq = frameSeq,
            pixelWrites = tonumber(status.lastPixelWrites) or 0,
        })
    end

    if status.lastLayoutChanged and layoutSyncCount > 0 and timelineState.lastLayoutSyncCountLogged ~= layoutSyncCount then
        timelineState.lastLayoutSyncCountLogged = layoutSyncCount
        AppendEntry("layout", "renderer layout resynced", {
            layoutSyncCount = layoutSyncCount,
            clientWidth = tonumber(status.clientWidth) or 0,
            pixelSize = tonumber(status.pixelSize) or 0,
        })
    end
end

function SessionTimeline.GetStatus()
    local timeline = EnsureConfig()
    local lastEntry = timeline.entries[#timeline.entries]
    return {
        schemaVersion = tonumber(timeline.schemaVersion) or TIMELINE_SCHEMA_VERSION,
        lastSeq = tonumber(timeline.lastSeq) or 0,
        entryCount = #(timeline.entries or {}),
        lastUpdatedAt = tonumber(timeline.lastUpdatedAt) or 0,
        lastZoneHash = tonumber(timeline.lastZoneHash) or 0,
        lastKind = lastEntry and tostring(lastEntry.kind or "") or "",
        lastMessage = lastEntry and tostring(lastEntry.message or "") or "",
    }
end

function SessionTimeline.PrintStatus()
    local status = SessionTimeline.GetStatus()
    print(string.format(
        "🛰️ Leader Timeline: entries=%d | seq=%d | zone=%d | last=%s | message=%s",
        status.entryCount,
        status.lastSeq,
        status.lastZoneHash,
        status.lastKind ~= "" and status.lastKind or "none",
        status.lastMessage ~= "" and status.lastMessage or "(none)"))
end

EnsureConfig()

Private.SessionTimeline = SessionTimeline
