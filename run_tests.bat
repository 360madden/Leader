@echo off
title 🧪 Leader Bridge — Roundtrip Tests
echo ========================================
echo   🧪 LEADER: RUNNING ENCODE/DECODE TESTS
echo ========================================
cd LeaderDecoder
bin\Release\net9.0\LeaderDecoder.exe --test
pause
