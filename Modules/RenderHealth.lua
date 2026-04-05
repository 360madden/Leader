-- Leader/Modules/RenderHealth.lua
local addon, Private = ...

local RenderHealth = {}

local RENDER_HEALTH_SCHEMA_VERSION = 1
local DEFAULT_MAX_HISTORY = 20

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
    LeaderConfig.renderHealth = LeaderConfig.renderHealth or {}

    local render = LeaderConfig.renderHealth
    if render.schemaVersion == nil then
        render.schemaVersion = RENDER_HEALTH_SCHEMA_VERSION
    end
    if render.maxHistory == nil then
        render.maxHistory = DEFAULT_MAX_HISTORY
    end
    if type(render.history) ~= "table" then
        render.history = {}
    end
    if render.lastFrameAt == nil then
        render.lastFrameAt = 0
    end
    if render.lastFrameSeq == nil then
        render.lastFrameSeq = 0
    end
    if render.lastPixelWrites == nil then
        render.lastPixelWrites = 0
    end
    if render.frameComplete == nil then
        render.frameComplete = false
    end
    if render.layoutSyncCount == nil then
        render.layoutSyncCount = 0
    end
    if render.lastLayoutChanged == nil then
        render.lastLayoutChanged = false
    end
    if render.incompleteFrameCount == nil then
        render.incompleteFrameCount = 0
    end
    if render.clientWidth == nil then
        render.clientWidth = 0
    end
    if render.pixelSize == nil then
        render.pixelSize = 0
    end
    if render.initialized == nil then
        render.initialized = false
    end

    return render
end

local function TrimHistory(render)
    local maxHistory = tonumber(render.maxHistory) or DEFAULT_MAX_HISTORY
    if maxHistory < 1 then
        maxHistory = DEFAULT_MAX_HISTORY
    end

    while #render.history > maxHistory do
        table.remove(render.history, 1)
    end
end

local function AppendHistory(render, kind, message, details)
    render.history[#render.history + 1] = {
        ts = ReadNow(),
        kind = tostring(kind or "info"),
        message = tostring(message or ""),
        details = details,
    }
    TrimHistory(render)
end

function RenderHealth.Init()
    EnsureConfig()
end

function RenderHealth.Sync()
    if not (Private.Renderer and Private.Renderer.GetHealth) then
        return
    end

    local render = EnsureConfig()
    local health = Private.Renderer.GetHealth()

    render.lastFrameAt = ReadNow()
    render.lastFrameSeq = tonumber(health.frameSeq) or 0
    render.lastPixelWrites = tonumber(health.lastPixelWrites) or 0
    render.frameComplete = health.frameComplete and true or false
    render.layoutSyncCount = tonumber(health.layoutSyncCount) or 0
    render.lastLayoutChanged = health.lastLayoutChanged and true or false
    render.clientWidth = tonumber(health.clientWidth) or 0
    render.pixelSize = tonumber(health.pixelSize) or 0
    render.initialized = health.initialized and true or false

    if not render.frameComplete then
        render.incompleteFrameCount = (tonumber(render.incompleteFrameCount) or 0) + 1
        AppendHistory(render, "frame", "incomplete render frame", {
            frameSeq = render.lastFrameSeq,
            pixelWrites = render.lastPixelWrites,
        })
    end

    if render.lastLayoutChanged then
        AppendHistory(render, "layout", "layout resynced", {
            clientWidth = render.clientWidth,
            pixelSize = render.pixelSize,
            layoutSyncCount = render.layoutSyncCount,
        })
    end
end

function RenderHealth.GetStatus()
    local render = EnsureConfig()
    return {
        schemaVersion = tonumber(render.schemaVersion) or RENDER_HEALTH_SCHEMA_VERSION,
        lastFrameAt = tonumber(render.lastFrameAt) or 0,
        lastFrameSeq = tonumber(render.lastFrameSeq) or 0,
        lastPixelWrites = tonumber(render.lastPixelWrites) or 0,
        frameComplete = render.frameComplete and true or false,
        layoutSyncCount = tonumber(render.layoutSyncCount) or 0,
        lastLayoutChanged = render.lastLayoutChanged and true or false,
        incompleteFrameCount = tonumber(render.incompleteFrameCount) or 0,
        clientWidth = tonumber(render.clientWidth) or 0,
        pixelSize = tonumber(render.pixelSize) or 0,
        initialized = render.initialized and true or false,
        historyCount = #(render.history or {}),
    }
end

function RenderHealth.PrintStatus()
    local status = RenderHealth.GetStatus()
    print(string.format(
        "🛰️ Leader Render: initialized=%s | frame=%d | writes=%d | complete=%s | layout-syncs=%d | pixel=%d | width=%d | history=%d",
        status.initialized and "YES" or "no",
        status.lastFrameSeq,
        status.lastPixelWrites,
        status.frameComplete and "YES" or "no",
        status.layoutSyncCount,
        status.pixelSize,
        status.clientWidth,
        status.historyCount))
end

EnsureConfig()

Private.RenderHealth = RenderHealth
