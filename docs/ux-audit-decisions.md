# VSMS UX Audit - Decisions Log

This document tracks our decisions for each UX issue identified in the audit.

---

## CRITICAL Issues

### C1. Publish Claims to Send Emails But Does NOT

**Problem:** The Publish page promises "Email notifications will be sent shortly" but the code only has TODO comments. No emails are actually sent.

**Options:**
1. Remove the false promises from the UI until email is implemented
2. Implement the email functionality now
3. Change the wording to be honest about current state ("Email notifications not yet implemented")

**Decision:** DEFER - Remove false promises from UI for now

**Notes:** Email infrastructure exists (Resend) but not configured. Will need to sign up for email provider and configure credentials when ready to implement. For now, remove misleading "Email notifications will be sent shortly" message from Publish page.

---

### C2. Shift Request Submits Into Void - No Confirmation

**Problem:** Volunteers submit a request and get no feedback - no email, no status check, no timeline.

**Options:**
1. Add request status to My Shifts page so volunteers can check
2. Send email confirmation on submit
3. Add clear "what happens next" message on the success page
4. All of the above

**Decision:** IMPLEMENT - Both A + B (no email dependency)

**Notes:**
- Add "Pending Requests" section to My Shifts page showing requests awaiting approval
- Improve success message on Request page with clear "what happens next" guidance
- Email confirmation can be added later when email system is configured

**Changes needed:**
- `src/VSMS.Web/Pages/Shifts/MyShifts.cshtml` - Add pending requests section
- `src/VSMS.Web/Pages/Shifts/MyShifts.cshtml.cs` - Query pending ShiftRequests for volunteer
- `src/VSMS.Web/Pages/Shifts/Request.cshtml` - Improve success message text

---

### C3. "Master Schedule" is Confusing Jargon

**Problem:** Admins don't understand this is a template for future months, not the actual schedule.

**Options:**
1. Rename to "Weekly Template" or "Default Schedule"
2. Add prominent explanation banner
3. Add visual distinction (different color scheme) from Calendar
4. Combination of above

**Decision:** REWORK - Eliminate Master Schedule / Generate / Publish entirely

**Notes:**
The entire template-based system is overengineered. New simplified model:
- Shifts are static, defined in code/DB seed
- Rolling 3-month window visible to volunteers
- Each shift is independent (no recurring assignments)
- Volunteers request → Admin approves
- Swap = volunteer withdraws, backup takes slot
- To change schedule structure, modify the program (acceptable for now)

**Remove:**
- Master Schedule pages (`/admin/master-schedule/*`)
- Generate Month flow (`/admin/calendar/generate`)
- Publish Month flow (`/admin/calendar/publish`)
- `MasterScheduleEntry` entity
- `MonthPublishedAt` field on Shift

**Keep:**
- Calendar view (shows next 3 months of shifts)
- TimeSlots (defines when shifts occur)
- Shift editing (assign/confirm volunteers)
- Request approval workflow

**Changes needed:**
- Remove Master Schedule nav item and pages
- Remove Generate/Publish pages and buttons
- Modify Calendar to show rolling 3-month window automatically
- Seed shifts in DB or generate on startup
- Update nav and admin dashboard

---

### C4. Expired Email Links Have No Recovery Path

**Problem:** 14-day token expiration with only "contact the office" as guidance.

**Options:**
1. Add "Request New Link" button that sends fresh token email
2. Extend expiration to 30 days
3. Show specific contact info instead of vague "office"
4. Combination

**Decision:** DEFER - Acceptable for now

**Notes:**
Volunteers should know what "the office" is since they're volunteering there. If they can't figure it out, they can call. Revisit when email system is implemented (could add "Request New Link" button then).

---

## HIGH Priority Issues

### H1. Color-Only Status Indication (Accessibility)

**Problem:** Red/yellow/green backgrounds only - colorblind users can't distinguish.

**Options:**
1. Add icons (circle outline/clock/checkmark)
2. Always show text labels
3. Add patterns (stripes, dots)
4. Combination

