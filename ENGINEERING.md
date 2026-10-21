# Engineering practices

_Started 2026-10-20. What is in place, what is deliberately not yet, and why._

## Running things locally

```bash
# Backend: needs a Mongo on localhost:27017
docker run -d --name radar-mongo -p 27017:27017 mongo:7
ASPNETCORE_ENVIRONMENT=Development dotnet run --project RadarV2.csproj --urls http://127.0.0.1:5080

dotnet test tests/RadarV2.Tests/RadarV2.Tests.csproj   # no database needed

# Frontend
cd radar-v2-frontend && npm ci && npm run dev
npm test && npm run lint && npm run build
```

Local secrets go in `appsettings.Development.local.json`, which is gitignored and loaded last.
Never put a real key in `appsettings.json` or `appsettings.Development.json` — both are tracked.

## Tests

`tests/RadarV2.Tests` (xUnit) and `radar-v2-frontend/src/**/__tests__` (vitest).

**These are regression locks, not coverage.** Every case was a bug that actually shipped and was
reported — "Foundations of Get Promotion", a timestamp reading "-1479554m ago", a feed field
rendered but never requested, a nurse being handed the data-analytics curriculum. The suite is
deliberately concentrated on pure logic, which is where almost all of the user-visible damage has
come from and which needs no database to test.

The suite was mutation-checked when written: reintroducing the "Get Promotion" bug failed four
tests. A test that cannot fail is worse than no test, because it buys confidence it has not earned.

**Not covered yet, and worth knowing:** controllers, Mongo services, and anything requiring a live
database or the LLM. Those still need the manual loop — start Mongo, register a user, exercise the
endpoint.

## CI

`.github/workflows/ci.yml` in both repos, on every push to `main` and every pull request.

- **Backend:** restore, build Release, run tests, and build the Docker image. The image is built
  because the app ships as a container, so a broken `Dockerfile` breaks the deploy even when the
  solution compiles.
- **Frontend:** `npm ci` (not `install` — CI must use the committed lockfile), lint, test, build.

`main` deploys straight to Railway and Vercel, so CI is currently the *only* gate between a commit
and production.

## Branching

```
feature/something ──PR──> staging ──PR──> main
                          (staging env)    (production)
```

- **`main`** is production. Nothing lands here that has not run on staging.
- **`staging`** deploys the staging environment. This is where a change is actually exercised.
- **`feature/*`**, **`fix/*`** — one branch per piece of work, branched from `staging`.

Both branches run the same CI, and every pull request runs it too. Branch names are a convention,
not enforcement: GitHub branch protection on `main` is what makes the flow real rather than
optional, and is worth turning on.

Why a feature branch rather than committing to `staging` directly: a branch is reviewable and
revertible as a unit. Most of this codebase's worst bugs — a canned work review identical for any
input, a goal pasted into a title, a field rendered but never requested — were small, plausible
diffs that nobody looked at twice. A PR is the cheapest place to look twice.

Keep a change on one branch per concern. Two unrelated fixes in one branch means reverting one
reverts both.

## Known gaps

Listed rather than quietly carried:

- **Environment separation is coded but not yet provisioned.** `appsettings.Staging.json`, the
  `/api/version` endpoint, per-environment frontend builds and the non-production banner are in.
  The Railway and Vercel services still need creating — runbook in `RAILWAY_DEPLOY.md`.
- **Branch protection is not enabled.** The `staging` → `main` flow is documented and the branches
  exist, but nothing prevents a direct push to `main`. Enable protection on `main` (require a PR,
  require CI to pass) to make it real.
- **No rate limiting.** AI calls cost real money against a monthly cap; one loop can exhaust it,
  and the only symptom is output quality quietly degrading.
- **Input validation on roughly a third of controllers.**
- **No dependency scanning** (Dependabot/Renovate).
- **Three service files over 1,000 lines**, mixing catalogue data with logic. This is why adding a
  domain once meant finding three separate keyword branches.
- **The production Atlas credential committed in plaintext has still not been rotated.** Flagged in
  `HANDOFF.md` since 2026-09-11. Highest-severity open item, and a dashboard action, not a code
  change.

## Conventions worth keeping

- **Never fabricate.** The recurring failure in this codebase is plausible invented content: a
  canned work review identical for a banana bread recipe and a security incident, research papers
  with generated DOIs, a "~520 words" badge over a summary that did not exist. When something
  cannot be produced, say so and keep what the user gave you.
- **Domains are data, not branches.** Scattered keyword `if`s are how a cybersecurity learner got
  an Excel curriculum. Adding a domain should be one row.
- **Degrade honestly.** A fallback should be visibly a fallback, never an imitation of the real
  thing.
