# VidiComm Audio Ecosystem Architecture & ID Canonicalization

**Version:** 1.0 (Architecture Frozen)  
**Status:** Canonical & Mixer-Agnostic  
**Reference Hardware Driver:** Blackmagic ATEM Constellation 40 (Fairlight Audio COM SDK)  

---

## 1. Architectural Overview

VidiComm is a **mixer-agnostic audio control platform**. The audio architecture enforces a strict separation between universal logical audio channels (consumed by WPF, PWA, automation, and monitoring) and hardware-specific physical channels (managed exclusively inside driver implementations).

```text
                    ┌─────────────────────────┐
                    │      WPF / PWA UI       │
                    │ (Mixer, EQ, Dyn, Meters)│
                    └────────────┬────────────┘
                                 │
                                 ▼
                    ┌─────────────────────────┐
                    │   AudioControlService   │
                    │(Thread-Safe Orchestrator│
                    │   & Immutable Cache)    │
                    └────────────┬────────────┘
                                 │
                                 ▼
                    ┌─────────────────────────┐
                    │     AudioRoutingMap     │
                    │(1:1 Bidirectional Route │
                    │ Logical <-> (Dev, Phys))│
                    └────────────┬────────────┘
                                 │
                                 ▼
                    ┌─────────────────────────┐
                    │  AudioHardwareRegistry  │
                    │ (Driver Registry/Lookup)│
                    └────────────┬────────────┘
                                 │
                                 ▼
                    ┌─────────────────────────┐
                    │  IAudioHardwareDriver   │
                    │ (Generic Driver Contract│
                    │  + IAudioDynamicsDriver)│
                    └────────────┬────────────┘
                                 │
             ┌───────────────────┴───────────────────┐
             ▼                                       ▼
    ┌─────────────────┐                     ┌─────────────────┐
    │ AtemAudioDriver │                     │  Future Drivers │
    └────────┬────────┘                     │ (Yamaha, Dante, │
             │                              │   Q-SYS, X32)   │
             ▼                              └─────────────────┘
     ATEM Fairlight SDK
             │
             ▼
    ATEM Constellation 40
```

---

## 2. Canonical Identifier Model

### 2.1 Logical Audio Channel IDs (`Core.LogicalAudioChannels`)
Logical IDs are completely mixer-agnostic. No generic layer or UI component knows or assumes physical ATEM hardware numbers.

| Logical Channel | Canonical ID Constant | Description |
| :--- | :---: | :--- |
| **Camera 1 – 40** | `1` – `40` | Video camera embedded audio channels (`LogicalAudioChannels.Camera(i)`) |
| **Master Output** | `100` | Canonical Logical Master Output Fader (`LogicalAudioChannels.Master`) |
| **Media 1 – 4** | `201` – `204` | Media Player audio channels (`LogicalAudioChannels.Media(i)`) |
| **XLR In 1** | `301` | Balanced Analog Input 1 (`LogicalAudioChannels.Xlr1`) |
| **XLR In 2** | `302` | Balanced Analog Input 2 (`LogicalAudioChannels.Xlr2`) |
| **MADI 1 – 32** | `401` – `432` | MADI Digital Audio Inputs (`LogicalAudioChannels.Madi(i)`) |

### 2.2 Physical ATEM / Fairlight Channel IDs (`Core.AtemPhysicalAudioChannels`)
Physical IDs are internal to the ATEM driver/adapter layer and are never leaked to generic application layers.

| Source Type | Physical ATEM Identifier | Physical Representation / Method |
| :--- | :---: | :--- |
| **Camera Inputs** | `1` – `40` | `IBMDSwitcherFairlightAudioInput` (ID = 1..40) |
| **Master Output** | `100` (`MasterOut`) | `IBMDSwitcherFairlightAudioMixer.SetMasterOutFaderGain` |
| **Media Player 1 – 4** | `2001` – `2004` | `IBMDSwitcherFairlightAudioInput` (ID = 2001..2004) |
| **XLR In 1** | `1301` | `IBMDSwitcherFairlightAudioInput` (ID = 1301) |
| **XLR In 2** | `1401` | `IBMDSwitcherFairlightAudioInput` (ID = 1401) |
| **MADI Inputs** | `1501` – `1532` | `IBMDSwitcherFairlightAudioInput` (ID = 1501..1532) |

---

## 3. Authoritative Routing & Reverse Feedback

### 3.1 Command Flow (Logical → Physical)
1. UI / PWA invokes `AudioControlService.SetVolumeAsync(logicalChannel, volumeDb)`.
2. `AudioControlService` queries `AudioRoutingMap.TryGetPhysicalRoute(logicalChannel, out deviceId, out physicalChannel)`.
3. Registry retrieves driver for `deviceId`.
4. Driver checks `SupportsChannelCapability(physicalChannel, capability)`.
5. Driver sends command to physical hardware using `physicalChannel`.

### 3.2 Feedback Flow (Physical → Logical)
1. ATEM Fairlight COM callback fires (e.g. `FairlightSourceCb`, `FairlightEqCb`, `MasterOutFaderGainChanged`).
2. `AtemAudioMonitor` forwards `physicalChannel` to `AtemAudioDriver`.
3. Driver raises `HardwareStateChanged(deviceId, physicalChannel, ...)` or `HardwareEqStateChanged(...)` or `HardwareDynamicsStateChanged(...)`.
4. `AudioControlService` translates physical channel to logical channel using `AudioRoutingMap.TryGetLogicalRoute(deviceId, physicalChannel, out logicalChannel)`.
5. `AudioControlService` merges update into immutable `AudioState` and raises `StateChanged(AudioState(InputId = logicalChannel, ...))`.
6. UI / PWA updates strip corresponding to `logicalChannel`.

```text
Translation Table:
Logical ID 1..40   <==> atem-main : Physical 1..40
Logical ID 100     <==> atem-main : Physical 100 (MasterOut)
Logical ID 201..204<==> atem-main : Physical 2001..2004
Logical ID 301     <==> atem-main : Physical 1301
Logical ID 302     <==> atem-main : Physical 1401
Logical ID 401..432<==> atem-main : Physical 1501..1532
```

---

## 4. Hardware Capability Model

Hardware capabilities are per-device and per-channel bitmasks:
- `AudioCapability.Volume`
- `AudioCapability.On`
- `AudioCapability.Afv`
- `AudioCapability.Metering`
- `AudioCapability.Eq`
- `AudioCapability.Dynamics`
- `AudioCapability.Gain`

### Channel Specific Constraints:
* **Master (`100`):** Supports `Volume` and `Metering` only. (No On/AFV).
* **Media Players (`2001`–`2004`):** Constrained by ATEM hardware mix options to `0x5` (`AFV | Off`). `On` mode is rejected and guarded.
* **XLR Inputs (`1301`, `1401`):** Support full fader, EQ, and Dynamics.

---

## 5. Adding Future Mixers

To add a new audio mixer (e.g. Yamaha CL/QL, Behringer X32, Dante, QSC Q-SYS):
1. Implement `IAudioHardwareDriver` (and optionally `IAudioDynamicsDriver`).
2. Register the driver in `AudioHardwareRegistry`.
3. Populate `AudioRoutingMap` with logical IDs mapping to the new device ID and physical channels.
4. **Zero changes required** in WPF UI, PWA endpoints, or `AudioControlService`.