**Decision:** IMPLEMENT - Icons + text labels (Option C)

**Notes:**
Add both icons and text badges for all shift statuses:
- Open: Circle outline icon + "Open" badge
- Assigned: Clock icon + "Assigned" badge
- Confirmed: Checkmark icon + "Confirmed" badge

**Changes needed:**
- `src/VSMS.Web/Pages/Shifts/Open.cshtml` - Add status indicators
- `src/VSMS.Web/Pages/Shifts/MyShifts.cshtml` - Add icons to status badges
- `src/VSMS.Web/Pages/Admin/Calendar/Index.cshtml` - Add icons to calendar cells
- `src/VSMS.Web/Pages/Admin/Index.cshtml` - Add icons to dashboard table
- Consider using Bootstrap Icons or similar icon library

---

### H2. My Shifts Email Lookup Has Poor Error Message

**Problem:** "No active volunteer found" doesn't help user know what to do.

**Options:**
1. Improve error message with suggestions
2. Add help link/contact info
3. Add "Request help finding my account" link

**Decision:** IMPLEMENT - Better error message

**Notes:**
Change error message to: "We couldn't find that email. Try the email you used when signing up, or contact the office if you need help."

**Changes needed:**
- `src/VSMS.Web/Pages/Shifts/MyShifts.cshtml.cs` - Update error message text

---

### H3. Admin Shift Edit Has Hidden Auto-Transitions

**Problem:** Status changes silently when volunteer is assigned/unassigned.

**Options:**
1. Hide status dropdown when volunteer assigned
2. Make status dropdown readonly with explanation
3. Remove status dropdown entirely (auto-managed)
4. Add explanatory text above dropdowns

**Decision:** IMPLEMENT - Remove status dropdown entirely

**Notes:**
Status is always auto-managed:
- No volunteer = Open
- Volunteer assigned = Assigned
- Admin confirms = Confirmed

No need to expose this to admin - just show current status as read-only text if needed.

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Calendar/EditShift.cshtml` - Remove status dropdown, optionally show status as text label

---

### H4. Calendar Shows Nothing When Month Not Generated

**Problem:** New admins see empty calendar with no explanation.

**Options:**
1. Show "Month not yet generated" message with link
2. Add workflow steps indicator (1. Generate → 2. Edit → 3. Publish)
3. Auto-prompt to generate on first visit

**Decision:** N/A - Resolved by C3

**Notes:**
Generate flow is being removed (C3). Calendar will show rolling 3-month window automatically. If no shifts exist, just show empty calendar - no special messaging needed.

---

### H5. "Backup Volunteer" Has No Explanation

**Problem:** IsBackup badge shown but never explained.

**Options:**
1. Add tooltip on all badges
2. Add help text in create/edit forms
3. Add documentation/FAQ page

**Decision:** REWORK - Backup is a slot type, not a person type

**Notes:**
Current model is wrong. `Volunteer.IsBackup` marks a person as backup.

Correct model:
- Each shift has 1 primary slot + up to 2 backup slots
- Any volunteer can be assigned to any slot type
- Backup slots shown on My Shifts page and admin views

**Changes needed:**
- Remove `IsBackup` from Volunteer entity
- Add `Backup1VolunteerId` and `Backup2VolunteerId` to Shift entity (or create ShiftAssignment table)
- Update admin shift editing to allow assigning backups
- Update My Shifts to show backup assignments
- Update volunteer create/edit forms to remove IsBackup checkbox

---

### H6. Error Page Shows Technical "Request ID"

**Problem:** Technical info meaningless to users.

**Options:**
1. Remove request ID from display (keep in logs)
2. Add friendly error message
3. Add specific contact info
4. Add "Try again" and "Go home" buttons

**Decision:** IMPLEMENT - Add friendly message, keep request ID

**Notes:**
Add helpful text above the request ID:
- "Something went wrong. Please try again or contact the office."
- "If the problem continues, reference this ID when contacting support:"
- Keep request ID for troubleshooting

**Changes needed:**
- `src/VSMS.Web/Pages/Error.cshtml` - Add friendly message, reframe request ID as support reference

---

### H7. CSV Import Has No Template Download

**Problem:** Users must create CSV from scratch following text instructions.

**Options:**
1. Add "Download Template" button
2. Add sample data in template
3. Show preview before importing

**Decision:** REMOVE - Delete import feature entirely

**Notes:**
Data will be seeded once, then all volunteer management happens in-app. No need for ongoing CSV import.

**Changes needed:**
- Remove `/admin/import` page
- Remove Import nav item
- Remove Import from Quick Actions on dashboard

---

## MEDIUM Priority Issues

### M1. Mobile Calendar Requires Horizontal Scrolling

**Decision:** IMPLEMENT - Card-based view on mobile

**Notes:**
Use responsive design to show card/list view on small screens instead of table grid.

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Calendar/Index.cshtml` - Add responsive breakpoint with card layout for mobile

