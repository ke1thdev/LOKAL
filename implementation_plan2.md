# LOKAL VSTO Add-in Overhaul — Match ClassPoint UI/UX

## Goal

Overhaul the LOKAL PowerPoint VSTO add-in to match ClassPoint's polished UI and behavior exactly. The current implementation has a messy ribbon, requires manual class code selection, and uses CustomTaskPanes (which don't work well during slideshows). This plan replaces everything with proper floating overlay forms.

## Key Changes

1. **Ribbon** — Clean layout matching ClassPoint's grouping (Me, Add quiz, More)
2. **Auto-generated join codes** — No manual class selection; auto-create session on slideshow start
3. **Slideshow toolbar** — Floating overlay at bottom with all 18 tools (not a CustomTaskPane)
4. **Class code badge** — Floating overlay in top-right corner during slideshow
5. **Collecting responses** — Floating centered overlay window (not a docked task pane)

> [!IMPORTANT]
> The current approach of using `CustomTaskPane` for the slideshow toolbar and collecting responses panel **does not work during slideshow mode** in PowerPoint. CustomTaskPanes only appear in the normal editing view. We must use **WinForms overlay Forms** positioned on top of the slideshow window instead.

---

## Proposed Changes

### Backend (Go) — New auto-session endpoint

#### [MODIFY] [api.go](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/handlers/api.go)
Add new endpoint: `POST /api/v1/session/auto-start`
- Creates a temporary class with an auto-generated 5-digit numeric code (like ClassPoint's `71287`)
- Starts a session immediately
- Returns `{ class_code, class_id, session_id }`
- No authentication required for simplicity (local-first approach)

#### [MODIFY] [activity.go](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/handlers/activity.go)
Add `AutoStartSession` handler that:
- Generates a random 5-digit code
- Creates a temporary class (or reuses an existing "LOKAL Session" class)
- Starts a session on that class
- Returns the join code + session info

#### [MODIFY] [class.go](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/handlers/class.go)
Add `CreateAutoClass` helper to create classes with auto-generated unique codes.

---

### Ribbon XML — Clean ClassPoint-style layout

#### [MODIFY] [LokalRibbon.xml](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/Ribbon/LokalRibbon.xml)

Restructure to match ClassPoint screenshot exactly:

```
| Me Group          | Add quiz Group                                                            | More Group                    |
|-------------------|---------------------------------------------------------------------------|-------------------------------|
| [User Avatar]     | Multiple | Word  | Short  | Slide   | Image  | Fill in | Audio  | Video  | Quick | My      | Reports | Settings |
| [username ▼]      | Choice   | Cloud | Answer | Drawing | Upload | Blanks  | Record | Upload | Ideas | Classes |         |          |
|                   |                                                                           | More features ▼               |
|                   |                                                                           | Reset ▼                       |
|                   |                                                                           | Get help ▼                    |
```

Key differences from current:
- Remove separate Quick Tools, Gamification, Dashboard groups
- Merge into 3 clean groups: **Me**, **Add quiz**, **More**
- All quiz buttons use `size="normal"` (small icons with labels below)
- "More" group uses dropdown menus for less-used features

---

### Ribbon Callbacks

#### [MODIFY] [LokalRibbon.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/Ribbon/LokalRibbon.cs)

- Remove `OnClassCode` — no manual class selection needed
- Remove `EnsureClassSelected()` — class is auto-created on slideshow start
- Simplify `LaunchActivity` — directly opens the Add Activity side panel (which now works in edit mode too)
- Add `OnSettings` callback for settings button
- Add `OnQuickIdeas` callback

---

### Slideshow Flow — Auto-session + Overlays

#### [MODIFY] [ThisAddIn.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/ThisAddIn.cs)

**On slideshow begin (`OnSlideShowBegin`):**
1. Auto-call `POST /api/v1/session/auto-start` to get a 5-digit join code
2. Store `CurrentClassCode`, `CurrentClassId`, `CurrentSessionId`
3. Show the **class code badge** overlay (top-right corner)
4. Show the **slideshow toolbar** overlay (bottom of screen)
5. Connect WebSocket for real-time student join/response events

**On slideshow end (`OnSlideShowEnd`):**
1. Hide all overlay forms
2. Stop the session via API
3. Disconnect WebSocket

**Remove all `CustomTaskPane` usage** — replace with overlay `Form` instances:
- `SlideshowToolbarForm` (replaces `PresentationToolbarPane`)
- `ClassCodeBadgeForm` (new — top-right floating badge)
- `CollectingResponsesForm` (replaces `CollectingResponsesPane`)

---

### New Overlay Forms

#### [NEW] UI/SlideshowToolbarForm.cs

A `FormBorderStyle.None`, `TopMost=true` overlay form positioned at the bottom of the slideshow window.

**Toolbar buttons (matching ClassPoint screenshot):**
```
| 🔲 | ◀ | ▶ | | ▶ | ◉ | ✏ | 🖌 | ⌫ | | 🔺 | A | 📋 | | ✋ | ⏱ | 📊 | 🎯 | 📊 | | 👁 | ❌ |
| Idx| Prev|Nxt| |Cur|Las|Pen|Hlt|Era| |Shp|Txt|WB | |Drg|Tmr|QPl|NPk|LB | |T/H|Exit|
```

Features:
- Semi-transparent dark background (`#1e1b4b` at 90% opacity)
- White icon buttons with hover effects
- Auto-hides after 3s inactivity, shows on mouse move to bottom
- Separator lines between groups
- Repositions when slideshow window moves/resizes

#### [NEW] UI/ClassCodeBadgeForm.cs

Small floating badge in top-right corner showing:
```
┌──────────┐
│ class    │
│ code     │
│  71287   │
└──────────┘
```
- Dark background with white text
- Large bold font for the code number
- "class code" label above in smaller text
- Click to copy code to clipboard

#### [MODIFY] [CollectingResponsesPanel.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/CollectingResponsesPanel.cs) → **[NEW] UI/CollectingResponsesForm.cs**

Convert from `UserControl` (task pane) to standalone `Form` overlay.

Matching ClassPoint's collecting responses window:
```
┌─────────────────────────────────────────────────┐
│ ○ LOKAL                                    _ □ × │
├─────────────────────────────────────────────────┤
│ 📊 Multiple Choice    Visit localhost:8080       │
│                       and use code 71287 to join │
│                                     Live status  │
├─────────────────────────────────────────────────┤
│                                                  │
│              [Animated bouncing dots]            │
│                                                  │
│           Collecting responses...                │
│   There are no participants yet.                 │
│   Here's how they can join                       │
│                                                  │
├─────────────────────────────────────────────────┤
│ 👥 0  ⏱ 00:03  [Close submission]  👁 Responses  │
│                                    🎵 Music       │
└─────────────────────────────────────────────────┘
```

Features:
- Standard window chrome (minimize, maximize, close)
- Header with activity type + join instructions + code
- Animated bouncing colored dots (blue, pink, orange, purple, green)
- "Collecting responses..." text with participant status
- Bottom bar: participant count, timer, close button, responses toggle, music toggle
- Gradient lavender/light purple background for the animation area

---

### Files to Remove

#### [DELETE] UI/PresentationToolbar.cs
Replaced by `SlideshowToolbarForm.cs`

#### [DELETE] UI/AddActivityPanel.cs
Activity config will be integrated into the collecting responses flow

---

### Modified Files Summary

| File | Action | Description |
|------|--------|-------------|
| `Ribbon/LokalRibbon.xml` | MODIFY | Clean 3-group layout matching ClassPoint |
| `Ribbon/LokalRibbon.cs` | MODIFY | Remove manual class code, simplify callbacks |
| `ThisAddIn.cs` | MODIFY | Auto-session on slideshow, overlay forms instead of task panes |
| `Services/SessionManager.cs` | MODIFY | Add `AutoStartSessionAsync()` method |
| `Services/LokalApiClient.cs` | MODIFY | Add `AutoStartSessionAsync()` API method |
| `UI/SlideshowToolbarForm.cs` | NEW | Floating toolbar at bottom of slideshow |
| `UI/ClassCodeBadgeForm.cs` | NEW | Floating join code badge, top-right |
| `UI/CollectingResponsesForm.cs` | NEW | Floating overlay for collecting responses |
| `UI/PresentationToolbar.cs` | DELETE | Replaced by SlideshowToolbarForm |
| `handlers/activity.go` | MODIFY | Add auto-start session endpoint |
| `handlers/api.go` | MODIFY | Register new endpoint route |

---

## Open Questions

> [!IMPORTANT]
> **Server auto-start**: Should the add-in automatically start the Go server (`lokal-server.exe`) if it's not running? Currently users must manually start the server first, which causes the "Failed to load classes" error in your screenshot.

> [!IMPORTANT]  
> **Authentication**: For the auto-session flow, should we skip authentication entirely (since LOKAL runs locally), or should there still be a login step? ClassPoint requires login, but since LOKAL is offline-first, we could skip it.

> [!IMPORTANT]
> **Activity config panel**: ClassPoint shows a side panel in **edit mode** (not slideshow) when you click an activity button — to configure number of choices, correct answers, quiz mode, etc. (see your 3rd screenshot). Should we keep the `AddActivityPanel` as a CustomTaskPane for edit mode configuration? Or defer config to the collecting responses phase?

---

## Verification Plan

### Build Verification
```bash
MSBuild LOKAL.PowerPoint.csproj /t:Build /p:Configuration=Debug
```

### Manual Verification
1. Open PowerPoint with LOKAL add-in loaded
2. Verify LOKAL ribbon tab has clean 3-group layout
3. Start slideshow → verify:
   - Auto-generated 5-digit code appears top-right
   - Toolbar appears at bottom with all 18 buttons
   - Buttons are functional (next/prev slide, pen, timer, etc.)
4. Click activity button → verify collecting responses overlay appears
5. End slideshow → verify all overlays close cleanly
