-- Leader/Modules/Renderer.lua
local addon, Private = ...
local Renderer = {}
local context = UI.CreateContext("LeaderRenderer")

--[[
-- LEADER TELEMETRY RENDERER v1.0
-- High-priority UI frames anchored at (0,0) for stable machine-vision capture.
--]]

local strip = nil
local pixels = {}
local layoutState = {
    anchorFrame = nil,
    clientWidth = 0,
    pixelSize = 0,
}
local renderState = {
    initialized = false,
    frameSeq = 0,
    currentPixelWrites = 0,
    lastPixelWrites = 0,
    frameComplete = false,
    layoutSyncCount = 0,
    lastLayoutChanged = false,
}
local config = {
    pixelSize = 8,
    anchorX = 0,
    anchorY = 0,
    referenceWidth = 2560,
    maxPixelSize = 24,
}

local function ReadClientWidth()
    if not UIParent or not UIParent.GetWidth then
        return config.referenceWidth
    end

    local ok, width = pcall(function()
        return UIParent:GetWidth()
    end)

    width = math.floor((tonumber(width) or 0) + 0.5)
    if not ok or width <= 0 then
        return config.referenceWidth
    end

    return width
end

local function ResolveAnchorFrame()
    return UIParent or context
end

local function ResolvePixelSize(clientWidth)
    local width = tonumber(clientWidth) or config.referenceWidth
    if width <= 0 then
        width = config.referenceWidth
    end

    local scaledPixelSize = math.ceil((config.referenceWidth / width) * config.pixelSize)
    if scaledPixelSize < config.pixelSize then
        scaledPixelSize = config.pixelSize
    elseif scaledPixelSize > config.maxPixelSize then
        scaledPixelSize = config.maxPixelSize
    end

    return scaledPixelSize
end

local function ClearPoints(frame)
    if frame and frame.ClearAll then
        frame:ClearAll()
    end
end

local function ApplyLayout(force)
    if not strip then
        return false
    end

    local anchorFrame = ResolveAnchorFrame()
    local clientWidth = ReadClientWidth()
    local pixelSize = ResolvePixelSize(clientWidth)

    if not force
        and layoutState.anchorFrame == anchorFrame
        and layoutState.clientWidth == clientWidth
        and layoutState.pixelSize == pixelSize then
        return false
    end

    ClearPoints(strip)
    strip:SetPoint("TOPLEFT", anchorFrame, "TOPLEFT", config.anchorX, config.anchorY)
    strip:SetWidth(pixelSize * 7)
    strip:SetHeight(pixelSize)

    for i = 1, 7 do
        local p = pixels[i]
        ClearPoints(p)
        p:SetPoint("TOPLEFT", strip, "TOPLEFT", (i-1) * pixelSize, 0)
        p:SetWidth(pixelSize)
        p:SetHeight(pixelSize)
    end

    layoutState.anchorFrame = anchorFrame
    layoutState.clientWidth = clientWidth
    layoutState.pixelSize = pixelSize
    renderState.layoutSyncCount = (tonumber(renderState.layoutSyncCount) or 0) + 1
    renderState.lastLayoutChanged = true
    return true
end

--- Initializes the telemetry strip UI.
-- Creates a single 56x8 background frame with 7 high-opacity child textures.
function Renderer.Init()
    if strip then
        Renderer.SyncLayout(true)
        return
    end

    strip = UI.CreateFrame("Frame", "LeaderStrip", context)
    strip:SetLayer(1000)
    strip:SetBackgroundColor(0, 0, 0, 1) -- Opaque black backing

    for i = 1, 7 do
        local p = UI.CreateFrame("Texture", "LeaderPixel" .. (i-1), strip)
        p:SetBackgroundColor(0, 0, 0, 1) -- Default black
        pixels[i] = p
    end

    renderState.initialized = true
    Renderer.SyncLayout(true)
end

function Renderer.BeginFrame()
    renderState.frameSeq = (tonumber(renderState.frameSeq) or 0) + 1
    renderState.currentPixelWrites = 0
    renderState.frameComplete = false
end

--- Updates the color of a specific telemetry pixel.
-- @param index The pixel index (0-4).
-- @param r Red float [0.0, 1.0].
-- @param g Green float [0.0, 1.0].
-- @param b Blue float [0.0, 1.0].
function Renderer.SetPixel(index, r, g, b)
    local pixelIdx = index + 1
    if pixels[pixelIdx] then
        pixels[pixelIdx]:SetBackgroundColor(r, g, b, 1)
        renderState.currentPixelWrites = (tonumber(renderState.currentPixelWrites) or 0) + 1
    end
end

function Renderer.EndFrame()
    renderState.lastPixelWrites = tonumber(renderState.currentPixelWrites) or 0
    renderState.frameComplete = renderState.lastPixelWrites >= 7
end

function Renderer.SyncLayout(force)
    local changed = ApplyLayout(force and true or false)
    if not changed then
        renderState.lastLayoutChanged = false
    end
    return changed
end

function Renderer.GetHealth()
    return {
        initialized = renderState.initialized and true or false,
        frameSeq = tonumber(renderState.frameSeq) or 0,
        currentPixelWrites = tonumber(renderState.currentPixelWrites) or 0,
        lastPixelWrites = tonumber(renderState.lastPixelWrites) or 0,
        frameComplete = renderState.frameComplete and true or false,
        layoutSyncCount = tonumber(renderState.layoutSyncCount) or 0,
        lastLayoutChanged = renderState.lastLayoutChanged and true or false,
        clientWidth = tonumber(layoutState.clientWidth) or 0,
        pixelSize = tonumber(layoutState.pixelSize) or 0,
    }
end

Private.Renderer = Renderer
