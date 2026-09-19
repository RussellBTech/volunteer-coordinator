# Production Operations

This runbook covers the Railway deployment, PostgreSQL recovery, and coordinator handoff for Volunteer Coordinator. It assumes an authenticated operator has access to the linked Railway project and its production environment. Never paste production secrets, connection strings, OIDC tokens, volunteer contact data, or raw status/action tokens into source, tickets, plan artifacts, or logs.

## Deployment safety

### Link and import

Install the repository-pinned IaC evaluator, authenticate with the Railway CLI, and select the existing project and production environment:

```powershell
npm ci
railway login
railway link
railway config pull --force
```

`railway config pull` is the authoritative import step. Review the resulting `.railway/railway.ts` against the linked environment before editing it. Sealed values must remain `preserve()`; do not use `--include-variables` or `--decrypt-variables` in a shared terminal or CI log.

The repository no longer contains a service-level `railway.toml`. Do not reintroduce one: the project-level IaC file is the only deployment configuration source.

### Plan, apply, and drift check

Create a pinned plan and inspect every resource before applying it:

```powershell
railway config plan --full --detailed-exit-code --out railway-plan.json
```

The approved change is limited to the Web service health check changing to `/health/ready`; the existing Web source, service name, PostgreSQL resource, volumes, variables, and unrelated services must remain intact. Stop without applying if the plan shows any service or database deletion/recreation, volume replacement or detachment, sealed-variable replacement, source change, replica change, or unrelated mutation.

Apply only the reviewed pinned plan:

```powershell
railway config apply --plan railway-plan.json
railway config plan --full --detailed-exit-code
```

The final plan must report no drift. Keep the reviewed plan and its redacted output with the release evidence; never use `--show-values` for production evidence.

### Migration gate and health checks

Railway's Web service dashboard must contain this exact pre-deploy command:

```text
dotnet VolunteerCoordinator.Web.dll --migrate-only
```

The current approved Railway migration contract keeps this setting in the dashboard rather than inventing an unsupported IaC property. Verify the command in the Web service deploy settings before every migration-bearing release and record the setting plus the release identifier in the deployment evidence. A failed command must stop the deployment; do not start the serving Web process manually after a failed migration.

The migrate-only process applies pending EF Core migrations and exits without binding an HTTP port. Ordinary Web startup never migrates. Railway deployment admission uses `GET /health/ready`, which is unhealthy when PostgreSQL cannot be reached. `GET /health` is process liveness only and remains healthy while the process is serving, even when PostgreSQL is unavailable.

A release gate is complete only when the migration command succeeds, `/health/ready` returns a 2xx response against the intended database, and the Railway deployment reports healthy. Do not use a public request or action link to trigger schema work.

### Variables and replicas

Configure these as Railway service variables. Keep OIDC client secrets and database connection values sealed:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | PostgreSQL connection string used by the Web service and migrate-only process. |
| `Oidc__Authority` | Production OIDC issuer. |
| `Oidc__ClientId` | OIDC client identifier. |
| `Oidc__ClientSecret` | OIDC client secret; sealed. |
| `Coordinator__AllowedEmails__0` | First normalized, verified coordinator email. |
| `Coordinator__AllowedEmails__1` | Second distinct normalized, verified coordinator email. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Trusted Railway proxy client-IP processing; keep enabled for the deployed Web service. |
| `AnonymousRateLimits__RequestMutation__PermitLimit` | Positive per-IP request POST limit; default `5`. |
| `AnonymousRateLimits__RequestMutation__Window` | Positive request-mutation window; default `00:01:00`. |
| `AnonymousRateLimits__PrivateTokenRead__Window` | Positive private-token-read window; default `00:01:00`. |
| `AnonymousRateLimits__AssignmentActionMutation__Window` | Positive assignment-action window; default `00:01:00`. |
| `AnonymousRateLimits__PrivateTokenRead__PermitLimit` | Positive per-IP private-token GET limit; default `30`. |
| `AnonymousRateLimits__AssignmentActionMutation__PermitLimit` | Positive per-IP action POST limit; default `10`. |

Use one Web replica for the approved process-local limiter baseline. A multi-replica or multi-region rate-limit design requires a later approved issue.

## Daily backup and recovery-point objective

Configure Railway PostgreSQL automated backups to run at least once every 24 hours and retain more than one daily copy (seven daily recovery points is the operating baseline). The operator records the backup completion timestamp, selected backup timestamp, and achieved recovery point for each drill. A backup older than 24 hours does not meet the approved RPO; do not use it as a successful recovery result.

Where Railway exposes a portable logical export, retain a restricted-access copy. Set `PGURL` from the operator's secret manager in a protected shell without echoing its value:

```powershell
pg_dump --format=custom --no-owner --no-acl --file=volunteer-coordinator-backup.dump $env:PGURL
Remove-Item Env:PGURL
```