---

### M2. No HTMX Loading Indicators

**Decision:** IMPLEMENT - Add loading indicators

**Notes:**
Add spinners/disabled states during HTMX requests.

**Changes needed:**
- Add `htmx-indicator` class with spinner to HTMX triggers
- Disable buttons during processing
- Add to modals, approval buttons, and other HTMX elements

---

### M3. "Role assigned by coordinator" is Vague

**Decision:** IMPLEMENT - Show actual role on shift cards

**Notes:**
Role is already determined on the shift. Display "In-Person" or "Phone" instead of vague text.

**Changes needed:**
- `src/VSMS.Web/Pages/Shifts/Open.cshtml` - Show role badge on open shift cards

---

### M4. Duplicate Request Only Detected After Form Submit

**Decision:** IMPLEMENT - Check on page load

**Notes:**
If email cookie exists, check for existing pending request before showing form. Show message if already requested.

**Changes needed:**
- `src/VSMS.Web/Pages/Shifts/Request.cshtml.cs` - Check for existing request in OnGet if email cookie present
- `src/VSMS.Web/Pages/Shifts/Request.cshtml` - Show "already requested" message instead of form

---

### M5. Quick Actions Lack Context

**Decision:** IMPLEMENT - Clean up per earlier decisions

**Notes:**
Remove Generate Month and Import Data buttons (removed in C3 and H7). Keep "Add Volunteer" action.

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Index.cshtml` - Remove Generate/Import quick actions

---

### M6. Time Slot Shows Duration Not End Time

**Decision:** IMPLEMENT - Show calculated end time

**Notes:**
Display "9:00 AM - 12:00 PM" instead of "180 minutes"

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/TimeSlots/Index.cshtml` - Calculate and display end time

---

### M7. Volunteer Filter Not Visually Indicated

**Decision:** IMPLEMENT - Add filter indicator

**Notes:**
Show active filter as badge/summary (e.g., "Showing: Active volunteers")

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Volunteers/Index.cshtml` - Add filter summary text above table

---

### M8. Add Admin Help Text Mentions Only Google

**Decision:** IMPLEMENT - Update text to mention both providers

**Notes:**
Change "must have a Google account" to "must be able to sign in with Google or GitHub"

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Settings/Admins.cshtml` - Update help text

---

## LOW Priority Issues

### L1. "Phone" Role Name Ambiguous
**Decision:** WON'T FIX - Users understand the term

