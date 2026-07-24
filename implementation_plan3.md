# Fix LOKAL Ribbon and Activity Panel

This plan addresses several UI/UX issues to match the ClassPoint reference images and improve the professional feel of the add-in.

## User Review Required

- **Transparent Overlay for Text Locking**: PowerPoint Interop does not have a native "lock text" property. To prevent text editing on the inserted shape, I will group a transparent shape over the button. Please confirm this workaround is acceptable.
- **Images**: I will map the buttons in the ribbon to the corresponding `.png` files in the `assets/` folder.
- **Side Panel Configuration**: I will create a basic configuration panel that appears when a quiz button is selected on the slide. This will initially only have the UI for "Multiple Choice" as shown in the screenshot. Other activities will show a placeholder configuration UI. Please let me know if you need full logic for all configuration options immediately.

## Proposed Changes

### Ribbon Update
- Update `LokalRibbon.xml`:
  - Remove the `btnUpgrade` from the `Me` group.
  - Remove `boxStyle="vertical"` from the `Add quiz` group so buttons are laid out horizontally.
  - Ensure all buttons in `Add quiz` are `size="large"`.
  - Replace `imageMso="..."` with `getImage="GetRibbonImage"` for all custom buttons.
- Update `LokalRibbon.cs`:
  - Add `GetRibbonImage(Office.IRibbonControl control)` method to load and return `System.Drawing.Bitmap` from the `assets/` folder based on the control ID.

### Preventing Multiple Insertions & Text Editing
- Update `ThisAddIn.cs` and `AddActivityPanel.cs`:
  - In `InsertActivityShape` / `InsertActivityButtonOnSlide`:
    - Iterate `slide.Shapes` and check if any shape has the `LOKAL_ACTIVITY` tag. If found, show the exact warning: `"There is already a quiz button on this slide. Please delete it before adding a new one."` and abort insertion.
    - Instead of a single shape, create the rounded rectangle, and an invisible rectangle over it, and group them. Add the tag to the group. This prevents users from clicking into the text frame to edit it.

### UI Improvements & Configuration Panel
- Update `AddActivityPanel.cs`:
  - Remove all "PRO" badges since LOKAL is free.
  - Polish colors and fonts to closely match the premium, clean look of ClassPoint.
- Add `ConfigActivityPanel.cs`:
  - Create a new UserControl that mimics the "Multiple Choice" configuration UI shown in the 4th screenshot (Number of choices, Allow selecting multiple choices, Has correct answer, Play Options).
- Update `ThisAddIn.cs`:
  - Hook into `Application.WindowSelectionChange`.
  - When the user selects a shape with the `LOKAL_ACTIVITY` tag, switch the CustomTaskPane to display the `ConfigActivityPanel` instead of the `AddActivityPanel`. When deselected, switch back to `AddActivityPanel`.

## Verification Plan

### Manual Verification
1. Open PowerPoint and check the LOKAL ribbon. The "Add quiz" buttons should be in a single row with custom icons from the `assets` folder, and "Upgrade to Pro" should be gone.
2. Click "Multiple Choice" to add it to the slide.
3. Try to add another activity (e.g., "Word Cloud"). A warning should appear preventing it.
4. Try to click and edit the text of the inserted "Multiple Choice" button. It should not be editable.
5. Click on the inserted "Multiple Choice" button. The side panel should switch to the configuration UI for Multiple Choice.
6. Deselect the shape. The side panel should switch back to the "Add Activity" menu.
