# Multiple Choice Quiz Mode — End-to-End Implementation

Make the LOKAL Multiple Choice quiz mode fully functional, responsive, and professional — matching ClassPoint's quality. This includes the VSTO side panel (ConfigActivityPanel), presentation mode overlays, and student-facing answer UI.

## Scope Summary

1. **Fix & enlarge the VSTO ConfigActivityPanel** — wider panel (350→420px), functional number-of-choices selector, working checkboxes, working quiz mode toggle, functional "View Responses" button that starts the activity
2. **Fix the presentation mode toolbar** — all buttons working, positioned correctly at bottom
3. **Fix the auto-generated class code badge** — appears top-right during slideshow, code from auto-session
4. **Fix the CollectingResponsesForm** — appears when "View Responses" is clicked or activity starts in slideshow, properly tracks incoming responses
5. **Fix the student Multiple Choice UI** — responsive answer buttons, correct submission flow, works on mobile
6. **End-to-end quiz flow** — teacher inserts MC → configures → starts slideshow → class code auto-generated → student joins with code → receives activity → submits answer → teacher sees response count

---

## Proposed Changes

### Component 1: VSTO ConfigActivityPanel (Side Panel Fix)

The side panel currently uses hard-coded pixel positions and doesn't respond to resizing. The panel width is set to 350px but ClassPoint uses a wider panel. Need to:

#### [MODIFY] [ConfigActivityPanel.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/ConfigActivityPanel.cs)

**Complete rewrite** to make the panel functional:
- **Use `FlowLayoutPanel` or `TableLayoutPanel`** instead of absolute positioning for proper resizing
- **Number of choices selector**: Currently visual-only — make clickable, track selected count (2-8), highlight active button
- **Checkboxes**: Wire `Allow selecting multiple choices` and `Has correct answer(s)` to store values
- **Correct answer dropdown**: Dynamically update options (A-H) based on number of choices selected
- **Quiz mode toggle**: Create a proper toggle switch control, enable/disable star rating
- **Star rating**: Make stars clickable (1-3 stars difficulty)
- **Auto-close timer**: Wire the checkbox + combobox to set auto-close seconds
- **"View Responses" button**: 
  - When clicked, start the activity via `SessionManager.StartActivityAsync()`
  - Read the question text from the current slide's text content
  - Build the config JSON from panel state: `{ choices: number, allow_multiple: bool, correct_answer: "A", quiz_mode: bool, difficulty: 1-3, auto_close: seconds }`
  - Show the `CollectingResponsesForm`
- Increase panel width from 350 to **420px**

#### [MODIFY] [ThisAddIn.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/ThisAddIn.cs)

- Change `ConfigActivityPane.Width` from 350 to 420
- Change `AddActivityPane.Width` from 350 to 420
- Add helper method `GetSlideQuestionText()` that reads text from the current slide
- Add helper to build choice labels (A, B, C, D...) based on number of choices

---

### Component 2: Presentation Mode Toolbar

#### [MODIFY] [SlideshowToolbarForm.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/SlideshowToolbarForm.cs)

The toolbar is mostly working but needs:
- **LOKAL logo button** at the far left (currently missing)
- **Active state highlighting** for the currently selected tool (Cursor/Laser/Pen/etc.)
- Fix the **Shapes/Text/Whiteboard** handlers (currently no-ops) — at minimum show "Coming soon" messages
- Fix **Draggable Objects** handler
- Ensure the toolbar auto-hides after inactivity and re-shows on mouse move near bottom

---

### Component 3: Class Code Badge

#### [MODIFY] [ClassCodeBadgeForm.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/ClassCodeBadgeForm.cs)

Currently functional but:
- Make the badge slightly larger for readability: from 92×56 to **110×60**
- Font size for code from 14f to **16f**
- Add subtle glow/shadow for visibility against any slide background

---

### Component 4: CollectingResponsesForm

#### [MODIFY] [CollectingResponsesForm.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/CollectingResponsesForm.cs)

The form structure is good but needs:
- **Fix the layout**: The bottom panel buttons overlap. Use `Anchor` or `Dock` properly
- **Make "Close submission" button anchored right** in the bottom panel
- **Add a results view**: When closed, show bar chart of responses (A: 3, B: 5, C: 1, D: 2)
- **Wire the response counter** to update from WebSocket events

---

### Component 5: Student Multiple Choice UI (Web)

#### [MODIFY] [student/js/app.js](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/js/app.js)

- Fix `renderMultipleChoiceUI()` — the answer buttons should show proper ClassPoint-style colored options:
  - **A = Blue-ish**, **B = Orange/Red**, **C = Green**, **D = Purple/Yellow**, etc.
  - Each button should be a large, touch-friendly rectangle with the letter AND choice text
- Fix `selectAnswer()` — proper toggle animation, visual feedback
- Fix `submitAnswer()` — show a "Response submitted" confirmation with the selected answer in a circle
- Parse activity config properly — the config JSON from the teacher should drive the number of choices displayed
- Show question text in the slide area

#### [MODIFY] [student/css/student.css](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/css/student.css)

- **Improve answer button colors** — use ClassPoint-style gradient colors per option:
  - A: `#5B6CF5` (indigo), B: `#EF6C4E` (coral), C: `#6BCB77` (green), D: `#FFD93D` (yellow), E-H: distinct colors
- **Larger touch targets** — min 56px height on mobile
- **Selected state animation** — scale + border glow
- **Responsive grid** — 2×2 on mobile, 2×4 on desktop for 8 options
- **Submit button** — full-width, gradient, disabled state with reduced opacity
- **Submitted confirmation** — centered check icon with answer label, gentle animation

---

### Component 6: Backend fixes for quiz flow

#### [MODIFY] [activity.go](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/handlers/activity.go)

- Fix `StudentSubmit` handler — currently broken because it tries to re-read the request body after `decodeJSON` consumed it. Fix to include `participant_id` in the `StudentSubmitRequest` model so it's parsed in the first decode.

#### [MODIFY] [models.go (internal/models)](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/models)

- Add `ParticipantID` field to `StudentSubmitRequest` so the handler can read it from the JSON body directly

---

## Open Questions

> [!IMPORTANT]
> **Quiz mode scoring**: ClassPoint's quiz mode awards stars based on speed and correctness. Should we implement the full scoring system (stars per correct answer + speed bonus) now, or just track correct/incorrect for MVP?

> [!IMPORTANT]
> **Slide content parsing**: When the teacher clicks "View Responses," should we auto-detect the question text from the slide content, or require them to type it manually in the panel?

## Verification Plan

### Manual Verification
1. Build the VSTO add-in and load in PowerPoint
2. Click "Multiple Choice" in LOKAL ribbon → verify shape appears on slide + config panel opens with proper width
3. Click the shape → config panel shows with all controls working
4. Select number of choices, check "Has correct answer(s)", set quiz mode
5. Start slideshow → verify class code badge appears top-right, toolbar at bottom
6. Click "View Responses" or the Multiple Choice button on slide → CollectingResponsesForm appears
7. Open student page in browser → enter class code → enter name → join
8. Verify MC answer buttons appear with proper colors
9. Select an answer → click Submit → see confirmation
10. Verify teacher's CollectingResponsesForm shows response count incrementing

### Automated Tests
- `go build ./...` — verify Go backend compiles
- `go run main.go` — verify server starts and endpoints respond
