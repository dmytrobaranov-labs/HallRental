# HallRental — Claude Code guide

Conference-hall booking REST API. Portfolio project: code quality and clean git history matter as much as features.

## Stack
- .NET 10, ASP.NET Core Minimal APIs, EF Core 10 (SQL Server / Azure SQL), in-memory repository when no connection string
- JWT bearer auth, roles `Admin` / `User` (claim `role`), policy `AdminOnly`
- xUnit: `tests/HallRental.UnitTests`, `tests/HallRental.IntegrationTests` (WebApplicationFactory)
- Azure: App Service F1 + Azure SQL free offer + Key Vault + App Insights, IaC in `infra/main.bicep`, CI/CD in `.github/workflows/deploy.yml`

## Commands
```bash
dotnet restore HallRental.slnx
dotnet build HallRental.slnx -c Release
dotnet test HallRental.slnx -c Release
dotnet run --project src/HallRental.API          # Swagger at /swagger
dotnet ef migrations add <Name> --project src/HallRental.API --output-dir Data/Migrations
```
A task is not done until `dotnet build` has zero warnings and `dotnet test` is green.

## Workflow rules
- One bug / feature = one branch = one PR. Branch names: `fix/<short-name>`, `feat/<short-name>`, `chore/<short-name>`.
- Never commit to `master` directly. Never force-push.
- Conventional Commits in English: `fix: return 409 on booking conflict`. Small, focused commits; no "restore accidentally removed..." style commits — if something broke, fix it in the same branch before the PR.
- Write a failing test first that reproduces the bug, then fix it (red → green → refactor).
- Do not change behaviour that is outside the task. If you notice another problem, list it in the PR description under "Out of scope", do not fix it.
- Do not add NuGet packages without stating why in the PR description. Pin exact versions (no `10.0.*`, `1.*`).
- Schema change ⇒ EF migration in the same PR, and mention it in the PR description.
- PR description: Problem → Cause → Fix → Tests → Out of scope.

## Language
- All code, identifiers, comments, XML docs, API messages, commits, PRs and README: **English**.
- When you touch a file that still has Ukrainian comments/messages, translate them in that file only.

## Clean code
- Meaningful names; no abbreviations except well-known ones (`Id`, `Dto`, `Utc`).
- Small methods with one responsibility; prefer early returns over nesting.
- No magic numbers/strings: constants or options classes (`IOptions<T>`).
- Comments explain *why*, not *what*. Delete commented-out code.
- No dead code, no test-only production constructors (the parameterless `HallService()` should go when tests are refactored).
- Follow `.editorconfig`; file-scoped namespaces; `var` when the type is obvious; nullable reference types respected — no `!` to silence warnings without a reason.
- SOLID pragmatically: depend on interfaces at boundaries (repository, time, pricing), not everywhere.

## API conventions
- Errors are RFC 7807 **ProblemDetails** (`AddProblemDetails` + `IExceptionHandler`).
  - validation error → 400, not found → 404, booking conflict → 409, unexpected → 500 with a generic message (details only in logs, never in the response).
- Never `throw new Exception(...)`. Use specific domain exceptions (e.g. `ValidationException`, `HallNotFoundException`, `BookingConflictException`) or return `Results.*` directly.
- `POST` that creates a resource returns 201 with a `Location` that actually resolves (`GET` endpoint must exist).
- Route groups under `/api/v1/...`; every endpoint has `.WithName`, `.WithSummary`, `.Produces...` metadata.

## Time
- API accepts `DateTimeOffset`; the domain and the database store **UTC**.
- Never call `DateTime.Now` / `DateTime.UtcNow` in business code — inject `TimeProvider` (use `FakeTimeProvider` in tests).
- Pricing rules (peak/evening/morning hours) are evaluated in the business time zone from configuration (`Pricing:TimeZone`, default `Europe/Kyiv`).
- Booking intervals are half-open `[start, end)`: overlap ⇔ `a.Start < b.End && a.End > b.Start`.

## Money
- `decimal` only. Round once, at the end, `MidpointRounding.AwayFromZero`, 2 decimals.

## Testing
- Test names: `Method_Scenario_ExpectedResult`.
- Arrange / Act / Assert, one behaviour per test; use `[Theory]` for tables of cases (pricing!).
- Integration tests assert status codes **and** ProblemDetails shape for error paths.
- Do not hardcode JWTs in tests; generate them from the test configuration's signing key.
- No `Thread.Sleep`, no dependence on the real clock or test order.

## Security
- No secrets in code or `appsettings.json`. Local dev key lives only in `appsettings.Development.json` / user-secrets; Azure uses Key Vault references.
- `Auth:DemoTokenEndpointEnabled` stays `false` outside Development unless explicitly asked.
- Do not log tokens, connection strings or request bodies with personal data.

## Azure / CI
- Free tier only: App Service `F1`, Azure SQL `useFreeLimit` with `AutoPause`. Never change SKUs to paid ones.
- Do not run `az` commands that create/delete resources; propose them in the PR description instead.
- CI must stay green; deploy job runs only from `master`.

## Don't
- Don't edit generated migration files by hand (except removing a broken migration together with its snapshot).
- Don't rename public API routes or DTO properties without saying so in the PR as a breaking change.
- Don't reformat whole files unrelated to the change.
