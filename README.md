<<<<<<< HEAD
# Production-pro
atem control
=======
# AtemDirector - Professional ATEM Switcher Control

A comprehensive desktop and web application for controlling Blackmagic ATEM video switchers with integrated tally system and voice communication.

## Features

- 🎥 **ATEM Switcher Control** - Full control of Blackmagic ATEM Mini Pro ISO, Mini Extreme, and 4 M/E Constellation HD
- 🔴 **Tally System** - Real-time tally lights for cameras (Program/Preview)
- 🎙️ **Voice Communication** - WebRTC-based voice chat between director and camera operators
- 🌐 **Web Interface** - Browser-based camera operator interface
- 🖥️ **Desktop App** - Native Windows WPF application for director control
- 🔒 **HTTPS Support** - Secure communication via Caddy reverse proxy

## System Requirements

- **Operating System:** Windows 10/11 (64-bit)
- **Network:** WiFi or Ethernet connection
- **Hardware:** Blackmagic ATEM switcher (optional - includes simulator)

**No additional software required!** The installer includes all dependencies.

## Installation

1. Download `AtemDirector-Setup-1.0.0.exe`
2. Run the installer
3. Follow the installation wizard
4. Launch from Start Menu or Desktop shortcut

**Note:** Windows may show a SmartScreen warning. Click "More Info" → "Run Anyway"

## Quick Start

### For Directors

1. Launch **AtemDirector** from Start Menu
2. Select your network IP address from the dropdown
3. The desktop application will open automatically
4. Select your ATEM mixer model (Mini Pro ISO, Mini Extreme, or Constellation HD)
5. Click "Connect to Selected Mixer"

### For Camera Operators

1. Open a web browser on your phone/tablet
2. Navigate to `https://<director-ip>` (shown in launcher)
3. Sign in as Director to configure camera names
4. Camera operators join with their assigned camera name
5. Tally lights will show red (Program) or green (Preview)

## Building from Source

See [INSTALLER-GUIDE.md](INSTALLER-GUIDE.md) for detailed build instructions.

### Quick Build

```powershell
# Build self-contained applications
.\Build-Release.ps1

# Create installer (requires Inno Setup)
.\Build-Installer.ps1
```

## Project Structure

```
AtemDirector/
├── desktop/          - WPF desktop application (director control)
├── server/           - ASP.NET Core web server (camera interface)
├── core/             - Shared business logic
├── simulator/        - ATEM simulator for testing
└── installer/        - Installer configuration files
```

## Configuration

### Changing Network IP

The launcher automatically detects available IP addresses. If you have multiple network interfaces, you'll be prompted to select one.

### ATEM Connection

The desktop app connects to ATEM hardware at `192.168.10.240` by default. This can be changed in the connection settings.

## Troubleshooting

### "No network interfaces found"
- Ensure you're connected to WiFi or Ethernet
- Check Windows network settings

### Voice communication not working
- Ensure you're accessing via HTTPS (not HTTP)
- Grant microphone permissions in browser
- Check firewall settings

### ATEM not connecting
- Verify ATEM is powered on and connected to network
- Check ATEM IP address matches desktop app settings
- Ensure Blackmagic Desktop Video drivers are installed

## License

Proprietary - Vidikom

## Support

For support, contact: admin@vidikom.com

## Version History

### 1.0.0 (2026-01-29)
- Initial release
- Support for Mini Pro ISO, Mini Extreme, Constellation HD
- Tally system with renamed camera support
- WebRTC voice communication
- Self-contained installer (no .NET installation required)
>>>>>>> fa1d267 (Initial commit of AtemDirector)
