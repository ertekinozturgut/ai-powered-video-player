# VideoAtlas development

Use the existing checkout. Cloud tasks are isolated; do not create a worktree unless the user explicitly requests one.

Read README.md, docs/PLAN.md, docs/OPERATIONS.md before changing workflows. Agent Squad's upstream standards and source are in vendor/agent-squad, pinned to the commit recorded in docs/PLAN.md. This project adapts those standards; do not run the upstream example init script over the real solution or replace real tests with its Company fixtures.

- .NET 10, Clean Architecture; Domain has no framework dependencies; Application only depends on Domain.
- All input and AI text is untrusted. No raw HTML rendering. API keys stay server-side.
- Preserve video ownership, antiforgery on authenticated mutations, capability expiration/revocation, range streaming, and frame-ancestor restrictions.
- Use TimeProvider and async cancellation. Keep package/signature/TLS checks enabled.
- SQLite + media + Data Protection keys share a durable data volume. Only one app instance may own that volume.
- Use migrations for schema changes; never EnsureDeleted/EnsureCreated over user data.
- Verify with `dotnet test VideoAtlas.sln`, `CHROMIUM_PATH=/usr/bin/chromium npm run test:e2e` when that browser exists, and release publish.
- No external AI tests without configured credentials and a deliberate real sample. Controlled provider tests are not live-provider validation.
- Record actual test evidence in docs/VALIDATION.md. Do not mark CodeQL/Opengrep/Stryker as passed if unexecuted.
