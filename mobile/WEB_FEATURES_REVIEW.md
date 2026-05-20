# AtemDirector Web Functionality Review

## Current Web Features

### 1. **Director Console** (`/index.html`)
**Purpose:** Main control interface for the director to manage the production

**Features:**
- **Connection Setup**
  - Room ID input
  - Camera role selection
  - Join/Create room functionality
  
- **Tally Display**
  - Real-time tally status table
  - Shows all cameras in the room
  - PGM (Program) and PVW (Preview) indicators
  - Color-coded badges (red for program, green for preview)
  
- **WebSocket Connection**
  - Connects to `/ws/tally` endpoint
  - Real-time tally updates
  - Auto-reconnect on disconnect
  
- **Voice Chat (WebRTC)**
  - Connects to `/ws/voice` endpoint
  - Peer-to-peer audio communication
  - Microphone controls
  
- **Visual Feedback**
  - Status dot (connected/disconnected)
  - Tally border around entire viewport
  - Responsive design

### 2. **Camera Room** (`/index.html` - Camera View)
**Purpose:** Interface for camera operators to see their tally status

**Features:**
- **Tally Status Display**
  - Large visual indicator
  - Three states: IDLE, PREVIEW, PROGRAM
  - Color-coded backgrounds
    - Red: ON AIR (Program)
    - Green: PREVIEW
    - Gray: IDLE
  
- **Border Tally**
  - Full-screen border changes color
  - Visual feedback for camera operators
  
- **WebSocket Connection**
  - Connects to `/ws/tally`
  - Receives tally updates for specific camera alias
  - Auto-reconnect functionality
  
- **Haptic Feedback**
  - Vibration when going to program (mobile devices)
  
- **Persistent Settings**
  - Camera alias saved to localStorage
  - Auto-fill on return

### 3. **Admin Page** (`/Admin/Index.cshtml`)
**Purpose:** Administrative interface for managing rooms and configurations

**Features:**
- **Room Management**
  - View all active rooms
  - Create new rooms
  - Delete rooms
  - Configure camera roles per room
  
- **Room Configuration**
  - Define available camera aliases
  - Set room capacity
  - Manage room settings
  
- **Server-Side Rendering**
  - ASP.NET Core Razor Pages
  - Form-based interface
  - Database integration

## Technical Stack (Web)

### Frontend
- **HTML5** - Structure
- **CSS3** - Styling (dark theme, responsive)
- **Vanilla JavaScript** - Logic
- **WebSocket API** - Real-time communication
- **WebRTC API** - Voice/video streaming
- **LocalStorage API** - Persistent settings

### Backend
- **ASP.NET Core** - Server framework
- **WebSocket Middleware** - Real-time tally/voice
- **Razor Pages** - Admin interface
- **SQLite/EF Core** - Database

### Communication Protocols
- **WebSocket** (`/ws/tally`) - Tally updates
- **WebSocket** (`/ws/voice`) - WebRTC signaling
- **HTTP/HTTPS** - Page serving and API calls

## Mobile App Requirements

### Must Replicate
1. ✅ **Director Console**
   - Room join/create
   - Tally table display
   - WebSocket connectivity
   - Voice chat (WebRTC)

2. ✅ **Camera Room**
   - Tally status display
   - Visual indicators (colors, borders)
   - WebSocket connectivity
   - Haptic feedback

3. ✅ **Admin Page**
   - Room management
   - Configuration interface

### Mobile-Specific Enhancements
- **Native Permissions**
  - Camera access (for future video features)
  - Microphone access (for voice chat)
  - Network access
  
- **Native UI Elements**
  - Bottom navigation bar
  - Native buttons and inputs
  - Platform-specific styling
  
- **Offline Support**
  - Cache server URL
  - Reconnection logic
  
- **Push Notifications** (Future)
  - Tally state changes
  - Room invitations

## Implementation Strategy

### Hybrid WebView Approach
- **Primary:** Load existing web UI in WebView
- **Wrapper:** Native MAUI shell for navigation
- **Bridge:** JavaScript-to-C# communication for native features

### URL Structure
- Director Console: `https://<server>:8443/`
- Camera Room: `https://<server>:8443/` (same page, different mode)
- Admin: `https://<server>:8443/Admin/Index`

### Advantages
- ✅ Minimal code duplication
- ✅ Consistent UX across platforms
- ✅ Easy updates (server-side changes reflect immediately)
- ✅ Leverage existing WebSocket/WebRTC code
