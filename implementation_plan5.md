# ClassPoint UI Implementation Plan

This plan outlines the steps to replicate the ClassPoint UI exactly as requested for both the Student Web Interface and the PowerPoint Add-in.

## User Review Required
> [!IMPORTANT]
> The requested changes are extensive and will completely overwrite the existing layout for the student web interface and introduce a major new UI component in the PowerPoint add-in. Please review the proposed designs below to ensure they align exactly with your expectations from the screenshots.

## Proposed Changes

---

### Component 1: Student Web UI Refactor (ClassPoint Clone)

We will redesign the student dashboard to feature the distinctive dark-themed split-screen view shown in your screenshot.

#### [MODIFY] [student/index.html](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/index.html)
- Update the dashboard layout structure to support a main presentation area on the left (white background for slide, dark bar below for student profile) and a dark-themed sidebar on the right.

#### [MODIFY] [student/css/student.css](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/css/student.css)
- Implement a global dark mode base for the dashboard (`#222222` / `#1a1a1a`).
- **Left Panel (Slide Area):** Set background to dark, add the white "whiteboard" container for the slide with proper 16:9 aspect ratio padding. Add the student profile strip below it.
- **Right Panel (Sidebar):** Set background to dark.
- **Answer Buttons:** Redesign buttons to be dark rectangles with colored letters (A: green, B: red, C: blue, D: yellow).
- **Submit Button:** Add the distinct purple-to-blue gradient `Submit` button.
- **Highlights:** Add the yellow background highlight for the "ONLY ONE" instruction text.

#### [MODIFY] [student/js/app.js](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/js/app.js)
- Update the HTML templates injected into the DOM (e.g. `renderMultipleChoiceUI`) to match the new markup structure required for the ClassPoint CSS.
- Move the student profile display from the right sidebar to the bottom of the left slide area.
- Ensure the choice letters (A, B, C, D) receive the exact color classes (Green, Red, Blue, Yellow).

---

### Component 2: PowerPoint Add-in "My Class" Form

We will build the "My Class" overlay that opens when the class code is clicked, exactly matching your screenshots.

#### [MODIFY] [UI/ClassCodeBadgeForm.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/ClassCodeBadgeForm.cs)
- Change the click handler so that clicking the badge opens the new `MyClassForm` instead of just copying the code.

#### [MODIFY] [ThisAddIn.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/ThisAddIn.cs)
- Add property and logic to manage the singleton instance of `MyClassForm`.

#### [NEW] UI/MyClassForm.cs
Create a new floating Windows Form to serve as the "My Class" dialog:
- **Header:** Class name, "Visit www.classpoint.app and use code UTOT to join", and a QR code icon button.
- **Participant List:** Search bar, "Sort by" dropdown, and a FlowLayoutPanel displaying the active participants (fetching from `ApiClient.GetParticipantsAsync`), showing their avatar, name, and star count.
- **Bottom Bar:**
  - Red "Change Class" button.
  - Trophy icon button.
  - Blue "Award stars to all" button.
  - Hamburger menu icon.
- **Popups:** Implement toggle panels for the QR Code popup (Image 4), the "Change Class" dialog (Image 3), and the hamburger menu options (Lock class, Allow guests, Quick Poll, Name Picker - Image 5).

## Verification Plan

### Manual Verification
1. Open the student web app and join a class to verify the UI exactly matches Image 2 (Dark theme, layout, colored answer letters, gradient submit button).
2. Start a presentation in PowerPoint. Click the top-right green class code badge. Verify the new "My Class" form appears.
3. Verify the layout of the "My Class" form matches Images 3, 4, 5. Click the QR code to ensure the popup appears. Click the hamburger menu to ensure the dropdown appears. Click "Change Class" to ensure the class selection popup appears.
