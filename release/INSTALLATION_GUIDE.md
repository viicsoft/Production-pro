# VidiComm Installation Guide

## Requirements
* Windows 10/11 (64-bit)
* Blackmagic ATEM Software Control (must be installed for ATEM drivers)
* .NET 9.0 Desktop Runtime (included in self-contained installer)

## Installation Steps
1. Double-click `VidiComm-Setup-<version>.exe`.
2. Accept the administrative prompt (required to install the COM Interop components and Windows Firewall rules for the PWA server).
3. Follow the on-screen prompts to complete the installation.

## First Launch
1. Launch VidiComm from the Start Menu or Desktop.
2. Ensure your physical Blackmagic ATEM Constellation is connected via USB or accessible via IP on your local network.
3. In VidiComm, navigate to the **Connection** tab.
4. If connected via USB, select **USB Connect**. If over network, enter the IP address and connect.

## PWA Access
VidiComm features a remote Progressive Web App (PWA) control surface.
- Once connected to the ATEM, the PWA server will start automatically.
- Open your browser and navigate to `https://<your-machine-ip>:8443`.
- Due to the self-signed local certificate, you may need to bypass the browser security warning for the local IP.

## Known Limitations
* **Media Players**: Media Players do not support the independent `On` MixOption for audio. They support only `Off` and `AudioFollowVideo`. VidiComm handles this seamlessly by disabling the `On` state for Media sources.

## Uninstalling
* Open Windows **Settings** > **Apps** > **Installed apps**.
* Locate **VidiComm** and select **Uninstall**.
* This will gracefully remove the application, firewall rules, and unregister the ATEM SDK components.

## Support & Diagnostics
* Application logs are stored locally within the application data folder for troubleshooting. If you encounter unexpected ATEM disconnects, ensure the USB cable is securely seated and Blackmagic ATEM Software Control is updated.
