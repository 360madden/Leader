-- Leader/Modules/Renderer.lua
local addon, Private = ...
local Renderer = {}
local context = UI.CreateContext("LeaderRenderer")

--[[
-- LEADER TELEMETRY RENDERER v1.0
-- High-priority UI frames anchored at (0,0) for stable machine-vision capture.
--]]

local pixels = {}
local config = {
    pixelSize = 4,
    anchorX = 0,
    anchorY = 0,
}

--- Initializes the telemetry strip UI.
-- Creates a single 28x4 background frame with 7 high-opacity child textures.
function Renderer.Init()
    local strip = UI.CreateFrame("Frame", "LeaderStrip", context)
    -- Attach directly to context to avoid UIParent UI Scaling interpolation/blur
    strip:SetPoint("TOPLEFT", context, "TOPLEFT", config.anchorX, config.anchorY)
    strip:SetWidth(config.pixelSize * 7)
    strip:SetHeight(config.pixelSize)
    strip:SetLayer(1000)
    strip:SetBackgroundColor(0, 0, 0, 1) -- Opaque black backing

    for i = 1, 7 do
        local p = UI.CreateFrame("Texture", "LeaderPixel" .. (i-1), strip)
        p:SetWidth(config.pixelSize)
        p:SetHeight(config.pixelSize)
        p:SetPoint("TOPLEFT", strip, "TOPLEFT", (i-1) * config.pixelSize, 0)
        p:SetBackgroundColor(0, 0, 0, 1) -- Default black
        pixels[i] = p
    end
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
    end
end

Private.Renderer = Renderer
