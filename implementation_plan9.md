# UI Polish & Live Chart Implementation Plan

This plan addresses all the visual and functional improvements requested for the `CollectingResponsesForm`.

## Proposed Changes

### 1. Live Bar Chart for Multiple Choice
We will replace the abstract "purple pulse" animation with a live bar chart specifically for Multiple Choice activities.
- Update `AnimPanel_Paint` in `CollectingResponsesForm.cs`: If the activity is `multiple_choice`, draw a bar for each option (A, B, C, D) using distinct colors. The height of each bar will dynamically scale based on the number of responses.
- The chart will immediately reflect new responses as they come in.

### 2. "Who Responded" Popup
- We will add `MouseClick` logic to the chart panel to detect when a bar (e.g., Option 'A') is clicked.
- Clicking a bar will trigger a new popup displaying a list of students who chose that specific answer, matching your second screenshot.

### 3. Live Status Popup (Submitted vs. Pending)
- **Fetching Participants**: To know who is "Pending", `SessionManager` will fetch the full list of participants in the class via `_addIn.ApiClient.GetParticipantsAsync` when the activity starts.
- **Display**: The "Live status" popup will dynamically populate the "Submitted" tab with students who have answered (showing their initials in a circle and their name), and the "Pending" tab with the students who haven't.

### 4. Fix Overlay Flickering
- The black/white flickering when the popup opens is a known Windows Forms issue caused by overlapping panels constantly redrawing.
- **Fix**: We will remove the `_overlayPanel` control and instead directly draw the semi-transparent dark background inside the main rendering loop (`AnimPanel_Paint`). This guarantees a perfectly smooth, flicker-free dark overlay.

### 5. Form Resizing
- We will increase the default dimensions of `CollectingResponsesForm` (e.g., to 950x650) to better match ClassPoint's larger, spacious UI.

### 6. Bottom Bar Icons
- We will replace the default Unicode emojis (⏱, 👥, 👁, 🎵) in the bottom bar with the actual image files from your `assets` folder (`clock.png`, `people.png`, `eye.png`, `hidden.png`, `musical-note.png`).
- We will add `PictureBox` elements next to the labels to render these icons cleanly.

## Verification Plan
- Build the add-in locally.
- Start a Multiple Choice activity and submit answers to verify the bar chart draws and updates correctly.
- Click a bar to verify the "Who Responded" popup shows the correct student.
- Open the Live Status popup to verify it fetches students, sorts them into Submitted/Pending, and doesn't flicker.
- Verify the new icons render correctly in the bottom bar.
