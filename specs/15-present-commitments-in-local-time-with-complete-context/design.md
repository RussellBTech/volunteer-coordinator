# Design: Present Commitments in Local Time With Complete Context

**Issue**: #15
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Overview

Add one small domain aggregate for group presentation settings and one shared application commitment projection. PostgreSQL still stores every workflow instant as UTC. The Web boundary formats UTC instants in the configured group zone; schedule commands accept local wall times and the Application boundary resolves them back to UTC using an explicit offset when daylight-saving rules produce two candidates.

The implementation adds no browser-time-zone detection and no client-side conversion. Every user sees the same group-local commitment. Existing `Shift.Notes` remains private coordinator context. A new `VolunteerInstructions` field is deliberately projected to volunteer surfaces.

---

## Domain and Persistence

### `GroupSettings`

Create `VolunteerCoordinator.Domain/Settings/GroupSettings.cs` as a singleton aggregate with a fixed well-known `Guid Id`, required `TimeZoneId`, and PostgreSQL `xmin` `Version`. The aggregate stores a trimmed IANA identifier only; Application resolves and validates the identifier through `TimeZoneInfo`, including `HasIanaId`, before creation or change. `Configure` records the new identifier without embedding platform time-zone objects in Domain.

Add `DbSet<GroupSettings>`, map the singleton key and required bounded zone string, and map `Version` to `xmin` consistently with `Shift`. `IWorkflowStore` adds tracked `GetGroupSettingsAsync`, `AddGroupSettings`, and the existing transaction/audit mechanisms remain authoritative. The migration creates `GroupSettings` and adds nullable `VolunteerInstructions` to `Shifts`; it does not alter existing UTC values or seed a guessed zone.

The service command `ConfigureGroupTimeZoneAsync` accepts the IANA ID, nullable expected version for first configuration, an explicit `confirmDisplayChange` flag, coordinator email, and cancellation token. Initial configuration creates the singleton; later changes require the current version and confirmation. It writes `GroupTimeZoneConfigured` or `GroupTimeZoneChanged` with old/new IDs but no schedule or volunteer data in one transaction. Concurrency failure uses the existing reload-and-retry wording.

### Shift context

Extend `Shift.Create` and `Shift.Edit` with nullable `volunteerInstructions`, normalized and limited to 1,000 characters. Preserve `Notes` and label it `Internal coordinator notes` everywhere. Audit shift create/edit details may state whether instructions exist but must not duplicate note or instruction content.

---

## Local-Time Input Resolution

Create Application models for local schedule input and resolution. A local input contains `DateTime` values with `Kind.Unspecified`, optional selected UTC offsets for start and end, and the expected `GroupSettings.Version`. A resolution result supplies either authoritative UTC instants or candidate choices for each ambiguous field. Each candidate contains the exact local wall time, numeric UTC offset, resulting UTC instant, and deterministic zone label.

Resolution follows this order for both start and end:

1. Load group settings; missing settings returns the configuration-required result.
2. Reject non-IANA or unavailable identifiers.
3. Treat posted local values as `DateTimeKind.Unspecified`; never convert through the server's local zone.
4. If `TimeZoneInfo.IsInvalidTime` is true, reject with `This local time does not exist because the clocks move forward. Choose another time.`
5. If `IsAmbiguousTime` is true and no matching candidate offset was posted, return both interpretations ordered by resulting UTC instant. The Web form redisplays radio choices such as `1:30 AM CDT (UTC-05:00)` and `1:30 AM CST (UTC-06:00)`.
6. If a selected offset is not one of the current zone candidates, reject as stale input. Otherwise calculate the UTC instant directly from the wall time and selected offset.
7. For an unambiguous time, derive the zone offset and reject any posted offset that does not match.
8. Require end UTC to be after start UTC. Calculate duration from UTC instants so an interval crossing a DST change is accurate.

Create/Edit POST handlers first request resolution. If choices or errors are returned, they redisplay without calling a mutation. The mutation command receives the local values, chosen offsets, and expected settings version, reloads settings inside its transaction, repeats resolution, and writes only if the same authoritative result is still valid. This prevents a concurrent zone change from silently changing an entered commitment.

