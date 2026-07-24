# Refined UI Implementation Plan

This plan addresses your feedback to ensure the UI feels like a premium, exact replica of the reference design, while using your custom "LOKAL" branding instead of ClassPoint, fixing mobile responsiveness, and auto-generating the real QR code.

## User Review Required
> [!IMPORTANT]
> Please review the proposed changes below. Note that for the QR code generation in the PowerPoint add-in, I will use a free, reliable public API (`api.qrserver.com`) to instantly generate the QR image directly in the app. This requires an active internet connection when opening the dialog. 

## Proposed Changes

---

### Component 1: Web UI Responsiveness and Branding
We will fix the "squeezed" layout on mobile by applying proper flexbox column layout rules and resizing the containers. We will also restore LOKAL branding while keeping the premium aesthetic.

#### [MODIFY] [student/index.html](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/index.html)
- Change "ClassPoint.app" text back to "LOKAL" in the header.
- Add a "Full Screen" button (`[ ]` icon) to the bottom right of the student profile strip below the slide, exactly like the reference image.

#### [MODIFY] [student/css/student.css](file:///c:/xampp/htdocs/LOKAL-ThesisSys/web/student/css/student.css)
- Change `.cp-logo-icon` to use LOKAL's primary color instead of ClassPoint blue.
- Add a `@media (max-width: 768px)` query specific to the `.cp-body` container. On mobile:
  - `.cp-body` will switch to `flex-direction: column`.
  - `.cp-slide-area` will take full width and scale proportionally without being squished.
  - `.cp-sidebar` will shift below the slide area and take full width.

---

### Component 2: PowerPoint Add-in "My Class" Form
We will fix the branding and make the QR code functional and dynamic based on the actual local IP and class code.

#### [MODIFY] [UI/MyClassForm.cs](file:///c:/xampp/htdocs/LOKAL-ThesisSys/addin/LOKAL.PowerPoint/UI/MyClassForm.cs)
- Change "Inknoe ClassPoint" window title to "LOKAL".
- Change "C." logo to "L." and the class text instructions to say: "Visit **{CurrentJoinUrl}** and use code **{ClassCode}** to join".
- In the QR popup, update the `PictureBox` to dynamically load the QR code image using `PictureBox.LoadAsync($"https://api.qrserver.com/v1/create-qr-code/?size=160x160&data={Uri.EscapeDataString(url)}")`.

## Verification Plan

### Manual Verification
1. Open the student web app on a mobile view (e.g., in Chrome DevTools) and verify the slide is full-width at the top, and the multiple choice buttons are below it.
2. Verify the header says "LOKAL" but maintains the dark premium aesthetic.
3. In the PowerPoint Add-in, open the "My Class" form and click the QR button. Verify a real, scannable QR code appears pointing to your local student join link.
