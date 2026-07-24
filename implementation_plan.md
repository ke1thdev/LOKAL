# LOKAL — Full System Implementation Plan

> **LOKAL** is a ClassPoint clone that works as a hybrid offline/online PowerPoint add-in system for interactive classroom activities.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | **Go (Golang)** + **SQLite** — lightweight, offline-first |
| Web Frontend | **Vanilla HTML/CSS/JS** — no frameworks, fast & simple |
| Real-time | **WebSocket** via Gorilla/websocket |
| PowerPoint Add-in | **VSTO C# .NET** — native PowerPoint integration |
| Student Web App | **PWA** (Progressive Web App) — works offline |

---

## Project Structure

```
LOKAL-ThesisSys/
├── main.go                          # Go server entry point
├── go.mod / go.sum
├── internal/
│   ├── auth/auth.go                 # JWT auth + session management
│   ├── database/database.go         # SQLite schema + CRUD operations
│   ├── handlers/
│   │   ├── api.go                   # REST API routes
│   │   ├── class.go                 # Class CRUD handlers
│   │   ├── activity.go              # Activity/quiz handlers
│   │   ├── report.go                # Reports handlers
│   │   ├── settings.go              # Settings handlers
│   │   ├── student.go               # Student join/submit handlers
│   │   └── websocket.go             # WebSocket upgrade + messaging
│   ├── hub/hub.go                   # WebSocket hub (broadcast/rooms)
│   ├── models/models.go             # Data structs
│   └── middleware/middleware.go      # Auth middleware, CORS
├── web/
│   ├── teacher/                     # Teacher Dashboard (SPA)
│   │   ├── index.html               # Main shell with sidebar
│   │   ├── css/
│   │   │   └── dashboard.css        # Full dashboard styles
│   │   ├── js/
│   │   │   ├── app.js               # Router + page controller
│   │   │   ├── api.js               # API client
│   │   │   ├── classes.js           # Classes page logic
│   │   │   ├── reports.js           # Reports page logic
│   │   │   ├── activities.js        # Activities page logic
│   │   │   ├── settings.js          # Settings page logic
│   │   │   └── account.js           # Account page logic
│   │   ├── manifest.json
│   │   └── sw.js                    # Service worker for offline
│   ├── student/                     # Student Join Page (PWA)
│   │   ├── index.html
│   │   ├── css/student.css
│   │   ├── js/
│   │   │   ├── app.js               # Student app controller
│   │   │   ├── api.js               # Student API client
│   │   │   └── activities.js        # Activity submission UI
│   │   ├── manifest.json
│   │   └── sw.js
│   └── shared/
│       ├── css/variables.css        # Design tokens (colors, fonts)
│       ├── js/websocket.js          # Shared WS client
│       └── icons/                   # SVG icons
├── addin-vsto/                      # VSTO PowerPoint Add-in
│   └── LokalAddin/
│       ├── LokalAddin.csproj
│       ├── LokalRibbon.xml          # Ribbon UI definition
│       ├── LokalRibbon.cs           # Ribbon callbacks
│       ├── ThisAddIn.cs             # Add-in lifecycle
│       ├── SidePanelControl.cs      # Side panel (quiz config)
│       ├── ApiClient.cs             # HTTP client to Go server
│       ├── BrowserOverlayForm.cs    # WebView2 overlay
│       ├── SlideshowToolbarForm.cs  # Bottom toolbar during slideshow
│       ├── TimerPillForm.cs         # Floating timer pill
│       ├── ResponseOverlayForm.cs   # Response collection overlay
│       ├── DraggableOverlayForm.cs  # Draggable objects
│       └── ToastForm.cs             # Toast notifications
└── task.md
```

---

## Phase 1: Backend Foundation (Go + SQLite)

### [NEW] main.go
- HTTP server on port `8080`
- Serve static files from `web/` directory
- Register API routes under `/api/v1/`
- WebSocket endpoint at `/ws`
- Auto-create SQLite database on first run

### [NEW] internal/database/database.go
**SQLite Schema:**

