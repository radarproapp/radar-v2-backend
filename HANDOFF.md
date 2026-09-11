# Radar — Project Handoff

_Last updated: 2026-09-11 · For the next agent/session. Read `REACT_MIGRATION.md` for the full plan._

---

## 1. What this project is

**Radar** ("Africa's Intelligence Operating System") — a personal-intelligence product:
it ingests trusted news/research/podcasts, turns them into personalised briefings
("what happened, why it matters, what to do"), and connects reading → learning
roadmaps → opportunities → portfolio projects for one user at a time.

The active codebase is **`RadarV2/`** — a **.NET 10 Blazor Server app** backed by
**MongoDB** (Atlas in prod, localhost in dev). ~35 routed Razor pages across 4 layout
shells, 19 service interfaces with Mongo implementations, and a background ingestion
pipeline (RSS/Atom, Mediastack, PodcastIndex, Taddy, OpenAlex, Groq Whisper,
OpenRouter LLM enrichment).

The workspace root also contains **`radar-app-redesign-planning/`** and `App/` +
`.dc.html` design prototypes (a UI redesign that has **not** been implemented in code —
separate thread, don't conflate).

> ✅ **Git repo initialized 2026-09-11** at the workspace root (`C:\Users\ACER\RiderProjects\RadarV2\`,
> one level above `RadarV2/`), baseline commit `52ec3f3`. Along the way the committed
> production MongoDB Atlas credential was stripped from `appsettings.json` and
> `AZURE_DEPLOY.md` before it entered history — **still needs rotating in the Atlas
> dashboard**, since it was previously exposed in plaintext.

## 2. The active mission: React frontend, .NET backend

**Decision made (with the user):** migrate the UI from Blazor Server to a **React SPA**
that deploys as static files, keeping the .NET backend (services, Mongo, ingestion)
unchanged. Driver: Blazor Server hosting is "limited and expensive to maintain"
(always-on SignalR circuits per user).

- **`RadarV2/REACT_MIGRATION.md`** — the full plan. Read it: target architecture,
  §4 decisions (recommended: JWT auth, identical URLs, keep `app.css`, TanStack
  Query), §6 service→endpoint inventory, §7 route table, phases 0–4, risks.
- Phase 1 (API foundation) and Phase 2 (React shell + flagship vertical) are **done**
  — details below. Phase 3 (bulk port, section by section) is next.

## 3. What has been built so far

### Phase 2 — React shell + flagship vertical (✅ done 2026-09-11)

New `web/` folder at the workspace root: Vite + React 19 + TypeScript + React Router 7
+ TanStack Query. Ported end-to-end against the real API (not mocked):

| Area | Files |
|---|---|
| Auth | `src/auth/AuthContext.tsx` (JWT in `localStorage`, `GET /api/me` query), `src/auth/RequireAuth.tsx` (route guard mirroring `SessionAwareBase`: no token → `/login`, incomplete profile → `/onboarding`) |
| Pages | `src/pages/{Login,Onboarding,Today,Feed,FeedDetail,Saved,NotFound}.tsx` |
| Shell | `src/components/MainLayout.tsx` (top nav, avatar menu, sign-out), `src/components/{LoadingSkeleton,EmptyState,ErrorState}.tsx` |
| Lib | `src/lib/{api.ts,types.ts,date.ts,layers.ts}` — typed fetch client, TS mirrors of the C# models, `DateHelper.Humanize` port, extracted `ContentLayer` label/colour/bg maps |
| Styles | `src/styles/app.css` — the existing `wwwroot/app.css` copied verbatim, per plan §4 |

Route → component map so far: `/` (Today), `/feed`, `/feed/:itemId`, `/saved`,
`/login`, `/onboarding` (all 8 wizard steps). Everything else nav-linked falls through
to a placeholder `NotFound` page until Phase 3 ports it.

**Backend additions made to support this vertical** (small, additive — no existing
service interfaces changed):
- `Controllers/TodayController.cs` — `GET /api/today` (wraps `INavigatorService`).
- `Controllers/RoadmapsController.cs` — `POST /api/roadmaps/active/topics` (wraps
  `IRoadmapService`, auto-creating the active roadmap from template if the user
  doesn't have one yet, same as the Blazor page relied on).
- `Program.cs` — added CORS (`AddCors`/`UseCors`) allowing `localhost:5173` /
  `127.0.0.1:5173`; JWT bearer means no cookies cross the origin, so no
  `AllowCredentials` needed. Revisit allowed origins again in Phase 4 (deployment).

**Bugs found and fixed while wiring this up** (pre-existing, not introduced by this
phase):
- `FeedController` save/unsave/`saved` endpoints keyed saved items by the raw JWT
  subject (auth `users` collection id), while every read path
  (`MongoIntelligenceFeedService.GetFeedAsync`'s `IsSaved` check, and the Blazor pages
  themselves) keys by the **profile id**. Saving via the API silently never showed up
  as saved. Fixed to resolve the profile first, consistently, in all three endpoints.
- FeedDetail's "Add to roadmap" passes `item.Topic`, which is empty for freshly
  ingested/un-enriched content (most real content today, since LLM keys are empty) —
  the new endpoint's validation rejected empty topics with a 400. Frontend fix: fall
  back to `item.title` when `topic` is blank.
- **Known, not fixed (pre-existing, cosmetic):** `IIntelligenceFeedService.GetByIdAsync`
  never sets `IsSaved` (only `GetFeedAsync` does, via a separate saved-ids lookup), so
  the Save button on a freshly-loaded FeedDetail page shows "Save" even if the item was
  saved earlier — toggling it still works and persists correctly. Same behaviour
  existed in the original Blazor `FeedDetail.razor`. Would need widening
  `GetByIdAsync`'s signature to take a profile id; left alone to avoid touching a
  shared service interface for a cosmetic issue.

**Verification done:** full live smoke test (register → onboarding all 8 steps → me →
today → feed list + filters → feed detail (persona tabs, save, add-to-roadmap) → saved
→ unsave) against a throwaway Docker Mongo with the real ingestion worker running
(picked up hundreds of real RSS items during the test). Driven with a headless
Playwright browser, screenshots checked visually, `console`/network errors checked —
none beyond expected unsplash placeholder image aborts on fast navigation. Test
server, Vite dev server, and Mongo container all cleaned up afterward.

### Phase 1 — API foundation (✅ done 2026-09-07)

### New API layer (in the existing project, Blazor untouched)
| File | Endpoints |
|---|---|
| `RadarV2/Controllers/AuthController.cs` | `POST /api/auth/register`, `POST /api/auth/login` → signed JWT (30-day) |
| `RadarV2/Controllers/MeController.cs` | `GET /api/me`, `PUT /api/me`, `POST /api/me/onboarding-complete` |
| `RadarV2/Controllers/FeedController.cs` | `GET /api/feed` (type/page/pageSize), `GET /api/feed/{id}`, `POST/DELETE /api/feed/{id}/save`, `GET /api/feed/saved`, `GET /api/feed/search` |

- `Program.cs`: added `AddControllers` (camel-case JSON + string enums), JwtBearer
  auth + `[Authorize]`, JSON 401/403 challenge handlers, and a **session-seed
  middleware** that copies JWT `sub`/`name` claims into the scoped
  `IUserSessionService` — so pre-existing services (`IUserProfileService` etc.) work
  for API requests with **zero service-layer changes**.
- Dev signing key: `Auth:Jwt` in `appsettings.Development.json` (code fallback exists
  for local dev).
- NuGet added: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.11.

### Pre-existing bug fixed en route
`Services/Ingestion/SourceRegistry.cs` crashed on first use (static field init order —
`All` spread still-null arrays → `ArgumentNullException`), which **prevented the app
from starting at all** in any environment. Fix: build `All` in a static constructor.
Build warnings dropped 54 → 27.

### Verification done
Full live smoke test (register → login → me → PUT profile → onboarding-complete →
personalised feed → error paths 401/400/404/204) against a throwaway Docker Mongo.
All green. Test server + container cleaned up.

## 4. Auth model (how it works now)

- **Users:** Mongo `users` collection, `UserDocument` { Id, Email (lowercased),
  PasswordHash (**BCrypt**), ProfileId → `profiles` `UserProfile` }.
- **Old UI flow (still live in Blazor):** in-memory scoped session +
  `ProtectedLocalStorage` key `radar_session` (`StoredSession(UserId, UserName)`);
  `SessionAwareBase` bounces to `/login` (no session) or `/onboarding`
  (profile incomplete).
- **New API flow:** register/login → `Authorization: Bearer <JWT>`; Program.cs seeds
  the same scoped session from claims. `[Authorize]` on all `/api` controllers.
- **Onboarding gating for the SPA later:** `GET /api/me` returns `onboardingComplete`;
  the React router must mirror the Blazor redirects above.

## 5. Codebase state at a glance

- **Wired to real services (Mongo):** Today/Home, Feed, FeedDetail, Saved, WeeklyBrief,
  Learn/Roadmap, LearnHub, Projects, Progress, Mentor (LLM), Ask Radar (LLM), Capture,
  Clips, Opportunities, Research, Notebook, Library, Sources, Topics, Plans, Profile
  pages. LLM/ingestion API keys are mostly **empty** in config → enrichment features
  need keys or stubs to work.
- **Mock / static (do not mistake for live):** `/admin` dashboard (hard-coded data,
  no DB), `/notifications` (hard-coded list), `/entry` (design "Reference" gallery),
  Compare loads canned `cmp1`, marketing pages (`/landing`, `/product`,
  `/how-it-works`, `/institutions`) are static, Google/Apple auth buttons decorative.
- **Shared UI:** `app.css` (~2,160 lines, one-file design system of CSS vars — Space
  Grotesk + DM Sans fonts), `Components/UI/{LoadingSkeleton,EmptyState,ErrorState}.razor`,
  `Helpers/DateHelper.Humanize`. Razor-only: `ReconnectModal`, SignalR circuit, default
  template `NavMenu.razor` (dead boilerplate — ignore).
- **Routes live in Razor `@page` directives** (see REACT_MIGRATION.md §7 table for the
  full route→component map). The React app (`web/`) now covers `/`, `/feed`,
  `/feed/:itemId`, `/saved`, `/login`, `/onboarding` — everything else is still
  Blazor-only until Phase 3.

## 6. Known issues / open items

- **Unmatched `/api/*` URLs** (typos) return the Blazor HTML not-found page (correct
  404 status, wrong body). Real endpoints return JSON. Cosmetic; fix cleanly when the
  Blazor UI is retired. (A terminal middleware was attempted; razor status-page
  re-execute wins — revisit then.)
- **⚠️ Security:** production MongoDB Atlas credentials were committed in
  `appsettings.json` and `AZURE_DEPLOY.md` — stripped from both files in the baseline
  commit (2026-09-11), but the credential itself is still live until **you rotate it
  in the Atlas dashboard** (not something an agent can do) and set the new connection
  string via the `MongoDB__ConnectionString` env var.
- **`FeedDetail`'s Save button can show stale state on direct load** — see §3 Phase 2
  "known, not fixed" note above (`GetByIdAsync` doesn't set `IsSaved`). Pre-existing in
  Blazor too; toggling still works.
- **Decisions still open** (§4 of the plan): final hosting targets (ASWA/CDN vs
  Vercel-class), whether to add SSE chat streaming now, final production CORS origins
  (currently only `localhost:5173` dev is allowed).
- Blazor Server UI remains the source of truth for everything outside the ported
  vertical, and still runs side-by-side — nothing has been removed.

## 7. How to run & verify locally

Requires .NET SDK 10 (10.0.203 present), Node (v24 present), and a MongoDB on
`localhost:27017` (dev config `appsettings.Development.json`). Docker Desktop must be
running first:

```bash
docker run -d --name radar-mongo -p 27017:27017 mongo:7
ASPNETCORE_ENVIRONMENT=Development dotnet run --project RadarV2/RadarV2.csproj \
  --no-launch-profile --urls http://127.0.0.1:5080
```

Then, in a second terminal, the React app:

```bash
cd web && npm install && npm run dev   # http://localhost:5173
```

The Vite dev server proxies nothing — it calls the API directly at
`http://127.0.0.1:5080` (CORS-enabled for `localhost:5173`); override with
`VITE_API_BASE_URL` if the API runs elsewhere. Visiting `http://localhost:5173` should
redirect to `/login`; use "Get started" to go through onboarding and land on Today.

Build checks: `dotnet build RadarV2/RadarV2.csproj` and `cd web && npm run build`
(runs `tsc -b` then `vite build`).

## 8. Suggested next steps (next session)

1. ~~`git init` + baseline commit~~ — done 2026-09-11 (`52ec3f3`).
2. ~~Phase 2: React shell + flagship vertical~~ — done 2026-09-11, see §3.
3. **Phase 3:** bulk-port the remaining sections per REACT_MIGRATION.md §7, in the
   recommended order (Intelligence remainder → Learn → Opportunity/Research → Utility
   → Chat → Marketing → Admin). Extend the API per §6 inventory as each section needs
   it.
4. **Rotate the committed Atlas credentials** — still outstanding, must be done in
   the Atlas dashboard, then set the new connection string via the
   `MongoDB__ConnectionString` env var / local secrets, never back in
   `appsettings.json`.
5. Decide production CORS origins and hosting targets before Phase 4 (deployment).
