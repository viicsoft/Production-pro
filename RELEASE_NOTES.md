# Version 1.0.38 - Release

## New Features
- **AFV Groups Setup UI & Nominal Audio Restoration:** Added the new Audio Follows Video (AFV) Groups Setup UI. Operators can now navigate to AFV GROUPS and create groups that bind video channels to exact audio channels with explicit, hardware-accurate target Fader dB values (Nominal Audio Level). The editor dynamically pulls the physical hardware faders when adjustments are made.
- **Audio Transitions:** AFV Fades and Cuts now intelligently target specific per-channel Nominal DB targets without overriding competing groups on shared channels, executing synchronously alongside hardware cuts. 

# VidiComm v1.0.37 Release Notes

**Release Date**: September 20, 2026  
**Installer**: `VidiComm-Setup-1.0.37.exe`  
**SHA256**: `187F7BDAFD9C016635245C665392313FBDB22A9B04EC06582C49BACB4D24F5B1`  
**Target Platform**: Windows 10 / 11 (x64)

---

## Overview

VidiComm 1.0.37 is the production release incorporating the frozen, mixer-agnostic audio control architecture and full hardware support for the Blackmagic ATEM Constellation 40 / Fairlight audio processor.

## Key Subsystems & Features

1. **Mixer-Agnostic Audio Architecture**
   - Clean layer separation: UI / PWA -> `AudioControlService` -> `AudioRoutingMap` -> `AudioHardwareRegistry` -> `IAudioHardwareDriver`.
   - Unified canonical channel IDs across all interfaces:
     - Cameras: `1` .. `40`
     - Master Output: `100`
     - Media Players: `201` .. `204`
     - XLR Inputs: `301` .. `302`
     - MADI Inputs: `401` .. `432`

2. **Fairlight Audio Subsystem**
   - Full bidirectional hardware control for faders, ON / OFF / AFV mix options, pan, and gain.
   - Complete 6-Band Parametric Equalizer support (Low-Pass, Low-Shelf, 4 Parametric Bands, High-Shelf, High-Pass) with real-time hardware callback synchronization.
   - Comprehensive Fairlight Dynamics processor: Expander/Gate, Compressor, and Limiter with attack, hold, release, threshold, and ratio controls.

3. **Video Switching Integration & Isolation**
   - Independent video switching layer with zero audio cross-talk or lock contention.
   - Native support for 4 M/E blocks, DSK, FTB, transitions, and input alias mapping.

4. **Web PWA & Mobile Intercom Relay**
   - Universal Caddy reverse proxy on port 8443 providing secure HTTPS/WSS endpoints for crew intercom and tally control.
   - Zero-configuration local network discovery.

