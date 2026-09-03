# Tasks: Define Volunteer Privacy Retention and Deletion Lifecycle

**Issue**: #16
**Date**: 2026-09-03
**Status**: Approved
**Author**: RussellBTech

---

## Summary

| Phase | Tasks | Status |
|-------|-------|--------|
| Policy and domain | 2 | [ ] |
| Lifecycle | 2 | [ ] |
| Presentation | 1 | [ ] |
| Verification | 2 | [ ] |
| **Total** | 7 | |

---

## Phase 1: Policy and Domain

### T001: Record approved privacy classification and policy

**File(s)**: `steering/product.md`; `steering/tech.md`; privacy configuration documentation
**Type**: Modify
**Depends**: None
**Acceptance**:
- [ ] contact, destination, bearer token, token hash, workflow history, audit, and backup classifications are explicit
- [ ] identifying contact retention is exactly 365 elapsed UTC days after the deterministic anchor when no live dependency exists
- [ ] verified earlier removal and indefinite non-identifying workflow/audit retention are explicit
- [ ] coordinator identity retention and provider-log deletion remain outside this volunteer policy
- [ ] the policy describes service scheduling without membership or attendance claims

### T002: Add irreversible volunteer anonymization transitions

**File(s)**: `src/VolunteerCoordinator.Domain/Volunteers/Volunteer.cs`; `src/VolunteerCoordinator.Domain/Requests/ShiftRequest.cs`; `src/VolunteerCoordinator.Domain/Notifications/NotificationAttempt.cs`; `src/VolunteerCoordinator.Infrastructure/Persistence/VolunteerCoordinatorDbContext.cs`; new EF Core migration
**Type**: Modify
**Depends**: T001
**Acceptance**:
- [ ] anonymization writes only neutral unique tombstones, clears phone, timestamps removal, and permanently rejects contact restoration
- [ ] request status links can be administratively invalidated and notification destinations can be idempotently redacted
- [ ] the migration adds only lifecycle fields/indexes and stores no copy/hash of original contact values
- [ ] existing surrogate relationships and non-identifying workflow history remain valid

---

## Phase 2: Lifecycle

### T003: Implement atomic privacy lifecycle command

**File(s)**: `src/VolunteerCoordinator.Application/VolunteerCoordinatorService.cs`; `src/VolunteerCoordinator.Application/Ports/IWorkflowStore.cs`; `src/VolunteerCoordinator.Infrastructure/Persistence/EfWorkflowStore.cs`
**Type**: Modify
**Depends**: T002
**Acceptance**:
- [ ] post-lock eligibility uses the greatest volunteer/request/assignment/notification/shift timestamp and exact 365-day cutoff
- [ ] pending requests, assigned/confirmed assignments, and future commitments block both automatic and coordinator removal
- [ ] eligible execution invalidates status/action links, redacts destinations, anonymizes contact, and writes one minimal audit in one transaction
- [ ] coordinator removal bypasses only age, while automatic retention enforces it
- [ ] empty collections short-circuit and batch reads avoid per-related-record queries
- [ ] failures and concurrency conflicts roll back every contact, token, destination, and audit mutation

### T004: Run bounded idempotent retention sweeps

**File(s)**: new retention options/hosted service in Application or Web composition according to layer ownership; `src/VolunteerCoordinator.Web/Program.cs`; `src/VolunteerCoordinator.Web/appsettings.json`
**Type**: Create / Modify
**Depends**: T003
**Acceptance**:
- [ ] validated defaults are 365 retention days, 24-hour interval, and 100-record batches; configuration cannot shorten the approved period
- [ ] a startup sweep and periodic sweep use `IClock`, cancellation, fresh scopes, ordered candidates, and bounded transactions
- [ ] shared volunteer-row locking serializes retention with request and assignment contact reuse
- [ ] concurrent workers produce one anonymization and audit per volunteer
- [ ] one record failure logs only surrogate ID/reason and does not stop later candidates

---

## Phase 3: Presentation

### T005: Add notices and coordinator-assisted removal

**File(s)**: `src/VolunteerCoordinator.Web/Pages/Privacy.cshtml`; contact-collection Razor Pages; new `/Coordinator/Privacy` Razor Page; shared privacy notice; privacy options/configuration
**Type**: Create / Modify
**Depends**: T003
**Acceptance**:
- [ ] the concise notice precedes every contact-form submit and links to a complete public privacy page
- [ ] purpose, field optionality, coordinator visibility, 365-day lifecycle, backup rotation, and configured removal contact are plain text
- [ ] exact normalized-email search plus one selected recent commitment gates the confirmation action
- [ ] absent/already-removed searches are generic; live dependencies return category/count and no partial mutation
- [ ] success redirects with `Volunteer contact data removed. Non-identifying scheduling history was retained.`
- [ ] all pages describe service scheduling and never characterize AA membership or attendance

---

## Phase 4: Verification

### T006: Add lifecycle, privacy, and concurrency coverage

**File(s)**: `tests/VolunteerCoordinator.UnitTests/`; `tests/VolunteerCoordinator.IntegrationTests/`
**Type**: Create / Modify
**Depends**: T002, T003, T004
**Acceptance**:
- [ ] fixed-time tests cover every anchor source, the exact boundary, future/live blockers, no-phone storage, tombstones, and no restoration
- [ ] PostgreSQL tests prove atomic token invalidation, destination redaction, audit minimization, rollback, row-lock races, batch bounds, and concurrent idempotency
- [ ] old status/action links fail without mutation and original contact byte strings are absent from current application tables after removal
- [ ] a restored expired record is anonymized before notification/use and an already-anonymized record remains unchanged

### T007: Verify actual privacy flows and version enhancement

**File(s)**: Web integration tests; `VERSION`
**Type**: Modify / Verify
**Depends**: T005, T006
**Acceptance**:
- [ ] increment the implementation branch's current root version by one minor component
- [ ] browser verification covers public notice/privacy text, optional phone, coordinator search/match/block/success, neutral historical display, and mobile accessibility
- [ ] anonymous/non-allowlisted callers cannot inspect or execute coordinator removal, and antiforgery remains enforced
- [ ] source/rendered-copy regression finds no membership or attendance characterization
- [ ] formatting, Release build, full isolated-PostgreSQL tests, migration application, and actual Web smoke scenario pass

---

## Dependency Graph

```text
T001 ──▶ T002 ──▶ T003 ──┬──▶ T004 ──▶ T006 ──▶ T007
                          └──▶ T005 ─────────────▶ T007
```

---

## Change History

| Issue | Date | Summary |
|-------|------|---------|
| #16 | 2026-09-03 | Initial feature spec |
