# CI/CD Strategy

## Status

Planned for the current foundation phase.

## Objective

Introduce an automated quality gate before the project advances to Business / Location.

The immediate goal is **Continuous Integration (CI)**. Continuous Delivery/Deployment (CD) will be introduced incrementally when the application has a deployable API and target Azure environments.

## CI — current phase

GitHub Actions will execute automatically for pull requests targeting `main` and for pushes/merges to `main`.

The pipeline must:

1. checkout the repository;
2. install/use the project's .NET 10 SDK;
3. restore dependencies;
4. build the solution in `Release`;
5. provision PostgreSQL 18 as an isolated service for integration tests;
6. provide `TEST_DATABASE_CONNECTION_STRING` to the test process without committing credentials;
7. apply/validate EF Core migrations through the existing PostgreSQL test fixture;
8. run Domain, Application, Architecture and Integration test projects;
9. fail the workflow when build or tests fail.

The PostgreSQL integration suite remains authoritative for behaviors that depend on the real database engine, including tenant isolation and future scheduling/concurrency guarantees.

## Quality gate and main protection

After the CI workflow is stable, `main` should be protected so that changes arrive through pull requests and the required CI checks must pass before merge.

The intended development flow is:

```text
feature/*
   |
   v
Pull Request -> main
   |
   v
GitHub Actions CI
   |
   +-- Restore
   +-- Build (Release)
   +-- PostgreSQL 18
   +-- EF Core migrations
   +-- Domain tests
   +-- Application tests
   +-- Architecture tests
   +-- Integration tests
   |
   v
Required checks green
   |
   v
Merge -> main
```

## CD / Release — later phase

A production deployment pipeline is intentionally deferred. At the current stage, provisioning Azure runtime resources, release environments and production deployment would add operational complexity before there is a useful application slice to deploy.

When the first deployable API is ready, the delivery flow can evolve toward:

```text
main
  |
  v
Build/version artifact or container
  |
  v
Development
  |
  v
Staging
  |
  v
Production
```

That phase should include, as appropriate:

- Azure target runtime;
- infrastructure as code with Terraform;
- environment-specific secrets in a managed secret store;
- controlled database migration strategy;
- deployment health checks;
- rollback strategy;
- Application Insights/observability;
- environment approvals where useful.

## Principles

- CI now; production CD when there is something meaningful to deploy.
- Do not commit credentials or connection strings containing secrets.
- Use real PostgreSQL for database-dependent integration tests.
- Keep the pipeline proportional to the MVP; no Kubernetes or unnecessary deployment infrastructure.
- A green pipeline is a prerequisite for merge once branch protection is enabled.
- CI complements local testing; it does not replace domain, architecture, integration or security-focused tests.

## Implementation order

1. Add the GitHub Actions CI workflow.
2. Validate it in a pull request.
3. Make the CI check required for `main`.
4. Continue with Business / Location under the protected PR workflow.
5. Introduce CD/Release when the first deployable API/environment is defined.