```sql
-- Teachers
CREATE TABLE teachers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT UNIQUE NOT NULL,
    email TEXT UNIQUE,
    password_hash TEXT NOT NULL,
    display_name TEXT,
    avatar_url TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Classes
CREATE TABLE classes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    teacher_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    code TEXT UNIQUE NOT NULL,
    avatar_color TEXT DEFAULT '#F97316',
    is_locked BOOLEAN DEFAULT 0,
    max_participants INTEGER DEFAULT 200,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (teacher_id) REFERENCES teachers(id)
);

-- Participants (students)
CREATE TABLE participants (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    class_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    device_id TEXT,
    total_stars INTEGER DEFAULT 0,
    level INTEGER DEFAULT 1,
    joined_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (class_id) REFERENCES classes(id)
);

-- Sessions (live class sessions)
CREATE TABLE sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    class_id INTEGER NOT NULL,
    started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    ended_at DATETIME,
    is_active BOOLEAN DEFAULT 1,
    FOREIGN KEY (class_id) REFERENCES classes(id)
);

-- Activities (Multiple Choice, Word Cloud, etc.)
CREATE TABLE activities (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    session_id INTEGER NOT NULL,
    class_id INTEGER NOT NULL,
    type TEXT NOT NULL, -- 'multiple_choice', 'word_cloud', 'short_answer', 'fill_blanks', 'slide_drawing', 'image_upload', 'audio_record', 'video_upload'
    question_text TEXT,
    config JSON, -- activity-specific config (choices, correct answers, etc.)
    is_quiz_mode BOOLEAN DEFAULT 0,
    auto_close_seconds INTEGER DEFAULT 0,
    started_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    closed_at DATETIME,
    FOREIGN KEY (session_id) REFERENCES sessions(id)
);

-- Responses
CREATE TABLE responses (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    activity_id INTEGER NOT NULL,
    participant_id INTEGER NOT NULL,
    answer JSON, -- submitted answer data
    is_correct BOOLEAN,
    stars_earned INTEGER DEFAULT 0,
    response_time_ms INTEGER,
    submitted_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (activity_id) REFERENCES activities(id),
    FOREIGN KEY (participant_id) REFERENCES participants(id)
);

-- Star Level Settings
CREATE TABLE star_levels (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    teacher_id INTEGER NOT NULL,
    level INTEGER NOT NULL,
    stars_required INTEGER NOT NULL,
    badge_name TEXT,
    FOREIGN KEY (teacher_id) REFERENCES teachers(id)
);
```

### [NEW] internal/models/models.go
- Go structs matching all DB tables
- JSON serialization tags

### [NEW] internal/auth/auth.go
- JWT token generation + validation
- Password hashing (bcrypt)
- Login/Register handlers

