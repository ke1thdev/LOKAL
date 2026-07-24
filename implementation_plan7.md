# Add Student Timers and Change Answer Functionality

This plan addresses the request to display a live timer on the student side and allow students to change or unsubmit their answers while the activity is still open.

## User Review Required

> [!IMPORTANT]
> Allowing students to change their answers requires changes across the database, Go backend API, student frontend, and PowerPoint Add-In UI. 
> 
> **Key Decisions:**
> 1. **Change Answer Behavior:** If a student changes their answer, the old response will be overwritten in the database.
> 2. **Unsubmit:** Students will have a "Change Answer" button that simply re-opens the question options for them.
> 3. **Teacher UI:** When a student changes their answer, their submission time in the PowerPoint "Collecting Responses" overlay will update.

## Open Questions

> [!WARNING]
> Since we are updating the Go backend API routes and logic again, you will need to recompile the `lokal.exe` server one more time after I complete these changes. Are you comfortable doing this?

## Proposed Changes

---

### Database Layer
The `database.go` file explicitly prevents submitting a response if one already exists. We will modify this to "upsert" (update if exists).

#### [MODIFY] [database.go](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/database/database.go)
- Modify `SubmitResponse` to check if a response from this participant for this activity already exists.
- If it exists, it will:
  - Revoke any previously earned stars.
  - Update the `answer`, `is_correct`, `stars_earned`, and `response_time_ms`.
  - Re-award the new stars.

---

### Go API Backend
The backend needs to correctly parse `started_at` so the student app can sync the timer. 

#### [MODIFY] [activity.go](file:///c:/xampp/htdocs/LOKAL-ThesisSys/internal/handlers/activity.go)
- No major changes required here as `SubmitResponse` is the core handler. The WebSocket `response` event will automatically broadcast the updated response.

---

### Student Frontend (Web)
We need to render a countdown timer and a "Change Answer" button.

#### [MODIFY] [app.js](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/js/app.js)
- **Timer:** Add a `setInterval` loop that calculates the remaining time using `activity.auto_close_seconds` and the server's `started_at` timestamp. 
- **Timer UI:** Render the remaining time in the `.slide-activity-badge` or a new floating badge on the slide preview.
- **Change Answer:** In the UI shown after submission, add a "Change Answer" button.
- **Button Logic:** Clicking the button resets `studentState.hasSubmitted = false` and re-renders the options, allowing them to click a new answer and submit again.

#### [MODIFY] [student.css](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/css/student.css)
- Add styling for the visual countdown timer (e.g., color changing to red when < 5 seconds remain).
- Add styling for the "Change Answer" button.

---

### PowerPoint Add-In UI
The teacher's live UI currently just appends responses. If a student changes their answer, a duplicate row might appear.

#### [MODIFY] [CollectingResponsesPanel.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/CollectingResponsesPanel.cs)
- Update `RenderResponse` and `AddResponse` to check if the `participant_id` already exists in the `_responses` list.
- If they already exist, update the existing row's response time instead of adding a duplicate row.

## Verification Plan

### Manual Verification
1. I will write the code and apply the changes.
2. You will need to stop and rebuild your Go backend.
3. You will need to rebuild the C# Add-In.
4. Test by joining as a student, submitting an answer, clicking "Change Answer", submitting a different one, and verifying the teacher UI only shows one submission. 
5. Verify the countdown timer accurately reflects the time remaining on the slide.
