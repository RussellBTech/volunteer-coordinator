# Design: Give Maintainers an Efficient Stewardship Workspace

**Issue**: #22
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Extend issue #17 rather than create a second dashboard or navigation model. Home keeps concise counts/examples and gains one `Open prioritized work` primary action. `/Coordinator/Work` supplies the full bounded queue. The #17 known-volunteer choice becomes search-backed; its recent human history becomes structured filterable history; issue #14's handoff runbook gains an in-app readiness/verification page.

Application query models own authoritative filtering/correlation. Infrastructure executes set-based PostgreSQL projections. Web owns wording, protected cursors, forms, and responsive presentation. No coordinator page queries `DbContext` directly and no cache is introduced to mask N+1 loading.

Prerequisites: #14 production/OIDC operations, #15 group-local time, #16 privacy/anonymization, #17 guided home/actions, #19 Messages, #20 recurring review, and #21 recurring participation/withdrawal.

---

## Prioritized Work Projection

Add `CoordinatorWorkQuery` and `CoordinatorWorkPageDto`. `CoordinatorAttentionOptions` validates `UrgentHours > 0`, `SoonHours > UrgentHours`, defaults 24/72, page size 50, and maximum examples from Home remains three.

One shared Application classifier produces both Home counts and Work rows from a single PostgreSQL snapshot/transaction where practical. Categories:

- pending one-time/recurring request;
- uncovered published concrete commitment;
- Assigned/waiting-for-confirmation commitment;
- Pending/RetryScheduled/Failed/Bounced/Complained current notification;
- future effective recurring withdrawal/reopened cadence risk;
- #20 Needs review gap or Zone review required series;
- #21 recurring handoff required.

Only commitments with `EndsAtUtc > now` are actionable. Severity/order:

1. **Urgent:** starts at/before `now + 24h`, ordered uncovered, message/access failure, unconfirmed, pending decision, then start.
2. **Soon:** starts after 24h and at/before `now + 72h`, same category precedence then start.
3. **Upcoming:** later pending decisions, recurrence/zone/handoff review, and explicit future withdrawal coverage, ordered effective/start date.

Overdue still-live rows sort before future start. Identical items deduplicate by category plus authoritative entity identity. A row contains plain category/severity, #15 commitment context, person display if authorized/available, state, deadline/effective date, and one intervention route kind with opaque server route value. It never contains a raw token or rendered URL in Application logs.

Work supports fixed category/severity filters and 50-row keyset paging by `(SeverityRank, DueAtUtc, CategoryRank, StableId)`. Home reuses counts and first three rather than recomputing category rules. When empty: `You're caught up. No coordinator action is needed right now.`

Interventions route to existing narrow pages: review request; assign known/new; send replacement/cancel/reassign; resend/reissue; resolve recurrence gap/zone; review withdrawal/handoff. There is no inline or bulk mutation on Work.

---

## Privacy-Aware Volunteer Search

Replace #17's full select materialization with `SearchAssignableVolunteersAsync(term, limit=10)`. Add `Volunteer.NormalizedName`, populated/updated with the same trim/uppercase invariant as normalized email and cleared to the tombstone form by #16 anonymization. Add btree indexes for prefix search on normalized name and email.

Search rules:

- POST-only Razor handler under Coordinator authorization and antiforgery, so contact terms never enter URL/query/access logs;
- trim and require 3–100 characters;
- normalize uppercase invariant;
- prefix match normalized name or email; exact email sorts first, then name/email/ID;
- `AnonymizedAtUtc IS NULL` and only current assignable contact rows;
- return at most 10 records with VolunteerId, Name, Email; never phone, history, address counts, unrelated results, or total count.

Render results as labelled radio/list choices with one `Choose volunteer` action. Selection POSTs only VolunteerId and slot/commitment context. Final assignment command reloads/locks volunteer and rechecks non-anonymized eligibility; stale result says `That volunteer is no longer available. Search again.` Input/results remain on validation failure. No autocomplete JSON API or broad roster page is added.

Performance does not depend on `%contains%` or trigram extensions. Prefix behavior is explained beside the field: `Enter the beginning of a name or email (at least 3 characters).`

---

## Structured Audit Correlation

Extend `AuditEntry` with nullable indexed `ShiftId` and `VolunteerId` correlation fields while preserving immutable action, actor, entity, timestamp, and DetailJson. Update every audit factory/call site to provide known correlations explicitly; do not parse JSON for new writes.

Migration backfills existing rows only where PostgreSQL `jsonb` contains a syntactically valid known `ShiftId`/`VolunteerId`, or where entity relationships deterministically resolve them from Request/Assignment/Shift IDs. Unresolvable fields remain null; do not guess. Existing DetailJson is unchanged.

Indexes:

- descending `(OccurredAtUtc, Id)` keyset;
- `(Actor, OccurredAtUtc, Id)`;
- `(Action, OccurredAtUtc, Id)`;
- partial `(ShiftId, OccurredAtUtc, Id)` and `(VolunteerId, OccurredAtUtc, Id)`.

For removed volunteers, VolunteerId correlation remains non-identifying and the UI label is `Removed volunteer` plus a short opaque reference generated by Web for distinguishing history. Do not expose the GUID.

---

## Filterable Human History

Upgrade `/Coordinator/Audit` presentation title/navigation to `History`. Add `AuditHistoryQuery`: optional group-local From/Through dates, normalized actor selected from known audit actors, shift selected through title/local-date search, volunteer selected through the protected search flow, fixed `AuditCategory`, direction cursor.