### [NEW] internal/handlers/api.go
**REST API Endpoints:**

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/v1/auth/register` | Register teacher |
| POST | `/api/v1/auth/login` | Login teacher |
| GET | `/api/v1/classes` | List all classes |
| POST | `/api/v1/classes` | Create new class |
| PUT | `/api/v1/classes/:id` | Update class |
| DELETE | `/api/v1/classes/:id` | Delete class |
| GET | `/api/v1/classes/:id/participants` | List participants |
| GET | `/api/v1/reports` | Get reports |
| GET | `/api/v1/activities` | List activities |
| GET | `/api/v1/settings/star-levels` | Get star level config |
| PUT | `/api/v1/settings/star-levels` | Update star levels |
| POST | `/api/v1/session/start` | Start live session |
| POST | `/api/v1/session/stop` | Stop live session |
| POST | `/api/v1/activity/start` | Start an activity |
| POST | `/api/v1/activity/close` | Close submissions |
| POST | `/api/v1/student/join` | Student joins class |
| POST | `/api/v1/student/submit` | Student submits response |

### [NEW] internal/hub/hub.go
- WebSocket hub with room-based messaging
- Rooms by class code (e.g., `class:UTOT`)
- Message types: `join`, `leave`, `activity_start`, `activity_close`, `response`, `leaderboard_update`

### [NEW] internal/middleware/middleware.go
- JWT auth middleware
- CORS headers
- Request logging

---

## Phase 2: Teacher Web Dashboard

> **UI must match ClassPoint exactly** — dark indigo sidebar, clean white content area, modern typography

### [NEW] web/shared/css/variables.css
Design tokens matching ClassPoint:
```css
:root {
    /* ClassPoint color palette */
    --sidebar-bg: #1e1b4b;           /* Deep indigo */
    --sidebar-hover: #312e81;        /* Lighter indigo */
    --sidebar-active: #4338ca;       /* Active item */
    --primary: #4f46e5;              /* Indigo-600 */
    --primary-hover: #4338ca;
    --accent: #f97316;               /* Orange for avatars */
    --bg-main: #f8fafc;              /* Light gray background */
    --bg-white: #ffffff;
    --text-primary: #1e293b;
    --text-secondary: #64748b;
    --border: #e2e8f0;
    --success: #22c55e;
    --danger: #ef4444;
    --warning: #f59e0b;
    
    /* Typography */
    --font-family: 'Inter', -apple-system, sans-serif;
    --font-size-xs: 0.75rem;
    --font-size-sm: 0.875rem;
    --font-size-base: 1rem;
    --font-size-lg: 1.125rem;
    --font-size-xl: 1.25rem;
    --font-size-2xl: 1.5rem;
    
    /* Spacing */
    --sidebar-width: 180px;
    --header-height: 60px;
    
    /* Shadows */
    --shadow-sm: 0 1px 2px rgba(0,0,0,0.05);
    --shadow-md: 0 4px 6px -1px rgba(0,0,0,0.1);
    --shadow-lg: 0 10px 15px -3px rgba(0,0,0,0.1);
    
    /* Border Radius */
    --radius-sm: 4px;
    --radius-md: 8px;
    --radius-lg: 12px;
    --radius-full: 50%;
}
```

### [NEW] web/teacher/index.html
Main shell matching ClassPoint layout:
- **Sidebar** (fixed left, dark indigo `#1e1b4b`):
  - LOKAL logo at top (styled like ClassPoint's "C" logo)
  - Nav items with SVG icons: Classes, Reports, Activities, Settings, Account
  - Active state with lighter purple highlight bar
- **Header bar** (top of content area):
  - Page title centered
  - User avatar + dropdown on right
- **Content area** (scrollable, light gray bg `#f8fafc`)
  - Dynamic content loaded by JS router

### [NEW] web/teacher/css/dashboard.css
Full styling for all dashboard pages:
- Sidebar with smooth hover animations
- Card components with subtle shadows
- Modal/dialog styling
- Form input styling
- Button variants (primary, secondary, danger)
- Empty state styling
- Level badge grid
- Responsive design for smaller screens
- Micro-animations (fade-in, slide-up, pulse)

### [NEW] web/teacher/js/app.js
SPA router:
- Hash-based routing (`#/classes`, `#/reports`, `#/activities`, `#/settings`, `#/account`)
- Page transition animations
- Sidebar active state management

### [NEW] web/teacher/js/api.js
API client:
- Fetch wrapper with JWT auth headers
- Error handling
- Base URL configuration

### [NEW] web/teacher/js/classes.js
**Classes Page** (matches ClassPoint exactly):
- Header: "View all your classes or add new ones." + "Create new class" button (indigo)
- Class cards in a grid:
  - Large circular avatar (letter + random color)
  - Class name (bold)
  - "X participant · X group" subtitle
  - "Class code: XXXX" in green/teal
- **Create New Class Modal:**
  - Clean white modal with backdrop blur
  - Fields: Class name, Class code (4-8 chars), Class avatar (color picker)
  - Save (indigo) + Cancel buttons
- Edit/Delete class functionality
- Click card to view class details (participants list)

### [NEW] web/teacher/js/reports.js
**Reports Page** (matches ClassPoint):
- **Empty state:**
  - Calendar/grid icon (gradient blue-purple)
  - "No reports yet" heading
  - "You don't have any reports yet. After you teach with LOKAL, the class reports will appear here!" description
- **With data:**
  - Filter by class, date range
  - Report cards showing: session date, class name, activities count, participants, avg score
  - Click to expand: per-activity breakdown with response charts

### [NEW] web/teacher/js/activities.js
**Activities Page** (matches ClassPoint):
- Filter dropdown: "Activity type: All activity types" (select box)
- Activity types: Multiple Choice, Word Cloud, Short Answer, Fill in the Blanks, Slide Drawing, Image Upload, Audio Record, Video Upload
- **Empty state:**
  - Waving hand emoji (👋)
  - "No activities yet" heading
  - "There's no activities yet. After you run LOKAL activities with your students, they will appear here!"
- **With data:**
  - Activity cards with type icon, question text, date, response count, class name

### [NEW] web/teacher/js/settings.js
**Settings Page** (matches ClassPoint exactly):
- **Tab navigation:** Star Levels | Whiteboard Backgrounds | Notifications
- **Star Levels tab:**
  - Description: "Star levels help learners see their progress as they earn stars and unlock higher levels over time."
  - 2×5 grid of level cards:
    - Level 1: 0 stars (gray badge)
    - Level 2: 5 stars (blue badge)
    - Level 3: 10 stars (green badge)
    - Level 4: 20 stars (purple badge)
    - Level 5: 30 stars (orange badge)
    - Level 6-10: escalating star counts with unique badge designs
  - "Edit levels" button (indigo) — opens inline editing
- **Whiteboard Backgrounds tab:**
  - Grid of background templates (lined, grid, dot grid, blank)
- **Notifications tab:**
  - Toggle switches for notification preferences

### [NEW] web/teacher/js/account.js
**Account Page:**
- Profile info (name, email, avatar)
- Change password
- Logout

---

## Phase 3: Student Web App (PWA)

### [NEW] web/student/index.html
Join flow matching ClassPoint student experience:
1. **Join screen:** Enter class code + student name → Join button
2. **Waiting screen:** "Waiting for activity to start..." with animation
3. **Activity screens** (dynamically shown based on activity type):
   - Multiple Choice: A/B/C/D buttons
   - Word Cloud: Text input + submit
   - Short Answer: Text area + submit
   - Fill in the Blanks: Input fields for each blank
   - Image Upload: Camera/file picker + upload
   - Audio Record: Record button with visualizer
   - Video Upload: File picker + upload
   - Slide Drawing: Canvas drawing tool
4. **Results screen:** Stars earned, correct/incorrect feedback
5. **Leaderboard:** Podium view with top performers

### [NEW] web/student/css/student.css
- Mobile-first responsive design
- Large touch-friendly buttons
- Activity-specific layouts
- Animations for feedback (correct/incorrect)

---

## Phase 4: VSTO PowerPoint Add-in

### [NEW] addin-vsto/LokalAddin/LokalRibbon.xml
Ribbon definition with tabs and buttons matching ClassPoint:
- **LOKAL tab** in PowerPoint ribbon
- Button groups:
  - **Me** section: User profile button
  - **Add quiz** section: Multiple Choice, Word Cloud, Short Answer, Slide Drawing, Image Upload, Fill in the Blanks, Audio Record, Video Upload
  - **More** section: My Classes, Reports, Settings
  - Quick Poll, Reset buttons

### [NEW] addin-vsto/LokalAddin/LokalRibbon.cs
Ribbon callbacks:
- Button click handlers for each activity type
- Insert activity marker on current slide
- Open side panel for configuration

### [NEW] addin-vsto/LokalAddin/ThisAddIn.cs
Add-in lifecycle:
- Start/stop local Go server
- SlideShow begin/end events
- Activity overlay management

### [NEW] addin-vsto/LokalAddin/SidePanelControl.cs
**Side Panel** (matches ClassPoint's right panel):
- Activity type header (e.g., "Multiple Choice")
- Configuration options per type:
  - **Multiple Choice:** Number of choices (2-8), allow multiple, correct answer(s), quiz mode toggle
  - **Word Cloud:** Max submissions per student
  - **Short Answer:** Character limit
  - **Fill in Blanks:** Define blanks + answer keys
- **Play Options:**
  - Start activity with slide checkbox
  - Minimize activity window checkbox
  - Auto-close submission after X seconds
- **Save as default** link
- **View Responses** button

### [NEW] addin-vsto/LokalAddin/SlideshowToolbarForm.cs
**Slideshow Toolbar** (bottom of screen during slideshow):
- Class name display
- QR code + class code for joining
- "Waiting for participants to join..." status
- Lock class toggle
- Quick Poll + Name Picker buttons
- Change Class button
- Participant avatars

### [NEW] addin-vsto/LokalAddin/ResponseOverlayForm.cs
**Response Collection Overlay** (matches ClassPoint):
- Header: Activity type + "Visit [url] and use code XXXX to join" + "Live status"
- Animated bouncing dots (blue, pink, orange, purple, green)
- "Collecting responses..." text
- "There are no participants yet. Here's how they can join" link
- Bottom bar: participant count, timer (MM:SS +Xs), Close submission button, Responses toggle, Music toggle

### [NEW] addin-vsto/LokalAddin/TimerPillForm.cs
Floating timer pill (orange capsule):
- Participant count icon + count
- Timer icon + elapsed time

### [NEW] addin-vsto/LokalAddin/BrowserOverlayForm.cs
WebView2 browser overlay for showing results, leaderboards

### [NEW] addin-vsto/LokalAddin/DraggableOverlayForm.cs
Transparent overlay for draggable objects on slides

### [NEW] addin-vsto/LokalAddin/ApiClient.cs
HTTP client:
- Connect to local Go server
- Start/stop sessions
- Start/close activities
- Get responses in real-time

---

## Phase 5: Pro Features (All Free in LOKAL)

| Feature | Implementation |
|---------|---------------|
| **200 Participants** | SQLite can handle it, WebSocket hub supports it |
| **Star Accumulation & Levels** | `participants.total_stars` + `star_levels` table, badge SVGs |
| **Quiz Mode** | `is_quiz_mode` flag, auto-scoring, difficulty tracking, speed bonuses |
| **Unlimited Draggable Objects** | `DraggableOverlayForm` — no artificial limit |
| **Fill in the Blanks** | Activity type with multiple blank fields + answer key matching |
| **Audio Record** | MediaRecorder API on student side, file upload to server |
| **Video Upload** | File upload with chunked transfer, embedded playback |
| **Unlimited Classes** | No limit in DB schema |
| **Leaderboard** | Session + historical modes, podium visualization |

---

## Implementation Order

> [!IMPORTANT]
> Building in this order ensures each phase is testable independently.

| Phase | What | Duration Estimate |
|-------|------|-------------------|
| **Phase 1** | Go backend + SQLite + REST API + WebSocket | First |
| **Phase 2** | Teacher Web Dashboard (all 5 pages) | Second |
| **Phase 3** | Student Web App (join + activities) | Third |
| **Phase 4** | VSTO PowerPoint Add-in | Fourth |
| **Phase 5** | Pro features + polish | Fifth |

---

## User Review Required

> [!IMPORTANT]
> **Login/Auth Flow**: Should teachers register with email+password, or do you want a simpler setup (e.g., just a username/password since this runs locally)?

> [!IMPORTANT]
> **Branding**: Should we use "LOKAL" as the brand name everywhere, with a custom logo? Or do you have specific branding assets?

> [!WARNING]
> **VSTO Requirement**: Building the VSTO add-in requires Visual Studio with Office Development tools installed. The web parts (dashboard + student app) can be built and tested independently. Should I start with the web parts first?

## Verification Plan

### Automated Tests
- Go backend: `go test ./...` for all handlers
- API endpoint testing with curl/httpie

### Manual Verification
- Run `go run main.go` and open teacher dashboard in browser
- Create a class, verify it appears in the list
- Open student page, join with class code
- Start an activity, submit a response, verify it appears in reports
- Test offline mode by disconnecting network