### L2. No Pagination on Volunteer List
**Decision:** IMPLEMENT - Add pagination

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Volunteers/Index.cshtml.cs` - Add pagination logic
- `src/VSMS.Web/Pages/Admin/Volunteers/Index.cshtml` - Add page navigation controls

### L3. Only First Name Shown on Calendar
**Decision:** IMPLEMENT - Show "John S." format

**Changes needed:**
- `src/VSMS.Web/Pages/Admin/Calendar/Index.cshtml` - Display first name + last initial

### L4. No Undo for Destructive Actions
**Decision:** IMPLEMENT - Add confirmation dialogs

**Changes needed:**
- Add confirmation dialogs for: reject request, deactivate volunteer, remove volunteer from shift
- Can use simple browser confirm() or Bootstrap modal

### L5. Date Formats Inconsistent
**Decision:** IMPLEMENT - Standardize formats

**Changes needed:**
- Define standard formats: full ("Monday, February 1") for primary display, short ("Feb 1") for compact views
- Update all date formatting across pages to use consistent patterns

### L6. No Help Documentation
**Decision:** IMPLEMENT - Add tooltips/help

**Changes needed:**
- Add tooltips on complex features and icons
- Consider adding brief help text on key admin pages

### L7. Success Messages May Be Missed
**Decision:** IMPLEMENT - Use toast notifications

**Changes needed:**
- Replace TempData alerts with toast notifications (Bootstrap toasts or similar)
- Position fixed in corner, auto-dismiss after 5 seconds

---

## Summary

| Priority | Total | Implement | Rework | Defer | Won't Fix |
|----------|-------|-----------|--------|-------|-----------|
| Critical | 4 | 1 | 1 | 2 | 0 |
| High | 7 | 4 | 1 | 0 | 1 (N/A) |
| Medium | 8 | 8 | 0 | 0 | 0 |
| Low | 7 | 6 | 0 | 0 | 1 |

### Major Reworks Required
1. **C3** - Remove Master Schedule / Generate / Publish flow entirely. Simplify to rolling 3-month calendar. **DONE**
2. **H5** - Change backup from person-type to slot-type. Each shift gets primary + 2 backup slots. **DONE**

### Features to Remove
- Master Schedule pages **DONE**
- Generate Month flow **DONE**
- Publish Month flow **DONE**
- CSV Import feature **DONE**
- `IsBackup` flag on Volunteer entity **DONE**

### Deferred (Need Email System First)
- C1: Email notifications on publish
- C4: Request new link for expired tokens

---

## Implementation Progress

### Phase 1: Structural Removals ✅ COMPLETE
- Removed Master Schedule pages and entity
- Removed Generate Month flow
- Removed Publish Month flow
- Removed CSV Import feature
- Removed `IsBackup` from Volunteer entity
- Removed `MonthPublishedAt` from Shift entity
- Removed Status dropdown from EditShift (H3)
- Updated tests and fixed all build errors

### Phase 2: Backup Slot Model ✅ COMPLETE
- Added `Backup1VolunteerId` and `Backup2VolunteerId` to Shift entity
- Added `SlotType` enum (Primary, Backup1, Backup2)
- Added `RequestedSlot` to ShiftRequest entity
- Updated Open Shifts page to show all available slots (primary + backup)
- Updated Request page to handle slot parameter
- Updated My Shifts page to show volunteer's role (Primary/Backup 1/Backup 2)
- Updated Admin Calendar to show backup count indicator (+1, +2)
- Updated Admin EditShift to allow editing backup volunteers
- Updated Admin Requests to show requested slot and approve to correct slot
- Created migrations: `AddBackupVolunteerSlots`, `AddRequestedSlotToShiftRequest`

### Phase 3: Core UX Fixes ✅ COMPLETE
- [x] C2: Add pending requests section to My Shifts page
- [x] H1: Add Bootstrap Icons, status badges with icons in calendar legend
- [x] H2: Improve error message in My Shifts email lookup
- [x] H6: Improve Error page with friendly message and Go Back button
- [x] M2: HTMX loading indicators on EditShift and Requests buttons
- [x] M3: Show shift role (In-Person/Phone) on Open Shifts page with icons
- [x] M4: Check for duplicate request on page load if cookie exists
- [x] M6: Show end time instead of duration on Time Slots page
- [x] M7: Show filter indicator with result count on Volunteers page
- [x] M8: Update admin add text to mention Google or GitHub
- [ ] M1: Mobile responsive calendar (deferred - more complex)

### Phase 4: Polish (TODO)
- [ ] L2: Pagination on volunteer list
- [ ] L3: Show "John S." format on calendar
- [ ] L4: Confirmation dialogs
- [ ] L5: Standardize date formats
- [ ] L6: Add tooltips/help
- [ ] L7: Toast notifications
