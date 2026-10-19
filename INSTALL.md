# VidiComm v1.0.37 Installation Guide

## Prerequisites
- Operating System: Windows 10 (64-bit) or Windows 11 (64-bit)
- Blackmagic ATEM Switcher (e.g., ATEM Constellation 40) connected via USB or Ethernet
- Blackmagic ATEM Software Control (or ATEM Switchers SDK runtime) installed

## Installation Steps

1. **Download / Locate Installer**
   - File: `installer-output/VidiComm-Setup-1.0.37.exe`
   - SHA256: `187F7BDAFD9C016635245C665392313FBDB22A9B04EC06582C49BACB4D24F5B1`

2. **Run the Installer**
   - Double-click `VidiComm-Setup-1.0.37.exe` (Administrator elevation is requested for COM registration and firewall configuration).
   - Follow the modern setup wizard.
   - The installer will automatically:
     - Install self-contained desktop application (`Vidikom.exe`)
     - Install backend server services (`server.exe`)
     - Install Caddy reverse proxy on port 8443
     - Register `BMDSwitcherAPI64.dll` COM type library via `RegisterATEM.bat`
     - Configure inbound Windows Defender Firewall rules for HTTP (80) and HTTPS (8443)

3. **Launch Application**
   - Launch **VidiComm** from Desktop or Start Menu shortcut.
   - Connect to the switcher (enter IP or use "AUTO" for direct USB connection).
   - Access the PWA intercom / tally interfaces on connected tablets/smartphones via `https://<host-ip>:8443/crew.html`.

## Uninstallation
- Use Windows **Settings > Apps > Installed Apps > VidiComm** or run `unins000.exe` in the application directory.
- The uninstaller will automatically unregister the ATEM COM DLL and remove firewall rules.
