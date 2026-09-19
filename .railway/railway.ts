import {
  defineRailway,
  github,
  postgres,
  preserve,
  project,
  service
} from "railway/iac";

export default defineRailway((context) => {
  const database = postgres("Postgres");
  const web = service("volunteer-coordinator", {
    source: github("RussellBTech/volunteer-coordinator", { branch: "master" }),
    healthcheck: "/health/ready",
    healthcheckTimeout: 100,
    env: {
      ASPNETCORE_FORWARDEDHEADERS_ENABLED: preserve(),
      ConnectionStrings__Postgres: preserve(),
      Oidc__Authority: preserve(),
      Oidc__ClientId: preserve(),
      Oidc__ClientSecret: preserve(),
      Coordinator__AllowedEmails__0: preserve(),
      Coordinator__AllowedEmails__1: preserve()
    }
  });

  return project(context.projectName ?? "volunteer-coordinator", {
    resources: [web, database]
  });
});
