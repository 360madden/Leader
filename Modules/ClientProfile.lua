-- Leader/Modules/ClientProfile.lua
local addon, Private = ...

local ClientProfile = {}

local CLIENT_PROFILE_SCHEMA_VERSION = 1

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
    LeaderConfig.clientProfile = LeaderConfig.clientProfile or {}

    local profile = LeaderConfig.clientProfile
    if profile.schemaVersion == nil then
        profile.schemaVersion = CLIENT_PROFILE_SCHEMA_VERSION
    end
    if profile.addonName == nil then
        profile.addonName = ""
    end
    if profile.addonVersion == nil then
        profile.addonVersion = ""
    end
    if profile.firstSeenAt == nil then
        profile.firstSeenAt = 0
    end
    if profile.lastSeenAt == nil then
        profile.lastSeenAt = 0
    end
    if profile.sessionStartedAt == nil then
        profile.sessionStartedAt = 0
    end
    if profile.primarySlashCommand == nil then
        profile.primarySlashCommand = ""
    end
    if profile.leaderRegistered == nil then
        profile.leaderRegistered = false
    end
    if profile.leaderBridgeRegistered == nil then
        profile.leaderBridgeRegistered = false
    end
    if profile.playerTag == nil then
        profile.playerTag = "____"
    end
    if profile.playerName == nil then
        profile.playerName = ""
    end
    if profile.playerId == nil then
        profile.playerId = ""
    end
    if profile.zoneHash == nil then
        profile.zoneHash = 0
    end
    if profile.zoneId == nil then
        profile.zoneId = ""
    end
    if profile.zoneName == nil then
        profile.zoneName = ""
    end

    return profile
end

function ClientProfile.Init()
    local profile = EnsureConfig()
    local now = ReadNow()
    profile.addonName = (Name and Name.English) or tostring(addon or "Leader")
    profile.addonVersion = tostring(Version or "unknown")
    profile.sessionStartedAt = now
    if (tonumber(profile.firstSeenAt) or 0) == 0 then
        profile.firstSeenAt = now
    end
end

function ClientProfile.RecordSlashRegistration(primarySlashCommand, leaderRegistered, leaderBridgeRegistered)
    local profile = EnsureConfig()
    profile.primarySlashCommand = tostring(primarySlashCommand or "")
    profile.leaderRegistered = leaderRegistered and true or false
    profile.leaderBridgeRegistered = leaderBridgeRegistered and true or false
end

function ClientProfile.Sync(packet)
    local profile = EnsureConfig()
    local now = ReadNow()

    profile.lastSeenAt = now
    profile.playerTag = tostring(packet and packet.playerTag or profile.playerTag or "____")
    profile.playerName = tostring(packet and packet.playerName or profile.playerName or "")
    profile.playerId = tostring(packet and packet.playerId or profile.playerId or "")
    profile.zoneHash = tonumber(packet and packet.zoneHash) or profile.zoneHash or 0
    profile.zoneId = tostring(packet and packet.zoneId or profile.zoneId or "")
    profile.zoneName = tostring(packet and packet.zoneName or profile.zoneName or "")
end

function ClientProfile.GetStatus()
    local profile = EnsureConfig()
    return {
        schemaVersion = tonumber(profile.schemaVersion) or CLIENT_PROFILE_SCHEMA_VERSION,
        addonName = tostring(profile.addonName or ""),
        addonVersion = tostring(profile.addonVersion or ""),
        firstSeenAt = tonumber(profile.firstSeenAt) or 0,
        lastSeenAt = tonumber(profile.lastSeenAt) or 0,
        sessionStartedAt = tonumber(profile.sessionStartedAt) or 0,
        primarySlashCommand = tostring(profile.primarySlashCommand or ""),
        leaderRegistered = profile.leaderRegistered and true or false,
        leaderBridgeRegistered = profile.leaderBridgeRegistered and true or false,
        playerTag = tostring(profile.playerTag or "____"),
        playerName = tostring(profile.playerName or ""),
        playerId = tostring(profile.playerId or ""),
        zoneHash = tonumber(profile.zoneHash) or 0,
        zoneId = tostring(profile.zoneId or ""),
        zoneName = tostring(profile.zoneName or ""),
    }
end

function ClientProfile.PrintStatus()
    local status = ClientProfile.GetStatus()
    local slashDisplay = status.primarySlashCommand ~= "" and ("/" .. status.primarySlashCommand) or "(none)"
    local zoneDisplay = status.zoneName ~= "" and status.zoneName or (status.zoneId ~= "" and status.zoneId or "(unknown)")
    local nameDisplay = status.playerName ~= "" and status.playerName or status.playerTag
    print(string.format(
        "🛰️ Leader Profile: %s (%s) | zone=%s [%d] | slash=%s | version=%s",
        nameDisplay,
        status.playerTag,
        zoneDisplay,
        status.zoneHash,
        slashDisplay,
        status.addonVersion))
end

EnsureConfig()

Private.ClientProfile = ClientProfile
