-- Leader/Modules/Encoder.lua
local addon, Private = ...
local Encoder = {}
local TWO_PI = math.pi * 2

--[[
-- LEADER TELEMETRY ENCODER v1.1
-- High-precision 24-bit packing for spatial coordinates and state flags.
-- Protocol version 1.1 — matches TelemetryService.cs decode logic exactly.
--]]

-- ──────────────────────────────────────────────────────────────────────────────
-- COORD PACK  (Pixels 2, 3, 4)
-- Precision: 0.1 units   |   Range: ±838860.7 metres
-- Formula:   n = floor(val * 10) + 8388608   (24-bit centre offset)
-- Layout:    R=low byte, G=mid byte, B=high byte
-- ──────────────────────────────────────────────────────────────────────────────
function Encoder.PackCoord(val)
    local n = math.floor(val * 10 + 8388608)
    -- Clamp to 24-bit range [0, 16777215]
    n = math.max(0, math.min(16777215, n))
    local r = n % 256
    local g = math.floor(n / 256) % 256
    local b = math.floor(n / 65536) % 256
    return r / 255, g / 255, b / 255
end

-- ──────────────────────────────────────────────────────────────────────────────
-- HEADING PACK  (Pixel 5)
-- Precision: ~0.0001 rad  |   Range: 0 → 2π (6.2832)
-- Formula:   radVal = floor(radian * 10000)  [fits in 16 bits: max 62832]
-- Layout:    R=low byte, G=high byte, B=zone hash
-- Note: No offset needed — RIFT facing is always [0, 2π]
-- This matches: TelemetryService.cs  (ph.R + ph.G*256) / 10000.0f
-- ──────────────────────────────────────────────────────────────────────────────
function Encoder.PackHeading(radian, zoneHash)
    radian = tonumber(radian) or 0

    while radian < 0 do
        radian = radian + TWO_PI
    end

    while radian > TWO_PI do
        radian = radian - TWO_PI
    end

    local radVal = math.floor((radian or 0) * 10000)
    radVal = math.max(0, math.min(65535, radVal))
    local r = radVal % 256
    local g = math.floor(radVal / 256) % 256
    local b = (zoneHash or 0) % 256
    return r / 255, g / 255, b / 255
end

-- ──────────────────────────────────────────────────────────────────────────────
-- STATE PACK  (Pixel 1)
-- R = playerHP [0-255], G = targetHP [0-255], B = flags bitfield
-- ──────────────────────────────────────────────────────────────────────────────
function Encoder.PackState(playerHP, targetHP, flags)
    return (playerHP or 0) / 255,
           (targetHP or 0) / 255,
           (flags or 0)    / 255
end

-- ──────────────────────────────────────────────────────────────────────────────
-- HASH PACK  (Pixel 6)
-- Derives a 3-byte identity from the last 6 hex characters of the Unit ID.
-- ──────────────────────────────────────────────────────────────────────────────
function Encoder.PackHash(id)
    if not id then return 0, 0, 0 end
    local s = tostring(id)
    local last6 = string.sub(s, -6)
    if #last6 < 6 then
        last6 = string.rep("0", 6 - #last6) .. last6
    end
    local r = tonumber(string.sub(last6, 1, 2), 16) or 0
    local g = tonumber(string.sub(last6, 3, 4), 16) or 0
    local b = tonumber(string.sub(last6, 5, 6), 16) or 0
    return r / 255, g / 255, b / 255
end

Private.Encoder = Encoder
