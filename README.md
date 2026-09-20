# VidiComm — Professional ATEM Switcher Control

A comprehensive desktop (WPF/.NET 9) and web application (PWA) for controlling Blackmagic ATEM video switchers with an integrated tally system and a fully service-driven Audio Engine.

## Phase 3: Audio Architecture Status

The VidiComm Audio Engine has successfully completed Phase 3 development and acceptance. It uses a strictly unidirectional, service-driven architecture that correctly maps logical audio states to physical ATEM hardware.

### Current Audio Architecture
```text
WPF / PWA UI
    ↓
AudioControlService (Logical State Cache)
    ↓
AudioRoutingMap (Logical-to-Physical Map)
    ↓
AudioHardwareRegistry (Driver Resolution)
    ↓
AtemAudioDriver (Hardware Abstraction)
    ↓
ATEM COM SDK
    ↓
Physical ATEM Hardware
```

### Hardware Feedback
Hardware feedback travels up the reverse path. Physical changes on the ATEM are unpacked from COM callbacks into logical updates (`AudioState`, `EqState`, `DynamicsState`) and broadcasted back to the UI.
* **Callback Lifecycle:** Memory leaks and duplicate callback loops have been completely eliminated. Callbacks are reliably detached and re-attached when the switcher connects/disconnects.
* **Hardware-First Ordering:** The application guarantees that logical state is not mutated unless the hardware execution command succeeds first.

### Supported Audio Features
- Fader / Volume Control
- Audio ON/OFF
- Audio Follow Video (AFV)
- Live Audio Metering
- **Fairlight EQ:** 6-band parametric EQ (Frequency, Gain, Q, Shape, Band Enable, Master Bypass).
- **Fairlight Dynamics:**
  - Compressor (Threshold, Ratio, Attack, Release, Enabled)
  - Expander/Gate (Threshold, Range, Ratio, Enabled)
  - Limiter (Threshold, Release, Enabled)
  - Makeup Gain

### Simulator
VidiComm ships with `SimAtem` (ATEM Simulator), which successfully intercepts hardware COM updates and correctly simulates asynchronous hardware callbacks for testing.

### Testing
- An extensive suite of automated tests (`AtemDirector.Tests`) validates the integrity of the routing layer, callback echo cancellation, and partial state merging.

### Known Limitations
- The `IAtemSwitch` interface currently handles Macros and Media Pool features in a monolithic fashion. While Audio has been cleanly abstracted into `AtemAudioDriver`, non-audio functions are scheduled for abstraction in a future phase.
- Some fader track visual scales inside `AudioMixerView` differ slightly from Blackmagic's reference visuals, but they accomplish the same usability goals using our own non-infringing design language.

### Physical Hardware Verification Status
**Physical hardware tested: NO** (Verification was performed using automated tests and the `SimAtem` simulator. No physical Blackmagic ATEM hardware was available for final acceptance testing).

## Project Structure

```
VidiComm/
├── desktop/          - WPF desktop application (director control)
├── server/           - ASP.NET Core web server (camera interface)
├── core/             - Shared business logic
├── simulator/        - ATEM simulator for testing
└── installer/        - Installer configuration files
```
