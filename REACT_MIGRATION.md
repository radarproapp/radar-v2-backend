# RadarV2 → React Frontend Migration Plan

**Status:** Phases 0–2 implemented (see HANDOFF.md); this doc still governs Phases 3–4
**Date:** 2026-09-07
**Applies to:** `RadarV2/` (.NET 10 Blazor Server app, MongoDB-backed)

---

## 1. Why we are doing this

The current UI is **Blazor Server**: every page renders on the server over a live
SignalR circuit. That gives the product a great in-process development model, but it
makes frontend deployment expensive to operate:

- Each connected user holds an always-on server process (circuit) — sticky sessions
  required, server CPU burns per user per render.
- "Frontend deployment" means scaling and patching a .NET server farm, not shipping
  static files to a CDN.
- Hosting cost and operational complexity are disproportionate to what is ultimately
  a content + chat application.

**Goal:** move the UI to a React SPA that is deployable as static files, while keeping
the .NET backend (Mongo repositories, ingestion pipeline, LLM enrichment) exactly as it
is. **This is not a backend rewrite.**

## 2. Verified current-state snapshot (2026-09-07)

- **Pure Blazor Server.** `Program.cs` registers `AddRazorComponents().AddInteractiveServerComponents()`
  and maps `MapRazorComponents<App>()`. **Zero HTTP API endpoints exist**
  (no controllers, no minimal APIs, no `MapGet`/`MapPost`). Pages call Mongo/LLM
  services in-process.
- **~35 routed Razor pages** across 4 layout shells:
  - `MainLayout` — the logged-in app (top nav + session restore + sign-out).
  - `MarketingLayout` — public pages (`/landing`, `/product`, `/how-it-works`, `/institutions`).
  - `OnboardingLayout` — `/login`, `/onboarding`, `/entry`.
  - `AdminLayout` — `/admin` and `/admin/{Page}` (9 sections).
- **19 service interfaces** (see §6) → Mongo implementations, all registered scoped.
  None are callable over the network today.
- **Auth is hand-rolled and server-session based:**
  - Users stored in Mongo (`users`: BCrypt `PasswordHash`, `ProfileId` → `profiles`).
  - `IAuthService.RegisterAsync/LoginAsync` only; no cookies, no JWT, no Identity.
  - Session = in-memory scoped `IUserSessionService` seeded from
    `ProtectedLocalStorage` key `radar_session` (`StoredSession(UserId, UserName)`).
  - Every app page inherits `SessionAwareBase`, which: restores the session after
    refresh → loads `UserProfile` → redirects to `/onboarding` if incomplete → else
    calls `OnAuthenticatedAsync()`. `Routes.razor` shows a spinner until the restore
    attempt finishes, then bounces to `/login` on failure.
  - Sign-out = delete `radar_session` + clear in-memory session + navigate to `/login`.
- **Auth-gating behaviour that must be reproduced in the SPA:**
  1. No session → `/login`.
  2. Session but profile missing/onboarding incomplete → `/onboarding`.
  3. Complete profile → app, then page data loads.
- **Design system:** single `wwwroot/app.css` (~2,160 lines) using CSS custom
  properties (tokens like `--cyan`, `--navy`, `--bg-hover`, `--font-display`) + Google
  Fonts (Space Grotesk + DM Sans). Per-page markup is largely inline-styled with those
  tokens/classes. Small scoped stylesheets exist for `MainLayout`/`NavMenu`
  (`RadarV2.styles.css`).
- **Dead code to ignore during port:** `Components/Layout/NavMenu.razor` is the
  untouched Blazor template (Home/Counter/Weather) — not used by the real nav.
- **Shared UI:** `Components/UI/{LoadingSkeleton,EmptyState,ErrorState,OfflineState}.razor`
  + `Helpers/DateHelper.Humanize()` used across pages + `ReconnectModal` (Blazor-only,
  drop).
- **Data models:** 18 model files (see §6 TS types). **Not a git repo** — initialize
  version control *before* refactor work (§8, Phase 0).
- **Not wired / mock today** (port as-is, flag for product): Admin dashboard (all
  static arrays), `/notifications` (hard-coded items), `/entry` (design "Reference"
  gallery), Compare loads canned `cmp1`, GrowthTracker + Home read profile stats
  directly, marketing pages are static, Google/Apple auth buttons are decorative.

## 3. Target architecture

