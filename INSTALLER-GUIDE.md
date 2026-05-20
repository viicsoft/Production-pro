# AtemDirector Installer Creation Guide

## Prerequisites

1. **Inno Setup** (free)
   - Download: https://jrsoftware.org/isdl.php
   - Install to default location: `C:\Program Files (x86)\Inno Setup 6\`

2. **PowerShell** (already installed on Windows)

3. **Icon File** (optional but recommended)
   - Place `icon.ico` in `installer/` folder
   - Or I can generate one for you

---

## Build Process

### Step 1: Build Self-Contained Applications

Run this command in PowerShell from the `AtemDirector` folder:

```powershell
.\Build-Release.ps1
```

**What it does:**
- Builds server as self-contained (includes .NET runtime)
- Builds desktop as self-contained (includes .NET runtime)
- Copies Caddy.exe
- Creates Caddyfile template
- Copies ATEM SDK DLLs
- Output: `.\publish\` folder with all files

**Expected output:**
```
.\publish\
├── caddy\
│   ├── caddy.exe (49 MB)
│   └── Caddyfile.template
├── server\
│   ├── server.exe
│   └── [~100 MB of .NET runtime + dependencies]
└── desktop\
    ├── desktop.exe
    ├── Interop.BMDSwitcherAPI.*.dll
    └── [~100 MB of .NET runtime + WPF dependencies]
```

**Total size:** ~250-300 MB

---

### Step 2: Build Installer

Run this command:

```powershell
.\Build-Installer.ps1
```

**What it does:**
- Checks if Inno Setup is installed
- Verifies `.\publish\` folder exists
- Compiles `installer\AtemDirector.iss`
- Creates `AtemDirector-Setup-1.0.0.exe` in `.\installer-output\`

**Expected output:**
```
Installer: AtemDirector-Setup-1.0.0.exe
Size: ~280 MB
Location: C:\worker\AtemDirector\installer-output\AtemDirector-Setup-1.0.0.exe
```

---

## Installation Process (User Experience)

1. **User downloads:** `AtemDirector-Setup-1.0.0.exe`

2. **User runs installer:**
   - Windows SmartScreen warning (click "More Info" → "Run Anyway")
   - Installer wizard opens
   - User clicks "Next" → "Install"

3. **Installer actions:**
   - Extracts to: `C:\Program Files\AtemDirector\`
   - Creates Start Menu shortcut
   - Optionally creates Desktop shortcut
   - No .NET installation needed!

4. **User launches AtemDirector:**
   - Double-click shortcut
   - Launcher detects IP addresses
   - If multiple IPs, shows selection menu
   - Starts Caddy → Server → Desktop
   - Desktop app opens

---

## Files Created

### In Project Folder

```
AtemDirector/
├── Build-Release.ps1          ← Build script
├── Build-Installer.ps1        ← Installer build script
├── installer/
│   ├── AtemDirector.iss       ← Inno Setup script
│   ├── AtemDirector.bat       ← Launcher batch file
│   └── icon.ico               ← App icon (you need to add this)
├── publish/                   ← Created by Build-Release.ps1
│   ├── caddy/
│   ├── server/
│   └── desktop/
└── installer-output/          ← Created by Build-Installer.ps1
    └── AtemDirector-Setup-1.0.0.exe
```

### On User's Machine (After Installation)

```
C:\Program Files\AtemDirector\
├── AtemDirector.bat           ← Launcher
├── config.json                ← Stores last used IP
├── caddy\
│   ├── caddy.exe
│   ├── Caddyfile.template
│   └── Caddyfile              ← Created on first run
├── server\
│   ├── server.exe
│   ├── atemdirector.db        ← Created on first run
│   └── [all dependencies]
└── desktop\
    ├── desktop.exe
    ├── debug.log              ← Created on first run
    └── [all dependencies]
```

---

## Customization

### Change App Version

Edit `installer\AtemDirector.iss`:
```ini
#define MyAppVersion "1.0.0"  ← Change this
```

### Change Install Location

Edit `installer\AtemDirector.iss`:
```ini
DefaultDirName={autopf}\{#MyAppName}  ← Change to custom path
```

### Add App Icon

1. Create or download a `.ico` file
2. Save as `installer\icon.ico`
3. Icon will be used for:
   - Installer icon
   - Desktop shortcut
   - Start Menu shortcut

---

## Troubleshooting

### "Inno Setup not found"
- Install Inno Setup from https://jrsoftware.org/isdl.php
- Ensure it's installed to default location

### ".\publish folder not found"
- Run `Build-Release.ps1` first

### "Caddy.exe not found"
- Ensure `C:\caddy\caddy.exe` exists
- Or manually copy `caddy.exe` to `.\publish\caddy\`

### Build fails
- Check PowerShell execution policy: `Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser`
- Ensure .NET 9 SDK is installed

---

## Distribution

Once built, distribute `AtemDirector-Setup-1.0.0.exe`:

**Via:**
- USB drive
- Network share
- Cloud storage (Google Drive, Dropbox, etc.)
- Internal company portal

**Note:** Without code signing, users will see:
- "Windows protected your PC" (SmartScreen)
- "Unknown Publisher" in installer

**Users can bypass by:**
- Clicking "More Info" → "Run Anyway"
- This is normal for unsigned software

---

## Next Steps

1. Run `Build-Release.ps1`
2. Install Inno Setup if not already installed
3. Run `Build-Installer.ps1`
4. Test the installer on a clean machine
5. Distribute!
