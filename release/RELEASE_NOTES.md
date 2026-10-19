# VidiComm 1.0.39 Release Notes

## Video
* **ATEM Switching**: Complete support for core video switching capabilities.
* **Program/Preview**: Real-time physical synchronization of Program and Preview busses.
* **Transitions**: Cut, Auto, and fully synced T-bar integration.

## Audio
* **Hardware-Agnostic Audio Architecture**: New generalized logical state model that correctly translates to the physical ATEM SDK.
* **Fairlight Support**: Deep integration with the Blackmagic Fairlight audio engine.
* **Master**: Complete volume and metering control.
* **Camera Inputs**: Independent control of all 40 SDI input channels.
* **Media**: Media Player audio integration.
* **XLR & MADI**: Full support for physical analog XLR and 32 discrete MADI channels.

## Fairlight Processing
* **6-Band EQ**: Full real-time parametric Equalizer control with graphical frequency/gain mapping.
* **Compressor / Expander / Limiter**: Dynamic processing correctly mapped to physical ATEM hardware limits.
* **AFV**: Reliable Audio-Follow-Video toggling and visual state reflection.

## Reliability
* **COM Lifetime Management**: Comprehensive eradication of unmanaged COM memory leaks during high-frequency audio updates.
* **ATEM Connection Persistence**: Solidified connection handler that prevents spurious disconnects.
* **Disconnect/Reconnect**: Graceful failover and robust SDK callback lifecycle tracking when hardware is physically removed and reinserted.

## UI
* **Visual Broadcast Workspaces**: Dedicated layouts for Audio, Video, and Media workflows.
* **Responsive Layout**: Fluid scaling across resolutions (up to 3840x2160 / 200% DPI).
* **Horizontal Feature Navigation**: Clean bottom navigation tray with graceful overflow handling.
* **Production UI Cleanup**: Elimination of all placeholder text and development artifacts, preserving critical technical units (dB, Hz, −∞).

## PWA
* **Logical Service/API Control**: Headless PWA architecture serving ATEM state via HTTP REST and SSE.
* **Live State Feedback**: Sub-second remote UI reflection mirroring the primary desktop application.

## Known Limitations
* Independent `On` state for audio is not supported by the hardware for Media Players (only `Off` and `AudioFollowVideo` are permitted).
* Incremental fader moves from `-infinity` start from the lowest numeric baseline to prevent floating-point SDK exceptions.