The connection value is supplied interactively or by a secret manager and is not committed. Keep backup files encrypted and access-controlled; do not attach them to issue comments or ordinary build artifacts.

## Isolated restore drill

Run a restore against a newly created isolated PostgreSQL target. Never point a restore command at the production database and never overwrite production as a drill.

1. Record the selected backup timestamp and confirm it is no more than 24 hours old.
2. Create a new isolated Railway PostgreSQL resource or a local PostgreSQL target with a distinct name. Set `ISOLATED_DATABASE_URL` from the protected operator environment only after the target exists.
3. Restore into the empty target. For a custom-format export:

   ```powershell
   pg_restore --exit-on-error --no-owner --no-acl --dbname=$env:ISOLATED_DATABASE_URL volunteer-coordinator-backup.dump
   ```

4. Confirm EF migration history and authoritative table presence without printing rows containing personal or token data:

   ```powershell
   psql $env:ISOLATED_DATABASE_URL -v ON_ERROR_STOP=1 -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'
   psql $env:ISOLATED_DATABASE_URL -v ON_ERROR_STOP=1 -c 'SELECT table_name FROM information_schema.tables WHERE table_schema = ''public'' AND table_name IN (''Shifts'', ''ShiftSlots'', ''Volunteers'', ''ShiftRequests'', ''Assignments'', ''ActionTokens'', ''NotificationAttempts'', ''AuditEntries'') ORDER BY table_name;'
   psql $env:ISOLATED_DATABASE_URL -v ON_ERROR_STOP=1 -c 'SELECT ''Shifts'' AS table_name, count(*) FROM "Shifts" UNION ALL SELECT ''ShiftSlots'', count(*) FROM "ShiftSlots" UNION ALL SELECT ''Volunteers'', count(*) FROM "Volunteers" UNION ALL SELECT ''ShiftRequests'', count(*) FROM "ShiftRequests" UNION ALL SELECT ''Assignments'', count(*) FROM "Assignments" UNION ALL SELECT ''ActionTokens'', count(*) FROM "ActionTokens" UNION ALL SELECT ''NotificationAttempts'', count(*) FROM "NotificationAttempts" UNION ALL SELECT ''AuditEntries'', count(*) FROM "AuditEntries";'
   ```

5. Check referential integrity with the schema's foreign keys and verify the token hash length constraints without selecting raw values. Check representative schedule, request, assignment, notification, and audit reads by counts/statuses only.
6. Run the published Web executable's migrate-only command with `ConnectionStrings__Postgres` set to the isolated target. It must exit successfully without opening a listener. If the restored migration history is current, it must make no schema change.
7. Start a non-production Web process only after the restore and migration checks pass. Point it exclusively at the isolated target and exercise a representative coordinator schedule, request queue, coverage, and audit read. Do not send real notifications or expose restored contact/token data.
8. Record the drill timestamp, selected backup timestamp, achieved recovery point, migration-history result, table/integrity result, representative-read result, and operator. Remove the isolated Web process and database only after the evidence is complete. Review any Railway deletion plan before confirming cleanup.
9. Unset protected connection variables and securely remove temporary backup material after retention requirements are met:

   ```powershell
   Remove-Item Env:ISOLATED_DATABASE_URL
   ```

## Rollback

For an application-only failure, use Railway's deployment rollback to the last known healthy image, then re-check `/health` and `/health/ready`. For an IaC change, restore the previously reviewed `.railway/railway.ts`, run a new non-destructive `railway config plan`, and apply only after the plan matches the intended reversal.

Database migrations are forward-only operational changes. Do not run an ad hoc destructive `dotnet ef database update` against production. If a migration-bearing release must be reversed, preserve the failed deployment evidence and backup, roll back the Web image only when it is compatible with the current schema, and use the isolated restore procedure for recovery planning.

## Two-coordinator OIDC handoff

Coordinator continuity is an operational prerequisite, not a startup validation: one allowlisted coordinator remains sufficient for emergency startup. Before removing an outgoing coordinator:

1. Add two distinct verified OIDC identities to `Coordinator__AllowedEmails__0` and `Coordinator__AllowedEmails__1`; do not share a credential or mailbox.
2. Apply the variable change through the reviewed Railway variable workflow, preserving the OIDC secret.
3. Have the incoming coordinator sign in independently through the production OIDC flow and open the coordinator schedule, request queue, coverage, and audit pages.
4. Have the outgoing coordinator sign in independently and confirm the existing schedule remains operable. Record both successful identity checks without recording tokens or personal workflow data.
5. Remove the outgoing identity only after the incoming identity succeeds. Re-plan or verify variables after the removal and repeat an incoming coordinator login.

If only one identity is available during an incident, leave that identity allowlisted and restore the second identity before planned handoff; never block emergency startup by requiring two entries.