```
┌────────────────────────────┐        ┌──────────────────────────────┐
│  React SPA (Vite + TS)     │  JSON  │  .NET 10 Web API             │
│  Static files → CDN/ASWA   │ ─────► │  (new: controllers over the  │
│  React Router (same URLs)  │  ◄──── │   existing 19 interfaces)     │
│  Auth: token in storage    │  SSE   │  ─────────────────────────── │
│                            │  chat  │  Auth (login/register/token) │
└────────────────────────────┘        │  Mongo repositories          │
                                      │  Ingestion pipeline +        │
                                      │  hosted background worker    │
                                      └───────────┬──────────────────┘
                                                  │
                                          MongoDB Atlas
```

- **Backend process:** API + existing services + ingestion worker. One small always-on
  service (scales independently; can scale-to-zero when idle between syncs if desired).
- **Frontend process:** none — static build served by CDN / Azure Static Web Apps /
  any static host.
- **URLs stay identical** to today so deep links and marketing links keep working
  (see §7 route table).

### What is unchanged (do not touch)
- All 19 `*Service` interfaces and Mongo implementations.
- `Data/RadarDatabase.cs`, Mongo `Documents`, `Models/*` C#.
- Ingestion: RSS/Atom, Mediastack, PodcastIndex, Taddy, OpenAlex, Groq Whisper,
  OpenRouter enrichment, `ContentIngestionService` hosted worker.
- `appsettings.json` / env config (Mongo + API keys).

## 4. Key decisions (defaults recommended, confirm before Phase 1)

| Decision | Recommendation | Why |
|---|---|---|
| API transport | ASP.NET Core **Web API controllers** (REST/JSON), one per service area | Familiar, toolable, maps 1:1 to the interface inventory |
| Auth | **JWT bearer** (login/register endpoints issue token) | SPA is on a static host, API on another origin — no cookie/CORS pain. Keep BCrypt + `users` collection. |
| Onboarding gating | `GET /api/me` returns profile + `onboardingComplete`; router guard mirrors today's redirects | Reproduce `SessionAwareBase` semantics in React |
| Chat streaming | Add **SSE** endpoints wrapping the existing `IAsyncEnumerable StreamMessageAsync` | Interfaces already support streaming; today's pages await full responses. Do it now, not later. |
| Routing | React Router with the **exact same paths** as today | No link/SEO breakage |
| Styling | Import existing `app.css` verbatim as global CSS; no CSS framework | Design system is already done — port, don't redesign |
| State/data | TanStack Query (server cache) + plain fetch client + typed DTOs | Fits "server state" dominant app |
| Repo layout | `web/` (React) inside the existing workspace alongside `RadarV2/` | Single workspace, one deploy pipeline |
| Version control | `git init` at workspace root before Phase 1 | Currently no repo; a big migration without VCS is reckless |

## 5. Phased roadmap

### Phase 0 — Plan & prep (no behaviour change)
- [x] This document written.
- [x] `git init` + first commit of current tree (2026-09-11, `52ec3f3`, workspace root).
- [ ] Confirm decisions in §4 (auth mechanism, hosting targets, chat streaming).
- [ ] Seed/dev-data script documented so both stacks can be tested (Mongo is shared, no migration needed).

**Exit criteria:** decisions signed off; VCS baseline committed.

### Phase 1 — API foundation ✅ implemented (2026-09-07)
Built in the existing project (no separate project):
- [x] `Controllers/AuthController.cs` — `POST /api/auth/register`, `POST /api/auth/login`
  → signed JWT (30-day). Same BCrypt + Mongo `users` store as the Blazor app.
- [x] `Controllers/MeController.cs` — `GET /api/me`, `PUT /api/me`,
  `POST /api/me/onboarding-complete`. Email is intentionally excluded from the PUT
  (editing it would desync the auth `users` collection).
- [x] `Controllers/FeedController.cs` — feed list (type/page/pageSize), detail,
  save/unsave, saved list, search.
- [x] `Program.cs` — `AddControllers` + JwtBearer auth + `[Authorize]`, camel-case
  JSON with string enums, JSON 401/403 challenge handlers.
- [x] Session seeding: a middleware after `UseAuthentication` copies the JWT
  `sub`/`name` claims into the scoped `IUserSessionService`, so existing services
  (`IUserProfileService.GetCurrentUserAsync` etc.) resolve the user unchanged.
- [x] JWT dev key in `appsettings.Development.json` (`Auth:Jwt`); code fallback for
  local dev if the section is missing.
