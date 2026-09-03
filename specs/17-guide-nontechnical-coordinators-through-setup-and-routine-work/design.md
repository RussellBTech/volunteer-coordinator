# Design: Guide Nontechnical Coordinators Through Setup and Routine Work

**Issue**: #17
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Add one coordinator home projection and focused server-rendered review flows inside the existing Razor Pages application. Do not add a client application, wizard framework, content system, or page-local workflow mutation. `VolunteerCoordinatorService` and `IWorkflowStore` remain the query/command boundary; PostgreSQL remains authoritative.

Issue #15 must supply group settings and local commitment presentation before implementation, and issue #13 must supply atomic deactivation and coordinator cancellation. This issue consumes those contracts. It does not pull self-scheduling from #21 or notification retry/resend from #19 into scope.

---

## Coordinator Home

Map `/Coordinator` to `Pages/Coordinator/Index`. Change successful production/development login redirects and the authenticated brand/home navigation to this route. Keep direct routes bookmarkable.

Add `CoordinatorHomeDto` with:

- setup mode and five ordered `SetupStepDto` rows;
- pending request count;
- uncovered current/future commitment count;
- unconfirmed assignment count;
- current/future failed-message count;
- up to three most urgent labelled examples per nonzero category;
- the single recommended next action.

`IWorkflowStore` implements one bounded projection or a fixed set of batch queries; never call existing per-row service methods in a loop. Counts use `_clock.UtcNow` and current authoritative states. Failed messages are actionable only when their transition resolves to a request/assignment on a shift with `EndsAtUtc > now`; historical failures do not inflate the home. The visible wording is `Messages not sent`, never adapter/provider terminology.

### Setup mode

Setup mode remains active until at least one shift is published. Render these steps in order:

1. **Set your group time zone** — complete when `GroupSettings` exists; link to `/Coordinator/Settings`.
2. **Understand volunteer requests** — always displays the supported policy: `Volunteers request a commitment. A coordinator reviews each request before anyone is assigned.` It is informational, not persisted configuration.
3. **Create the first schedule entry** — complete when any active shift exists; link to local-time create form.
4. **Review what volunteers will see** — active when an unpublished shift exists; link to its server-rendered publish preview.
5. **Publish open commitments** — complete when any shift is published; final explicit confirmation.

Show one primary button for the earliest incomplete actionable step. Informational completed/current steps remain visible with textual state. No hidden “wizard progress” row is persisted; authoritative settings/shift state derives progress.

### Routine mode

Order attention cards: `Requests to review`, `Open commitments`, `Waiting for confirmation`, `Messages not sent`. Each nonzero card shows count, plain-language examples with local date/title/slot/person where applicable, and one link to the corresponding filtered Requests, Coverage, or Messages view. Zero categories are omitted. If all are zero, render `You're caught up. No coordinator action is needed right now.` plus a secondary schedule link.

Add simple named filter parameters (`attention=pending`, `attention=uncovered`, `attention=unconfirmed`, `attention=message`) accepted only from a fixed allowlist. Visible headings state the applied filter; URLs never require entity GUIDs.

---

## Safe Review and Confirmation

Use dedicated Razor Page review modes, not browser `confirm()` dialogs. Every preview is an authenticated GET followed by an antiforgery-protected POST. Hidden identifiers/versions remain implementation inputs but are never rendered as visible text.

- **Publish shift:** show title, group-local start/end, location, volunteer instructions, every active slot, and the fact that openings become public.
- **Deactivate shift:** use issue #13's authoritative preview query to show pending-request and active-assignment counts, affected volunteer names/slots/local dates, link invalidation, and removal from public openings.
- **Cancel assignment:** show volunteer name, commitment/slot/local date, that the slot becomes open, and that action links stop working.
- **Replace volunteer:** first collect either a known volunteer selection or new contact data, then show current and replacement names, commitment/slot/local date, prior-link invalidation, and notification consequence before mutation.

Each preview has one danger/primary confirm button whose label names the result (`Publish commitments`, `Deactivate and resolve`, `Cancel assignment`, `Replace volunteer`) and an ordinary link/button `Go back without changes`. The back path performs no POST and no command.

The final POST reloads/revalidates preview state through Application and then invokes the existing atomic command. A changed version, assignment, or affected set returns to the preview with `This information changed. Review the updated details before confirming.` and never applies the stale action. Do not trust posted affected counts, names, or consequences.

No bulk mutation is introduced. Extract only a small shared `_ConsequencePreview` partial for heading, consequence list, confirm, and back controls so later bulk work must use the same interaction contract.

---

## Known and New Volunteer Assignment

The assignment landing page presents two clear links:

- `Choose known volunteer` — a form containing an accessible select ordered by name then normalized email, with each option labelled `Name — email`; selection posts a volunteer ID to an application command that reloads the active/non-anonymized volunteer and proceeds to replacement review.
- `Add someone new` — the existing name/email/optional-phone form, with the privacy notice when issue #16 is present, proceeding to the same review stage.

Do not show a collapsible contact directory beside manual fields. Do not copy known contact values into editable hidden fields. Final confirmation uses the volunteer ID for known records; new-contact values are server-validated again. A stale removed/missing volunteer returns `That volunteer is no longer available. Choose someone else.` without exposing an identifier.

Review-stage form values are preserved server-rendered on validation failure or when the coordinator chooses `Change selection/details`. Do not put volunteer contact data in query strings, logs, or unprotected client storage. Antiforgery and authorization apply to both stages.

---

## Plain-Language Presentation Contract

Add a Web-owned vocabulary/summary layer; do not change domain enum names solely for copy. Use these visible terms:

| Internal concept | Coordinator text |
|------------------|------------------|
| Pending request | Request to review |
| Available/uncovered slot | Open commitment |
| Assigned | Waiting for confirmation |
| Confirmed | Confirmed |
| Failed notification | Message not sent |
| Supersede/reassign | Replaced/resolved as described by the action |
| Concurrency conflict | Information changed; reload or review again |

Forms use `asp-validation-summary="All"`, explicit labels, `aria-describedby` for help/consequence text, and field-keyed model errors when a specific value is wrong. Valid bound values remain on the same Page result. Cross-record/concurrency errors appear in the summary and identify the next recovery action without stack, SQL, provider, JSON, status code, or UTC terminology.

Replace the routine Audit table's visible entity GUID, action code, and raw `DetailJson` with a human summary projection derived from known audit actions: local occurrence time, actor display, action sentence, commitment/person context where available. Unknown historical actions use `A recorded coordinator action occurred.` and local time rather than exposing raw JSON. Authoritative `AuditEntry` persistence is unchanged.

Replace notification warnings such as `No transactional notification provider is configured` with `Message could not be sent. Contact the volunteer another way.` Link current/future failures from the home to a read-only `/Coordinator/Messages` list with person, commitment, local time, message purpose, and direct manual follow-up destination/action. Issue #19 later adds retry/resend; this issue adds none.

---

## Accessibility and Responsive Behavior

Use semantic headings, ordered lists for setup, nav landmarks, real links/buttons, associated labels, status/alert roles, and a logical DOM/focus order. Minimum touch target is 44 by 44 CSS pixels. At 320 CSS pixels, cards stack, tables use the existing labelled responsive pattern or cards, and controls never require horizontal page scrolling.

Keyboard verification covers skip link, navigation, setup steps, filters, select controls, previews, back links, confirmations, and validation recovery. Focus moves to the validation summary or page heading after a response using server navigation; no hover content, drag/drop, color-only status, icon-only action, or auto-advancing step exists.

---

## Security and Privacy

- Every coordinator route remains protected by `CoordinatorOnly`; every mutation remains antiforgery-protected and audited through existing commands.
- Preview GETs are read-only. Final POSTs recompute authoritative consequences and reject stale state.
- Known-volunteer lists are coordinator-only, exclude anonymized records, and never place contact values in URLs/logs.
- Human audit/message summaries may display coordinator-authorized operational contact but never raw tokens, hashes, JSON, or unnecessary provider errors.
- Setup and routine wording preserves explicit deactivation, link invalidation, privacy, and request-approval consequences.

---

## Performance Considerations

Home counts and examples use bounded batch queries and indexed active/status/time predicates. Load no more than three examples per category. Do not materialize full audit/notification history to compute attention.

Preview queries batch affected requests, assignments, volunteers, slots, and shifts once. A preview is advisory; the command repeats required reads transactionally. Known volunteers remain bounded by the existing dataset for the first release, but the select query excludes anonymized records and returns only ID/name/email.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Application/PostgreSQL | Integration | Setup/routine derivation, current/future message correlation, count/example limits, preview accuracy, stale confirmation rollback, known-volunteer assignment. |
| Web | Integration | Login redirect, authorization, filters, form preservation, field/summary errors, no-mutation back paths, antiforgery, no raw technical values. |
| Browser | Actual Razor UI | Empty setup through publication using visible text only; routine interventions; keyboard order; 320px/desktop layout; consequence review; accessible names/status. |
| Regression | Content/behavior | No new signup policy, bulk mutation, retry/resend, UTC instruction, provider wording, raw GUID/JSON, or hidden privacy/security consequences. |

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #17 | 2026-09-03 | Initial feature spec |
