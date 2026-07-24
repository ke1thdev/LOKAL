# LOKAL UI Revamp & Feature Sync Plan

This plan addresses all the bugs and design requests you've mentioned, including a complete, pixel-perfect UI overhaul of the Add-in dialogs to match ClassPoint's premium modern look.

## User Review Required
> [!IMPORTANT]
> This is a massive update that involves overhauling the PowerPoint Add-in's UI and adding new backend APIs for slide synchronization. Please review the plan below. If everything looks good, approve it and I will begin execution!

## Open Questions
- For the "Collecting Responses" dialog, ClassPoint has a "Music" button. LOKAL doesn't currently support background music during activities. Should I add a dummy "Music" button for visual parity, or leave it out? (I will leave it out unless you specify otherwise).

## Proposed Changes

---

### Backend (Go)

#### [MODIFY] `internal/database/database.go`
- Fix the bug where students don't get points even if they answer correctly. The frontend submits answers as string indices (`"A,C"`) which the C# addin converts to `[0,2]`. We will ensure `isAnswerCorrect` correctly compares these indices to what the student submits.

#### [MODIFY] `internal/handlers/api.go`
- Add a new route `POST /api/v1/classes/{id}/slide` to handle slide uploads when no activity is running.

#### [NEW] `internal/handlers/presentation.go`
- Implement the `UploadClassSlide` handler. This will accept a slide image and broadcast a `slide_changed` event to the WebSocket room, ensuring students always see the current PowerPoint slide, even outside of activities.

---

### Student Web UI

#### [MODIFY] `web/student/index.html`
- Add a permanent "Total Stars" badge in the UI (e.g., top right corner) so students can always see their current score.

#### [MODIFY] `web/student/js/app.js`
- Initialize the total stars counter from the student's session data.
- Listen for the `stars_awarded` WebSocket event. When received, increment the student's on-screen star counter with a smooth animation.
- Listen for the `slide_changed` WebSocket event to update the background image when the teacher moves to a new slide in PowerPoint.

---

### PowerPoint Add-in (C#)

#### [MODIFY] `addin/LOKAL.PowerPoint/ThisAddIn.cs`
- Update `OnSlideShowNextSlide` to extract the current slide image and upload it to the new `/api/v1/classes/{id}/slide` endpoint. This guarantees the student's screen always stays in sync with the presentation.

#### [MODIFY] `addin/LOKAL.PowerPoint/UI/SlideshowToolbarForm.cs`
- Ensure the "Start Activity" button (▷) in the toolbar acts as a "Reopen" button if the dialog was closed but the activity is still running in the background.

#### [MODIFY] `addin/LOKAL.PowerPoint/UI/CollectingResponsesForm.cs`
- **UI Revamp**: Completely redesign this form to exactly match Screenshot 2 & 3.
- Remove standard Windows borders and implement rounded corners.
- Add the white top header with logo, join URL, and blue "Live status" text.
- Add the light-purple center body with an animated visualizer and "Collecting responses..." text.
- Add the white footer with participant count, timer, and the red rounded "Close submission" button.

#### [MODIFY] `addin/LOKAL.PowerPoint/UI/MyClassForm.cs`
- **UI Revamp**: Completely redesign this form to exactly match Screenshot 4.
- Implement the clean split layout with the QR code card on the left and the participant grid on the right.
- Add the bottom footer with the "Change Class" (red) and "Award stars to all" (blue) rounded buttons.

## Verification Plan

### Automated Tests
- Build the Go backend `lokal.exe` to ensure new endpoints compile correctly.
- Ensure the Visual Studio solution builds without errors.

### Manual Verification
- Start a presentation and verify that navigating through slides automatically updates the student's web view.
- Start an activity, close the new modern UI, and verify that clicking the toolbar button reopens it.
- Submit a correct answer, close the activity, and verify the student's on-screen total star counter increases.