- [x] Live smoke test against a throwaway local Mongo: register → login → GET
  `/api/me` → PUT profile → onboarding-complete → personalised feed all green;
  401/400/404/204 paths return JSON.

**Exit criteria: met.** A non-Blazor client can register → login → fetch a
personalised feed for a real user.

**Known limitation:** unknown `/api/*` URLs (typos) return the Blazor `not-found`
HTML page with a correct 404 status — the razor status-page re-execute wins over the
API 404 middleware. Cosmetic only (real endpoints return JSON); fix cleanly when the
Blazor UI is retired.

**Fixed en route (pre-existing bug):** `SourceRegistry` threw on first use — static
field init order meant `All` spread still-null arrays. Moved the build into a static
constructor. The app could not start at all before this fix. Hosted-ingestion worker
now boots.

**⚠️ Security note (unrelated, needs action):** production MongoDB Atlas credentials
are committed in `appsettings.json`. Rotate and move to env vars.

### Phase 2 — Prove the loop end-to-end ✅ implemented (2026-09-11)
- [x] Scaffold `web/` Vite + React 19 + TS + React Router 7 + TanStack Query.
- [x] Auth context: login/signup pages (`/login`, `/onboarding` full 8-step wizard) +
  JWT in `localStorage` + route guard (`RequireAuth`) mirroring §2 gating rules.
- [x] Global CSS import (`app.css` copied verbatim); port `MainLayout` shell + top nav.
- [x] Port **one flagship vertical fully**: Today (`/`) → Feed (`/feed`) → Detail
  (`/feed/{id}`, incl. save + "add to roadmap") → Saved (`/saved`).
- [x] Port `LoadingSkeleton`/`EmptyState`/`ErrorState` + `DateHelper.humanize`.
- [x] Backend additions needed for this vertical: `GET /api/today`,
  `POST /api/roadmaps/active/topics` (both not yet built in Phase 1); CORS for the
  Vite dev origin. See `HANDOFF.md` §3 for the two pre-existing bugs found and fixed
  along the way (save/unsave keying, empty-topic 400 on add-to-roadmap).

**Exit criteria: met**, except deployment behind a preview URL (no hosting decision
made yet — still local-only, see §4/§8). Verified with a full live Playwright-driven
run: register → onboarding (all 8 steps) → Today → Feed (filters, real ingested
content) → FeedDetail (persona tabs, save, add-to-roadmap) → Saved (remove), against
the real API and a throwaway Mongo with the ingestion worker actually running.
Screenshots checked visually against the target design; no console/network errors
beyond expected image-abort noise on fast navigation.

### Phase 3 — Bulk port, section by section
See §7 for the recommended order and §8 risk callouts. Sections, each gated on
previous:

1. **Intelligence remainder:** `/clips`, `/capture`, `/weekly`, `/source/{id}`,
   `/topic/{id}`.
2. **Learn:** `/learn`, `/learn/hub`, `/mentor` (4 modes), `/projects`, `/progress`,
   `/notebook`, `/notebook/new|{id}` — large; Notebook is CRUD + editor.
3. **Opportunity/Research:** `/opportunities`, `/research`.
4. **Utility:** `/library`, `/compare`, `/plans`, `/settings`, `/profile/edit`,
   `/notifications` (flag as mock).
5. **Chat:** wire Ask Radar (`/ask`) + Mentor chat to SSE streaming.
6. **Marketing + entry:** `/landing`, `/product`, `/how-it-works`, `/institutions`,
   `/entry` (all static — convert markup to components, last because they block nothing).
7. **Admin:** `/admin` sections (static today — copy data arrays into the SPA, keep the
   visual identical, wire to real endpoints in a later product decision).

**Exit criteria:** every route serves identical content/behaviour from React; Razor
pages still run side-by-side behind a flag for comparison.

### Phase 4 — Cutover & deployment
- [ ] Retire Blazor UI entry (`MapRazorComponents` → API-only host; delete Razor pages
  once feature parity confirmed).
- [ ] Static hosting for `web/dist` (CDN / Azure Static Web Apps) + API deployment
  (App Service / Container Apps) + env keys.
- [ ] Update `Dockerfile`/`AZURE_DEPLOY.md` for the new topology.
- [ ] E2E pass on every route, auth flows (refresh, expired token, onboarding bounce),
  and the ingestion worker still populating the same Mongo.

## 6. Service inventory → API surface

