# AtemDirector Mobile - Technology Stack

## Development Environment
- **.NET SDK:** 9.0.305
- **MAUI Workload:** 9.0.120 ✅ (Already installed)
- **IDE:** Visual Studio / Visual Studio Code
- **Android Studio:** For APK deployment and testing

## Technology Stack

### Framework
- **.NET MAUI** (Multi-platform App UI)
  - Single codebase for Android + iOS
  - Native performance
  - C# integration with existing AtemDirector codebase

### UI Approach
- **Hybrid WebView**
  - Embed existing web interface (`server/wwwroot/index.html`)
  - Minimal code duplication
  - Consistent UX across web and mobile

### Target Platforms
- **Android:** API 21+ (Android 5.0 Lollipop and above)
- **iOS:** iOS 15+ (Future phase)

### Key Dependencies
- `Microsoft.Maui.Controls` - Core MAUI framework
- `Microsoft.Maui.Essentials` - Device features (permissions, network)
- `CommunityToolkit.Maui` - Additional UI controls (optional)

## Project Structure
```
AtemDirector/
├── mobile/
│   └── AtemDirector.Mobile/
│       ├── Platforms/
│       │   ├── Android/
│       │   │   ├── MainActivity.cs
│       │   │   ├── AndroidManifest.xml
│       │   │   └── Resources/
│       │   └── iOS/ (future)
│       ├── Resources/
│       │   ├── Images/
│       │   ├── Fonts/
│       │   └── Splash/
│       ├── Pages/
│       │   └── MainPage.xaml (WebView container)
│       ├── Services/
│       │   ├── ServerDiscoveryService.cs
│       │   └── PermissionsService.cs
│       ├── App.xaml
│       ├── AppShell.xaml
│       └── MauiProgram.cs
```

## Build Configuration
- **Debug:** For development and testing on physical device
- **Release:** For production APK distribution
- **Signing:** Android keystore (for release builds)

## Network Architecture
- Mobile app connects to existing AtemDirector server
- Server URL: `https://<local-ip>:8443`
- WebSocket connections for tally and voice
- WebRTC for camera streams

## Permissions Required

### Android
- `INTERNET` - Network communication
- `CAMERA` - Camera room video
- `RECORD_AUDIO` - Voice chat
- `MODIFY_AUDIO_SETTINGS` - Audio routing
- `ACCESS_NETWORK_STATE` - Network detection
- `ACCESS_WIFI_STATE` - WiFi detection

### iOS (Future)
- Camera Usage Description
- Microphone Usage Description
- Local Network Usage Description
