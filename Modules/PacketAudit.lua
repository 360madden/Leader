-- Leader/Modules/PacketAudit.lua
local addon, Private = ...

local PacketAudit = {}

local PACKET_AUDIT_SCHEMA_VERSION = 1
local DEFAULT_MAX_HISTORY = 40
local TWO_PI = math.pi * 2
local TAG_PATTERN = "^[A-Z0-9_][A-Z0-9_][A-Z0-9_][A-Z0-9_]$"

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
    LeaderConfig.packetAudit = LeaderConfig.packetAudit or {}

    local audit = LeaderConfig.packetAudit
    if audit.schemaVersion == nil then
        audit.schemaVersion = PACKET_AUDIT_SCHEMA_VERSION
    end
    if audit.maxHistory == nil then
        audit.maxHistory = DEFAULT_MAX_HISTORY
    end
    if type(audit.history) ~= "table" then
        audit.history = {}
    end
    if audit.checkedPacketCount == nil then
        audit.checkedPacketCount = 0
    end
    if audit.validPacketCount == nil then
        audit.validPacketCount = 0
    end
    if audit.invalidPacketCount == nil then
        audit.invalidPacketCount = 0
    end
    if audit.lastCheckedAt == nil then
        audit.lastCheckedAt = 0
    end
    if audit.lastIssueAt == nil then
        audit.lastIssueAt = 0
    end
    if audit.lastIssue == nil then
        audit.lastIssue = ""
    end
    if audit.lastIssueKind == nil then
        audit.lastIssueKind = ""
    end

    return audit
end

local function TrimHistory(audit)
    local maxHistory = tonumber(audit.maxHistory) or DEFAULT_MAX_HISTORY
    if maxHistory < 1 then
        maxHistory = DEFAULT_MAX_HISTORY
    end

    while #audit.history > maxHistory do
        table.remove(audit.history, 1)
    end
end

local function AppendIssue(audit, kind, message, details)
    local now = ReadNow()
    audit.history[#audit.history + 1] = {
        ts = now,
        kind = tostring(kind or "issue"),
        message = tostring(message or ""),
        details = details,
    }
    audit.lastIssueAt = now
    audit.lastIssue = tostring(message or "")
    audit.lastIssueKind = tostring(kind or "issue")
    TrimHistory(audit)
end

local function IsFiniteNumber(value)
    return type(value) == "number" and value == value and value ~= math.huge and value ~= -math.huge
end

local function IsByte(value)
    return IsFiniteNumber(value) and value >= 0 and value <= 255 and math.floor(value) == value
end

local function ValidatePacketShape(packet)
    if type(packet) ~= "table" then
        return false, "shape", "packet missing or not a table"
    end

    if not IsByte(packet.playerHP) then
        return false, "packet", "playerHP outside byte range"
    end

    if not IsByte(packet.targetHP) then
        return false, "packet", "targetHP outside byte range"
    end

    if not IsByte(packet.flags) then
        return false, "packet", "flags outside byte range"
    end

    if not IsByte(packet.zoneHash) then
        return false, "packet", "zoneHash outside byte range"
    end

    if not IsFiniteNumber(packet.coordX) or not IsFiniteNumber(packet.coordY) or not IsFiniteNumber(packet.coordZ) then
        return false, "packet", "coordinate field is not finite"
    end

    if not IsFiniteNumber(packet.facing) or packet.facing < 0 or packet.facing > TWO_PI then
        return false, "packet", "facing outside protocol range"
    end

    if not IsFiniteNumber(packet.motionSpeed) or packet.motionSpeed < 0 then
        return false, "packet", "motionSpeed invalid"
    end

    local playerTag = tostring(packet.playerTag or "")
    if not string.match(playerTag, TAG_PATTERN) then
        return false, "packet", "playerTag invalid"
    end

    return true, "ok", ""
end

local function ValidateEncodedPacket(packet)
    if not Private.Encoder then
        return true, "ok", ""
    end

    local channels = {}
    channels[1], channels[2], channels[3] = Private.Encoder.PackState(packet.playerHP, packet.targetHP, packet.flags)
    channels[4], channels[5], channels[6] = Private.Encoder.PackCoord(packet.coordX)
    channels[7], channels[8], channels[9] = Private.Encoder.PackCoord(packet.coordY)
    channels[10], channels[11], channels[12] = Private.Encoder.PackCoord(packet.coordZ)
    channels[13], channels[14], channels[15] = Private.Encoder.PackHeading(packet.facing, packet.zoneHash)
    channels[16], channels[17], channels[18] = Private.Encoder.PackPlayerTag(packet.playerTag)

    for index, channel in ipairs(channels) do
        if type(channel) ~= "number" or channel ~= channel or channel < 0 or channel > 1 then
            return false, "encode", "encoded channel outside normalized range", {
                channelIndex = index,
                value = channel,
            }
        end
    end

    return true, "ok", ""
end

function PacketAudit.Init()
    EnsureConfig()
end

function PacketAudit.Sync(packet)
    local audit = EnsureConfig()
    audit.checkedPacketCount = (tonumber(audit.checkedPacketCount) or 0) + 1
    audit.lastCheckedAt = ReadNow()

    local okPacket, packetKind, packetMessage = ValidatePacketShape(packet)
    if not okPacket then
        audit.invalidPacketCount = (tonumber(audit.invalidPacketCount) or 0) + 1
        AppendIssue(audit, packetKind, packetMessage, {
            playerTag = packet and packet.playerTag or nil,
            zoneHash = packet and packet.zoneHash or nil,
        })
        return false
    end

    local okEncoded, encodeKind, encodeMessage, encodeDetails = ValidateEncodedPacket(packet)
    if not okEncoded then
        audit.invalidPacketCount = (tonumber(audit.invalidPacketCount) or 0) + 1
        AppendIssue(audit, encodeKind, encodeMessage, encodeDetails)
        return false
    end

    audit.validPacketCount = (tonumber(audit.validPacketCount) or 0) + 1
    return true
end

function PacketAudit.GetStatus()
    local audit = EnsureConfig()
    return {
        schemaVersion = tonumber(audit.schemaVersion) or PACKET_AUDIT_SCHEMA_VERSION,
        checkedPacketCount = tonumber(audit.checkedPacketCount) or 0,
        validPacketCount = tonumber(audit.validPacketCount) or 0,
        invalidPacketCount = tonumber(audit.invalidPacketCount) or 0,
        lastCheckedAt = tonumber(audit.lastCheckedAt) or 0,
        lastIssueAt = tonumber(audit.lastIssueAt) or 0,
        lastIssueKind = tostring(audit.lastIssueKind or ""),
        lastIssue = tostring(audit.lastIssue or ""),
        historyCount = #(audit.history or {}),
    }
end

function PacketAudit.PrintStatus()
    local status = PacketAudit.GetStatus()
    print(string.format(
        "🛰️ Leader PacketAudit: checked=%d | valid=%d | invalid=%d | last-issue=%s | history=%d",
        status.checkedPacketCount,
        status.validPacketCount,
        status.invalidPacketCount,
        status.lastIssue ~= "" and status.lastIssue or "none",
        status.historyCount))
end

EnsureConfig()

Private.PacketAudit = PacketAudit
