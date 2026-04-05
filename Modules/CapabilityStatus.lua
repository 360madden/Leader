-- Leader/Modules/CapabilityStatus.lua
local addon, Private = ...

local CapabilityStatus = {}

local CAPABILITY_SCHEMA_VERSION = 1
local DEFAULT_MAX_HISTORY = 24
local moduleFieldMap = {
    runtime = "runtimeReady",
    transition = "transitionReady",
    debugexport = "debugExportReady",
    renderhealth = "renderHealthReady",
    sessiontimeline = "sessionTimelineReady",
    sessionstats = "sessionStatsReady",
    packetaudit = "packetAuditReady",
    updatecadence = "updateCadenceReady",
    clientprofile = "clientProfileReady",
    targetsnapshot = "targetSnapshotReady",
    renderer = "rendererReady",
    statusbadge = "statusBadgeReady",
    diag = "diagReady",
    dump = "dumpReady",
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
    LeaderConfig.capabilities = LeaderConfig.capabilities or {}

    local capabilities = LeaderConfig.capabilities
    if capabilities.schemaVersion == nil then
        capabilities.schemaVersion = CAPABILITY_SCHEMA_VERSION
    end
    if capabilities.maxHistory == nil then
        capabilities.maxHistory = DEFAULT_MAX_HISTORY
    end
    if type(capabilities.history) ~= "table" then
        capabilities.history = {}
    end
    if capabilities.lastUpdatedAt == nil then
        capabilities.lastUpdatedAt = 0
    end
    if capabilities.primarySlashCommand == nil then
        capabilities.primarySlashCommand = ""
    end
    if capabilities.leaderRegistered == nil then
        capabilities.leaderRegistered = false
    end
    if capabilities.leaderBridgeRegistered == nil then
        capabilities.leaderBridgeRegistered = false
    end
    if capabilities.runtimeReady == nil then
        capabilities.runtimeReady = false
    end
    if capabilities.transitionReady == nil then
        capabilities.transitionReady = false
    end
    if capabilities.debugExportReady == nil then
        capabilities.debugExportReady = false
    end
    if capabilities.renderHealthReady == nil then
        capabilities.renderHealthReady = false
    end
    if capabilities.sessionTimelineReady == nil then
        capabilities.sessionTimelineReady = false
    end
    if capabilities.sessionStatsReady == nil then
        capabilities.sessionStatsReady = false
    end
    if capabilities.packetAuditReady == nil then
        capabilities.packetAuditReady = false
    end
    if capabilities.updateCadenceReady == nil then
        capabilities.updateCadenceReady = false
    end
    if capabilities.clientProfileReady == nil then
        capabilities.clientProfileReady = false
    end
    if capabilities.targetSnapshotReady == nil then
        capabilities.targetSnapshotReady = false
    end
    if capabilities.rendererReady == nil then
        capabilities.rendererReady = false
    end
    if capabilities.statusBadgeReady == nil then
        capabilities.statusBadgeReady = false
    end
    if capabilities.diagReady == nil then
        capabilities.diagReady = false
    end
    if capabilities.dumpReady == nil then
        capabilities.dumpReady = false
    end

    return capabilities
end

local function TrimHistory(capabilities)
    local maxHistory = tonumber(capabilities.maxHistory) or DEFAULT_MAX_HISTORY
    if maxHistory < 1 then
        maxHistory = DEFAULT_MAX_HISTORY
    end

    while #capabilities.history > maxHistory do
        table.remove(capabilities.history, 1)
    end
end

local function AppendHistory(capabilities, kind, message, details)
    capabilities.history[#capabilities.history + 1] = {
        ts = ReadNow(),
        kind = tostring(kind or "info"),
        message = tostring(message or ""),
        details = details,
    }
    capabilities.lastUpdatedAt = ReadNow()
    TrimHistory(capabilities)
end

local function SetCapability(capabilities, fieldName, ready, label)
    local normalized = ready and true or false
    if capabilities[fieldName] == normalized then
        return
    end

    capabilities[fieldName] = normalized
    AppendHistory(capabilities, "capability", tostring(label or fieldName), {
        ready = normalized,
    })
end

function CapabilityStatus.Init()
    EnsureConfig()
end

function CapabilityStatus.MarkModuleReady(moduleName, ready)
    local capabilities = EnsureConfig()
    local name = string.lower(tostring(moduleName or ""))
    local fieldName = moduleFieldMap[name] or ""
    if fieldName == "" or capabilities[fieldName] == nil then
        return false
    end

    SetCapability(capabilities, fieldName, ready, name)
    return true
end

function CapabilityStatus.RecordSlashRegistration(primarySlashCommand, leaderRegistered, leaderBridgeRegistered)
    local capabilities = EnsureConfig()
    capabilities.primarySlashCommand = tostring(primarySlashCommand or "")
    capabilities.leaderRegistered = leaderRegistered and true or false
    capabilities.leaderBridgeRegistered = leaderBridgeRegistered and true or false
    AppendHistory(capabilities, "slash", "slash registration updated", {
        primarySlashCommand = capabilities.primarySlashCommand,
        leaderRegistered = capabilities.leaderRegistered,
        leaderBridgeRegistered = capabilities.leaderBridgeRegistered,
    })
end

function CapabilityStatus.GetStatus()
    local capabilities = EnsureConfig()
    return {
        schemaVersion = tonumber(capabilities.schemaVersion) or CAPABILITY_SCHEMA_VERSION,
        lastUpdatedAt = tonumber(capabilities.lastUpdatedAt) or 0,
        primarySlashCommand = tostring(capabilities.primarySlashCommand or ""),
        leaderRegistered = capabilities.leaderRegistered and true or false,
        leaderBridgeRegistered = capabilities.leaderBridgeRegistered and true or false,
        runtimeReady = capabilities.runtimeReady and true or false,
        transitionReady = capabilities.transitionReady and true or false,
        debugExportReady = capabilities.debugExportReady and true or false,
        renderHealthReady = capabilities.renderHealthReady and true or false,
        sessionTimelineReady = capabilities.sessionTimelineReady and true or false,
        sessionStatsReady = capabilities.sessionStatsReady and true or false,
        packetAuditReady = capabilities.packetAuditReady and true or false,
        updateCadenceReady = capabilities.updateCadenceReady and true or false,
        clientProfileReady = capabilities.clientProfileReady and true or false,
        targetSnapshotReady = capabilities.targetSnapshotReady and true or false,
        rendererReady = capabilities.rendererReady and true or false,
        statusBadgeReady = capabilities.statusBadgeReady and true or false,
        diagReady = capabilities.diagReady and true or false,
        dumpReady = capabilities.dumpReady and true or false,
        historyCount = #(capabilities.history or {}),
    }
end

function CapabilityStatus.PrintStatus()
    local status = CapabilityStatus.GetStatus()
    local slashDisplay = status.primarySlashCommand ~= "" and ("/" .. status.primarySlashCommand) or "(none)"
    print(string.format(
        "🛰️ Leader Capabilities: runtime=%s | transition=%s | export=%s | render=%s | timeline=%s | stats=%s | packet=%s | cadence=%s | profile=%s | target=%s | renderer=%s | badge=%s | diag=%s | dump=%s | slash=%s | history=%d",
        status.runtimeReady and "OK" or "no",
        status.transitionReady and "OK" or "no",
        status.debugExportReady and "OK" or "no",
        status.renderHealthReady and "OK" or "no",
        status.sessionTimelineReady and "OK" or "no",
        status.sessionStatsReady and "OK" or "no",
        status.packetAuditReady and "OK" or "no",
        status.updateCadenceReady and "OK" or "no",
        status.clientProfileReady and "OK" or "no",
        status.targetSnapshotReady and "OK" or "no",
        status.rendererReady and "OK" or "no",
        status.statusBadgeReady and "OK" or "no",
        status.diagReady and "OK" or "no",
        status.dumpReady and "OK" or "no",
        slashDisplay,
        status.historyCount))
end

EnsureConfig()

Private.CapabilityStatus = CapabilityStatus