Categories map internal actions to plain sets: Schedule, Requests, Assignments, Volunteer actions, Messages, Privacy, Recurring schedules, Recurring commitments, Access. Unknown actions map Other. Filters resolve stable IDs server-side and use protected opaque values in URLs/forms; visible text never requires a GUID/action code.

Use 50-event descending keyset pages ordered `(OccurredAtUtc DESC, Id DESC)`. A cursor contains boundary timestamp/ID, normalized filter fingerprint, and direction, protected with ASP.NET Core Data Protection. A cursor used with different filters, invalid protection, or malformed data returns the first page plus `History position expired. Showing the newest matching events.` It never throws or exposes content. Previous/Next navigation uses boundary cursors; no offset or total-count query.

`AuditHistoryDto` contains local occurrence text inputs, actor display, category, and a human summary assembled from structured correlation and authoritative related records. Cover every action introduced through #21. Examples: `Jordan published Tuesday's Greeter commitments.` and `Message delivery bounced for Tuesday's Backup 1 commitment.` Unknown historical action renders `A recorded coordinator action occurred.` Existing raw JSON/entity/action/ID stay available only in PostgreSQL/operational diagnostics, not routine HTML.

Date filters use #15 group zone: From inclusive at local midnight; Through inclusive calendar date represented as exclusive next local midnight with explicit DST resolution. Results preserve immutable occurrence UTC.

---

## Coordinator Access and Handoff Diagnostics

Add `/Coordinator/Access`. Read `Oidc` configuration presence (never values for client secret), distinct normalized `Coordinator:AllowedEmails`, current authenticated normalized email, and latest `CoordinatorAccessVerified` audit per allowed actor.

Show:

- OIDC: `Configured` only when authority/client ID/client secret are all present; otherwise `Needs operational setup` without secret detail;
- Coordinators configured: distinct count and sorted normalized emails;
- current user marker;
- each email's latest explicit verification date or `Not yet verified`;
- overall `Handoff ready` only when OIDC is complete, at least two distinct emails are allowlisted, and at least two currently allowlisted identities have verified access.

`OnPostVerifyAsync` is coordinator-authorized and antiforgery-protected. It records `CoordinatorAccessVerified` with the current normalized actor and no secret/config values, then redirects with `Your independent coordinator access was verified.` It does not edit the allowlist.

Inline steps, also linked to #14 operations guidance:

1. Operational owner adds the incoming person's own verified OIDC email to Railway configuration and redeploys.
2. Incoming coordinator signs in with their own credentials and selects `Verify my access`.
3. Existing coordinator confirms both addresses show verified.
4. Only then does the operational owner remove the outgoing address and redeploy.
5. Remaining coordinator signs in again and verifies schedule access.

The page states that shared credentials are prohibited and configuration remains external. Historical verification for a removed email remains in audit but not in current readiness count.

---

## Set-Based Query and Performance Contract

Replace remaining coordinator per-row lookups with bounded projections/batch loads. Instrument integration tests using EF Core command interception, not timing alone. Budgets exclude authentication/session setup and count SQL commands per completed page query:

| Surface | Maximum SQL commands |
|---------|----------------------|
| Home counts/examples | 6 |
| Work page (50 rows) | 6 |
| Requests page (50 rows) | 5 |
| Coverage page (50 rows) | 5 |
| Messages page (50 rows) | 5 |
| History page (50 rows plus filter choices) | 6 |
| Volunteer search (10 rows) | 1 |

Run each at representative data and at ten times rendered result volume; command count must stay within the same budget. Fixed additional queries per filter are allowed only within budget. No lazy loading, looped store call, unbounded `Include`, full-table materialization, or application cache.

Representative fixture includes at least 1,000 volunteers, 5,000 concrete shifts/occurrences, 10,000 audits, and enough requests/assignments/messages/recurring exceptions to fill each page. Performance proof records command count and query plan/index use; wall-clock latency is observational, not a flaky pass threshold.

---

## Web and Accessibility

Home/Work/History/Search/Access follow #17 language and components. At 320px rows become labelled cards; desktop may use tables. Filter forms have explicit Apply/Clear actions and preserve selected values. Keyset navigation has descriptive labels. Search result focus returns to result heading; validation summary receives focus on errors through normal server navigation.

All tasks work with keyboard and assistive technology, 44px targets, textual severity/state, one primary action, no hover/drag/color-only controls, and no horizontal page scrolling. Raw IDs, JSON, SQL/provider/OIDC protocol details, cursor internals, and personal search terms are absent from routine visible/error/log output.

---

## Security and Privacy

- Every workspace route and query is CoordinatorOnly; mutations/search POSTs use antiforgery.
- Search returns only matching active name/email, bounded to ten, with no query-string/log term.
- Audit correlation retains surrogate references after anonymization but human display remains neutral; DetailJson never renders.
- Protected cursors bind to filter fingerprint and reveal no raw IDs.
- Access page shows allowlisted emails only to allowlisted coordinators and reveals secret presence, never secret value.
- Work actions delegate to existing authorized/concurrent commands and never mutate inline.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Application | Unit | Severity/category ordering, dedup, audit category/summary fallback, local-date bounds, cursor filter binding. |
| PostgreSQL | Integration | Structured audit backfill/index/filter, keyset stability, search bounds/privacy, set-based command budgets at scaled data, authoritative queue counts. |
| Web | Integration | Authorization/antiforgery, POST search no URL leak, filters/cursors, stale cursor recovery, access verify/readiness, input preservation. |
| Browser/accessibility | Actual UI | No-training daily queue, narrow intervention, search/select, history investigation, two-coordinator handoff, keyboard/320px/textual severity. |

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #22 | 2026-09-03 | Initial feature spec |
