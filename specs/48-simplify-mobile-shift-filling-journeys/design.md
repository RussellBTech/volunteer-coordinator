# Design: Simplify Mobile Shift-Filling Journeys

**Issue**: #48
**Date**: 2026-09-23
**Status**: Approved
**Author**: Volunteer Coordinator product team

## Approach

Use the existing Razor Pages, projections, server-side forms, and CSS. No client application, new controller, query, database schema, or workflow service. This is a focused presentation cutover across the shared header, volunteer one-time and recurring signup/hubs, and coordinator Home/Work/Coverage/Requests/Schedule. Existing deep links and backend decisions remain the source of truth.

## Header and navigation

The authenticated header shows a brand/Home link and a compact three-link primary row: Home, Work, Schedule. A native `<details>` labelled `More coordinator pages` contains Recurring, Requests, Coverage, Messages, History, Access, Settings, Privacy removal, Open shifts, and the existing sign-out POST. The anonymous header exposes Open shifts directly; its `More pages` disclosure contains Privacy and Coordinator sign in. Use real anchors, a `summary` with native Enter/Space behavior, semantic `nav`, and a normal antiforgery-protected sign-out form; avoid custom menu JavaScript, ARIA menu roles, and inaccessible hidden links. At 320px the primary row fits one line, and the closed disclosure is one short row. All links remain navigable when open. Desktop may show the same disclosure to avoid separate duplicated navigation trees.

## Volunteer browse and request

An opening card leads with shift title, local start and slot/status, a short outcome sentence tied to the existing `SignupPolicy`, then one full-width-on-mobile link: `Claim this commitment` or `Request this [slot] slot`. Location remains visible before the action; the existing `_CommitmentDetails` partial stays intact inside a native optional disclosure after the action, retaining full start/end, duration, timezone, location, slot and instructions in server-rendered HTML. Never hide a warning, policy consequence, location, or action within that disclosure. Recurring service remains its own secondary section and link. Existing shift filters/data and direct-claim/approval transaction remain untouched.

Open shifts includes a quiet but visible link to the existing non-enumerating `/Commitments/Recover` form for people who lost their private link, and a same-page shortcut to published recurring series so they are discoverable above a long list of concrete one-time openings. No extra recovery endpoint, query value, or access shortcut is added.

On the request page, show the selected opening compactly and the existing policy consequence immediately before the contact form; use one submit button with the same policy-dependent label, preserve bound values/validation, and leave `_VolunteerPrivacyNotice` in place before submission. Completion continues to issue or explain the private link exactly as before. Hub displays textual request/assignment state, status message, and only already-offered actions, with any destructive cancel visually secondary; do not preempt server eligibility or token lifecycle. Preserve assignment-action preview pages unchanged.

Recurring signup keeps the three ordered inputs and exact server preview, but the pre-preview state offers only `Review exact dates`; after preview, `Claim recurring commitment` or `Send recurring request` becomes the primary submit and reviewing changed inputs remains an explicit secondary action. Exact date results remain visible before final submission. On the recurring private hub, use plain-language status, bring valid Confirm and Withdraw choices ahead of the potentially long occurrence list, and list dates chronologically, retaining explanatory withdrawal consequences, selector, and complete included/unavailable dates. Coordinator and volunteer recurring summaries use plain policy and familiar time-zone labels instead of raw enum or IANA identifiers. No command or eligibility changes.
Coordinator recurring Create, Publish, Revision and Handoff's existing Preview/Save/Confirm buttons, plus schedule Edit's policy preview, use Razor Pages handler attributes so they reach their existing server handlers instead of posting an inert `handler` form field. The regular schedule Save button explicitly targets the unhandled POST after a preview. After server preview recalculates hidden expected versions, mappings or policy consequences, remove only those ModelState attempted values so Razor renders the authoritative reviewed values for confirmation. Keep authoritative commands, validation of user-entered fields, and reviewed publication unchanged.


## Coordinator workflow

Home presents its single recommended setup action or routine `Open prioritized work` first, with Schedule secondary. Work renders authoritative item cards before a secondary native `Filter work` disclosure, so a phone user can act without traversing selectors first. Filter form is server GET and available with no JavaScript. Work links remain through the existing protected `Work/Go` route. An uncovered slot goes to its exact existing Assign page; a pending one-time or recurring request goes to its row on the filtered Requests page using a fragment (no new endpoint or mutation), and stale rows retain the filtered-list recovery path. A zone-review card protects a distinct `recurring-zone` route kind and opens the series Zone page, not an occurrence identified by an unrelated series ID. Labels name the actual intervention by category (e.g. `Assign volunteer`, `Review request`, `Follow up on message`) rather than repeat a category noun. The projection's severity/context/person/state and pagination remain.
Schedule Edit preloads the existing volunteer instructions and converts the saved UTC instants to the configured group zone for its `datetime-local` inputs; saving still resolves/revalidates the submitted local times and authoritative version as before. Policy preview returns the existing canonical `PolicyConsequence(proposedPolicy)` text checked by `EditShiftAsync`, so an eligible reviewed confirmation succeeds rather than failing on two different descriptions. The Save button targets the normal POST rather than repeating preview. No permission or policy transition changes.


Coverage keeps existing table/card data and state labels; open slots foreground Assign volunteer, assigned slots Replace volunteer; volunteer access and cancellation are secondary text/quiet actions. Requests foreground Approve and assign when valid; Decline stays available but quieter and a pending request that cannot be approved still offers Decline. Other states remain read-only. No coordinator POST/form handler changes. Existing preview pages remain intact.

## Responsiveness/accessibility

Constrain flexible elements with `min-width: 0`, wrap long content, use one-column cards on mobile, and remove existing `overflow-x: hidden` masks instead of disguising overflow. Retain 44 CSS-pixel interactive targets and clear focus rings. At 320 CSS pixels, primary header, opening primary action, Work intervention, and coordinator coverage action are available without horizontal page scrolling. Semantic heading levels, proper labels, `summary` keyboard activation, text states, local `<time>` content, and logical DOM order work without JavaScript. Current layout's optional focus enhancement may run, but menu/actions/forms are native.

## Safety and privacy

Do not expose private status/action tokens in public markup outside existing private routes or persist them in new client state. Keep coordinator-only route authorization, antiforgery, audit, authoritative transaction/revalidation, review previews, volunteer privacy disclosure, local-time formatter and capability deadlines unchanged. No visible private contact in public browsing; coordinator work retains authorized minimal person context.

## Verification and delivery

Main performs actual 320px browser screenshots/interaction across anonymous and coordinator pages, keyboard disclosure/tab/focus, no page overflow, request/claim policy, Work intervention to Coverage/Requests, and run-once solution gates. Preserve regression coverage only for meaningful behavior boundaries. Enhancement version rule: Main updates root `VERSION` exactly once to next minor and records the entry in `CHANGELOG.md` at integration; no duplicate bump in this UI slice.
