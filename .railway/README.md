# Railway Infrastructure as Code

`.railway/railway.ts` is the project-level Railway configuration. It preserves the existing Web source, PostgreSQL resource, and sealed runtime variables while changing the Web deployment health check to `/health/ready`.

Install the pinned evaluator from the repository root with `npm ci`. In an authenticated Railway CLI session linked to the production project and environment:

```powershell
railway config pull --force
railway config plan --detailed-exit-code --out railway-plan.json
```

Review the plan before applying it. Stop if it deletes or recreates a service or database, replaces a sealed variable, changes a volume, or mutates an unrelated setting. Apply only the reviewed plan, then run `railway config plan --detailed-exit-code` again and require no drift.

The Railway IaC graph does not carry the approved Web pre-deploy dashboard setting in this repository's migration contract. Configure and verify this exact Web service command separately in Railway before a migration-bearing release:

```text
dotnet VolunteerCoordinator.Web.dll --migrate-only
```

Do not put decrypted variables, connection strings, OIDC secrets, volunteer data, or raw action/status tokens in this file or in plan output.