| Service interface | Endpoints to expose | Used by pages |
|---|---|---|
| `IAuthService` | `POST /auth/register`, `POST /auth/login` | Onboarding, Login |
| `IUserProfileService` | `GET/PUT /me`, `POST /me/onboarding-complete`, stats write | Every app page (via SessionAwareBase) |
| `IIntelligenceFeedService` | `GET /feed`, `GET /feed/{id}`, `POST/DELETE /feed/{id}/save`, `GET /saved`, `GET /feed/search` | Feed, FeedDetail, Saved, LearnHub (reuses GetFeed), WeeklyBrief fallback, Home |
| `INavigatorService` | `GET /today` ✅ implemented (builds Today's Focus from profile) | Home, Mentor prefill |
| `IOpportunityService` | `GET /opportunities`, `GET /opportunities/{id}`, save/unsave, `POST /opportunities/{id}/applied` | Opportunities, Home, WeeklyBrief fallback |
| `IResearchService` | `GET /research/search`, `GET /research/papers/{id}`, related/explain/gaps/cite | ResearchDiscovery |
| `IRoadmapService` | `POST /roadmaps/active/topics` ✅ implemented; `GET /roadmaps`, `GET /roadmaps/active`, `POST /roadmaps`, lessons complete, progress still to do | Learn, FeedDetail (add topic), Home/Progress stats |
| `IWeeklyBriefService` | `GET /weekly-brief`, `POST /weekly-brief/generate` | WeeklyBrief |
| `IProjectStudioService` | `GET /projects/templates`, `GET /projects`, `POST /projects` | ProjectStudio |
| `INotebookService` | `GET/POST /notes`, `GET/PUT/DELETE /notes/{id}` | Notebook, NoteEditor, Mentor review hand-off |
| `IClipsService` | `GET /clips/today`, `POST /clips/{id}/save` | Clips |
| `ICaptureService` | `POST /capture`, `GET /capture/recent` | Capture |
| `ICompareService` | `GET /comparisons`, `GET /comparisons/{id}`, `POST /comparisons` | Compare (currently canned `cmp1`) |
| `ISourceService` | `GET /sources`, `GET /sources/{id}`, follow/weekly/prioritise toggles | SourcePage |
| `ITopicService` | `GET /topics`, `GET /topics/{id}`, follow/alert toggles | TopicHub |
| `IPlansService` | `GET /plans`, `GET /plans/current` | Plans, Settings |
| `ILearningMentorService` | chat, **SSE stream**, quiz generate/submit, study plan, work review | Mentor |
| `IAskRadarService` | chat, **SSE stream**, summarize, study plan, recommendations | AskRadar |
| `ILibraryService` | `GET /library` (category/year/search), `GET /library/categories` | Library |
| `IUserSessionService` | — (replaced by JWT on the client) | — |

### Shared UI / utilities to port to React
- `LoadingSkeleton`, `EmptyState`, `ErrorState` (+ unused `OfflineState`) → components.
- `DateHelper.Humanize` → `lib/date.ts` (verify behaviour: relative labels like "2h ago").
- Feed/layer badge colour maps (28 `ContentLayer` label/colour/bg triples live in
  `Feed.razor` + `FeedDetail.razor` — extract once into `lib/layers.ts`).
- Library category colour maps (in `Library.razor`) → `lib/library-categories.ts`.
- Nav model from `MainLayout` (Today/Feed/Clips/Capture/Opportunities/Saved/Notebook/
  Ask Radar/Learn/Research/Library/Mentor + Intelligence/Learn pills + avatar menu).
- Admin + marketing + onboarding CSS class systems already live in `app.css`.

### C# → TypeScript models (18 files in `Models/`)
`ContentItem`, `Opportunity`, `NavigatorFocus`, `GrowthRoadmap` + modules, `Clip`,
`CapturedItem`, `PolicyComparison`, `WeeklyBrief`, `Note`, `StudioProject`,
`ProjectTemplate`, `MentorQuiz/StudyPlan/WorkReview`, `SourceProfile`, `TopicProfile`,
`LibraryDocument`, `SubscriptionPlan`, `UserProfile`/`UserStats`/`NotificationPrefs`,
`ChatMessage` + all enums (`Enums.cs`: `ContentType`, `ContentLayer`, `PersonaType`,
`OpportunityType`, `CaptureMode`, …). Derive from the C# model files, not from
JSON snapshots — they are the source of truth.

## 7. Route table (keep identical)

| Current route | React page component | Shell | Notes |
|---|---|---|---|
| `/` | Today | App | ✅ ported. Navigator focus + momentum stats |
| `/feed` | Feed | App | ✅ ported |
| `/feed/{id}` | FeedDetail | App | ✅ ported. Persona tabs, add-to-roadmap |
| `/saved` | Saved | App | ✅ ported |
| `/weekly` | WeeklyBrief | App | |
| `/learn` | Learn | App | Roadmap modules |
| `/learn/hub` | LearnHub | App | |
| `/projects` | ProjectStudio | App | |
| `/progress` | GrowthTracker | App | |
| `/mentor` | LearningMentor | App | 4 modes |
| `/notebook`, `/notebook/new`, `/notebook/{id}` | Notebook, NoteEditor | App | |
| `/opportunities` | Opportunities | App | |
| `/research` | ResearchDiscovery | App | |
| `/ask` | AskRadar | App | SSE chat |
| `/capture` | Capture | App | |
| `/clips` | Clips | App | Dark standalone UI — hides top nav? (check) |
| `/compare`, `/compare/{id}` | Compare | App | |
| `/library` | Library | App | |
| `/source/{id}` | SourcePage | App | |
| `/topic/{id}` | TopicHub | App | |
| `/plans` | Plans | App | |
| `/settings` | Settings | App | |
| `/profile/edit` | EditProfile | App | |
| `/notifications` | Notifications | App | Currently mock data |
| `/login` | Login | Onboarding | ✅ ported |
| `/onboarding` | Onboarding | Onboarding | ✅ ported. 6-step wizard |
| `/entry` | EntryScreens | Onboarding | Design reference |
| `/landing`, `/m` | Landing | Marketing | Static |
| `/product` | Product | Marketing | Static |
| `/how-it-works` | HowItWorks | Marketing | Static |
| `/institutions` | Institutions | Marketing | Static tabs |
| `/admin`, `/admin/{page}` | Admin (9 sections) | Admin | Mock data today |
| `/not-found`, error | NotFound / Error | — | |

## 8. Risks & mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| **No version control** | Any phase can silently lose work | `git init` + baseline commit in Phase 0 |
| **No API boundary exists** | API layer is net-new work (largest backend surprise) | §6 inventory; build Phase 1 fully before React work starts |
| **Auth rework** | Session semantics subtle (refresh bounce, onboarding gating) | Reproduce §2 gating exactly; test 4 flows: fresh login, refresh, expired token, incomplete onboarding |
| **Inline-styled markup everywhere** | Page port is mechanical but voluminous (~35 pages) | Phase 2 proves pattern on one vertical; reuse it for the rest |
| **Chat without streaming feels broken** | Ask Radar/Mentor degrade to spinners | SSE endpoints in Phase 1, used from Phase 3 step 5 |
| **Mock pages carried over silently** | Admin/Notifications/Compare look live but aren't | Keep identical visuals, but mark clearly in code + ship a "mock data" inventory section in the API host |
| **Routes split across shells** | Easy to miss public/private routing in SPA | One router config with per-route guards; §7 table is the checklist |
| **Blazor-only features** (`ReconnectModal`, SignalR, `ProtectedLocalStorage`) | Leftover behaviour | Drop consciously; JS equivalents listed in §2 |
| **LLM cost on re-testing** | Chat/quiz endpoints burn tokens | Test with stubbed/OpenRouter-off responses; cap prompts |
| **Single 2,160-line CSS file** | Fights component architecture | Keep as-is (global import); refactor to CSS modules only if it ever becomes a real problem |

## 9. Verification strategy

- **Per endpoint (Phase 1):** curl/Postman happy + error paths.
- **Per page (Phases 2–3):** render identical to current Blazor page (run both apps
  against the same Mongo and compare screenshots on seeded data).
- **Auth matrix** (see §8).
- **Per section (Phase 3):** interaction pass — save toggles, filters, debounced
  search, quiz flow, note CRUD.
- **Cutover (Phase 4):** route-by-route checklist from §7 plus ingestion worker still
  publishing to the same Mongo, and `AZURE_DEPLOY.md` updated.

## 10. Immediate next step

Phases 0–2 are done (see HANDOFF.md §3/§8). Next: start Phase 3, section 1
("Intelligence remainder": `/clips`, `/capture`, `/weekly`, `/source/{id}`,
`/topic/{id}`), extending the API per §6 as each page needs it. Still-open decisions
from §4: final hosting targets and whether to add SSE chat streaming now.