---

## Shared Commitment Projection

Add `CommitmentDto` in Application with shift ID/title, UTC start/end, group time-zone ID, location, slot label, and volunteer-visible instructions. Compose it into `OpeningDto`, `RequestStatusDto`, `ActionInspectionDto`, `CoordinatorRequestDto`, `CoverageDto`, assignment/link page results, and the coordinator `ShiftDto`. Queries load the singleton settings once and never expose `Shift.Notes` through `CommitmentDto`.

Web owns a single `GroupTimeFormatter` and a reusable `_CommitmentDetails.cshtml` partial. The formatter uses the DTO's IANA zone, not `TimeZoneInfo.Local` or request culture, and produces stable text in this shape:

`Tuesday, November 3, 2026, 6:30 PM–8:00 PM CST (UTC-06:00) · 1 hour 30 minutes`

When the start and end fall on different local dates, render both dates in full. Include semantic `<time datetime="...Z">` elements for start and end, visible labels for Start, End, Duration, Time zone, Location, Slot, and Instructions, and explicit `Not provided` text for optional location/instructions. CSS may adapt layout but must not remove text.

Use the partial on public openings, request form, request status, and action inspection, plus coordinator schedule, request queue, assign/reassign, action-link, and coverage views. Coordinator schedule edit additionally shows internal notes outside the shared volunteer context.

---

## Unconfigured and Changed-Zone Behavior

Before `GroupSettings` exists:

- `/Coordinator/Settings` remains available and is the primary schedule call to action.
- Coordinator schedule/create/edit routes explain that a zone must be configured before schedules can be displayed or mutated.
- Public openings, request, request-status, and assignment-action GETs render `Commitment times are temporarily unavailable. Please contact the coordinator.` without a UTC fallback.
- Their POST handlers perform the settings check before request creation, token lookup, or action mutation and return the same unavailable state for valid and invalid identifiers.

Changing a configured zone requires a checked consequence confirmation: existing UTC instants remain fixed, while all displayed local dates/times may change. The update is audited. It does not rewrite shifts, requests, assignments, or tokens. After commit, every query uses the new zone immediately.

---

## Security and Privacy

- `Shift.Notes` remains reachable only through authenticated coordinator schedule DTOs/pages. Public models and the shared partial contain only `VolunteerInstructions`.
- Unconfigured private-token routes short-circuit before token hashing/lookup, preserving valid/invalid indistinguishability.
- Razor encoding remains the only rendering path for location and instructions; no raw HTML is accepted.
- Settings updates use existing coordinator authorization, antiforgery, normalized identity, transactions, optimistic concurrency, and audit conventions.
- Audit details store zone identifiers and entity identifiers, never private token values, volunteer contact data, or note/instruction text.

---

## Performance Considerations

Group settings is one row. Each service query loads it once, then projects all commitments in memory alongside existing batch reads. Do not query it per row or per partial. Time-zone resolution objects may be cached by immutable zone ID in a bounded singleton dictionary, but correctness must not depend on caching; avoid a new cache abstraction unless profiling demonstrates need.

No historical UTC data is rewritten when the zone changes. The schema migration adds only one small table and one nullable shift column.

---

## Testing Strategy

| Layer | Type | Coverage |
|-------|------|----------|
| Domain | Unit | Singleton settings transition, bounded volunteer instructions, coordinator notes retained. |
| Application | Unit | IANA validation, UTC preservation, DST gap rejection, two overlap candidates, explicit/stale offset selection, elapsed duration across DST, settings-version conflict. |
| PostgreSQL | Integration | Migration, singleton identity, `xmin` concurrency, settings/shift audit atomicity, instruction persistence and notes privacy. |
| Web | Integration/browser | Local controls, two-step overlap choice, unconfigured uniform state/no mutation, every commitment surface, semantic and textual labels, narrow viewport. |

Use fixed zones and instants including `America/New_York` spring gap `2026-03-08 02:30`, autumn overlap `2026-11-01 01:30`, a cross-midnight commitment, and a zone without DST. Never derive expected values from the host local zone or current clock.

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #15 | 2026-09-03 | Initial feature spec |
