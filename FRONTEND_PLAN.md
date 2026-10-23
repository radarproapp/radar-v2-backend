# Frontend: the fold, and what it would take to be solid

Written 2026-10-23, against `08a4671`.

Two things: a plan to fold eleven navigation items into four, and an honest assessment of the
frontend with the work it would take to call it production-grade.

Everything here is measured from the repository, not estimated. Where something is a judgement
rather than a measurement, it says so.

---

## Part 1 — The fold

### Why

The app presents **11 top-level destinations, 28 authenticated routes and 42 page components**
before a user has done anything. Two of the eleven — "Radar Intelligence" and "Feed" — are the
same idea, which means the navigation itself cannot say what the product is.

The three personas are students, graduates and professionals. Their job is: become demonstrably
employable, then get the place. Three features serve that; the rest are drawers and verbs that
were promoted to destinations.

### Target

| Nav item | Route | Sub-features |
|---|---|---|
| **Intelligence** | `/` | Today's brief *(default)*, Feed, Weekly brief |
| **Learn** | `/learn` | My path *(default)*, Hub, Build, Mentor, Progress |
| **Opportunities** | `/opportunities` | Daily, All, Pipeline *(already tabbed — the model to copy)* |
| **Workspace** | `/workspace` | Notes, Captures, Saved |

**Not destinations:**

- **Ask** becomes a persistent action, not a place. It is a verb.
- `/settings`, `/profile/edit`, `/plans`, `/notifications` → account menu (already separate).
- `/admin/*`, `/metrics`, `/source-quality` → admin, never in user navigation.
- `/topic/:id`, `/source/:id`, `/compare`, `/feed/:id`, `/showcase/:id` → drill-downs reached from
  content, not from a menu.

**11 → 4.**

### Research and Library: removed

Decided 2026-10-23. The Library holds 208 general-knowledge reference documents spread evenly
across History, Gaming & Esports, Art & Craft, Real Estate, Law, Technology, Politics and Health —
titles like "Global Music Report 2026" and "Focus 2026: World Film Market Trends". That is a
world-knowledge collection. It is not career material for a student chasing a scholarship, a
graduate trying to become employable, or a professional assessing market value.

Research is the closer call: a final-year student writing a dissertation genuinely needs papers.
But that is one slice of one persona, and the student onboarding states the job as "master
difficult courses, improve grades, build practical skills, prepare for internships and
scholarships". Learn and Opportunities serve all four.

**Scope: remove from the product, keep the backend.** Delete the pages, routes and navigation so
users cannot reach it; leave `MongoResearchService`, `MongoLibraryService`, `OpenAlexClient`,
`ResearchPaperCatalog` and `AcademicPaperSynthesizer` in place. The product gets clean, ~4,000
lines of working backend stay available, and bringing papers back is a routing change rather than
a rebuild.

**Consequences for the backlog:**

- **8.7** (broaden the Library to 100+ areas) comes off entirely. Broadening something being
  hidden is work in the wrong direction.
- **8.2–8.5** (honest paper summarisation) is not wasted — it applies to any paper summarisation,
  and `ContentType.ResearchPaper` still flows through the feed.

### One distinction that must not be got wrong

`ResearchPaper` is also a **feed content type**. Papers appear in the feed and must keep working.
`src/lib/layers.ts`, `src/pages/Feed.tsx` and `src/pages/FeedDetail.tsx` reference "Research" for
that reason and are **not** part of this removal. Only `pages/ResearchDiscovery.tsx`,
`pages/Library.tsx`, `lib/library-categories.ts` and their routes, nav entries and API client
functions are.

### Sequence

Each step ships on its own and is independently revertible.

1. **Remove Research and Library** — 2 pages, 2 routes, 2 nav entries, 1 lib file, 8 API client
   functions, the `LibraryDocument` type. Nothing else references them.
2. **Merge Feed into Intelligence** — the two-items-one-idea problem. A tab shell on `/`, with
   `/feed` kept as a working route so existing links survive.
3. **Create `/workspace`** — a container with Notes / Captures / Saved as tabs. The three pages
   move unchanged; only their parent changes.
4. **Fold Mentor, Build and Progress under Learn** — tabs on `/learn`, same pattern.
5. **Demote Ask** to a persistent action in the layout.
6. **Delete the old nav definitions** once the four are live.

**Every existing route keeps working throughout.** Pages are re-parented, not rewritten. If a
grouping turns out wrong it is a one-line move, which is the point of doing it this way rather
than as one large change.

---

## Part 1b — Radar Intelligence: let them build it

Direction set 2026-10-23, adapting Feedly's onboarding.

### The idea, and why it is structural

Today Radar meets a new user with information it guessed they wanted. The guessing is the source
of most of what has been fixed this week: cybersecurity as everyone's default field, a nursing
learner handed data-modelling weeks, a London postcode described to a reader as "a structured
methodology designed to solve complex operational challenges".

Every one of those is the same failure — inferring a subject when none was stated, then writing
confidently about the inference.

Letting the user declare removes the need to guess. Personalisation becomes **stated** rather than
inferred. That is not a UX preference; it deletes a whole class of bug by making the input
explicit.

### The flow

1. **Who are you** — student / graduate / professional. *Already exists* (`/onboarding`).
2. **Narrow it** — searchable course and field cards. *Data already exists*: `lib/interests.ts`
   holds 235 entries across 14 groups, six of them `Academic Programs ·` (STEM, Business, Health &
   Medicine, Social Sciences, Education, Creative & Applied). The UI does not.
3. **Start empty, on purpose** — the user adds what they want to follow. Nothing is pre-filled.
4. **Group it** — the user organises what they added. This is the genuinely new build.

### Where we adapt rather than mirror

**Feedly's unit is a source. Radar's unit must be a subject.**

Feedly works because its users already know which blogs matter to them. A second-year nursing
student does not, and asking them to name sources produces an empty app they never return to.

So: the user declares *what* ("Nursing", "Medical-Surgical Nursing"), and Radar resolves *where* —
the feeds, the videos, the opportunities behind it. That division is the whole value over an RSS
reader, and it is why the 235 subject entries matter more than a source list would.

### What this changes elsewhere

- **The empty state reverses responsibility.** `lib/feed-empty.ts` currently says "Radar has not
  pulled anything in yet" — Radar's failure. Under this model it is "You have not added anything
  yet", with the action to add. Feedly's version is "Personalize your Feedly → Add articles", not
  an apology. Small change, different meaning.
- **`WhatRadarKnows` gets simpler and stronger.** It currently shows inferred signals a user can
  reject. With declared subjects, most of what it shows is what they chose — which is easier to
  trust and easier to correct.
- **The learning record keeps its job.** Declared subjects say what to follow; behaviour still
  says what is working. The record stops having to guess the subject and starts only ranking
  within it, which is what it is good at.

### What does not exist yet

| Piece | State |
|---|---|
| Persona selection | Built |
| Subject catalogue (235 entries, 14 groups) | Built, as data |
| Narrowing screen with searchable cards | **Not built** |
| Follow / unfollow a subject | **Not built** |
| User-created groups — Feedly's folders and boards | **Not built** — the real new work |
| Empty state that invites building | Partly; wording reverses responsibility |

### Risk worth naming

An empty first screen is a real cost. Feedly can afford it because its users arrive knowing what
they want. A student may not, and a blank app is easy to abandon.

The mitigation is the narrowing screen doing actual work: once someone picks "Nursing", offer a
sensible starter set they can accept wholesale or edit — chosen *from* their declaration, not
guessed before it. Declared, then helped. Not helped, then guessed at.

---

## Part 2 — Frontend assessment

Measured on 2026-10-23.

### What is genuinely good

- **Routes are lazy-loaded.** Page chunks are 16–36 kB; nobody downloads the Mentor to read the
  feed.
- **Server state is TanStack Query**, not hand-rolled `useEffect` fetching.
- **The field-projection system** (`?fields=`) with a test that fails when a rendered field is not
  requested. That is a real class of bug caught cheaply.
- **Honest empty and error states**, with a rule enforced by test: never offer a button that
  cannot fix the stated problem.
- **A named icon system** with a guard against emoji returning.

### The five real problems

#### 1. No component tests at all

```
27 tests, all 5 files in src/lib/__tests__
0 tests that render a component
42 page components, 17 shared components
```

Every test is a pure function test. Nothing verifies that a page renders, that a button is wired,
that a loading state resolves, or that an error path shows an error. The emoji sweep broke three
render sites — onboarding printed the words "course", "job" and "launch" on screen — and **no test
caught any of it**. A screenshot did.

*Fix:* Vitest + Testing Library, starting with the four pages that matter — Intelligence, Learn,
Opportunities, Workspace — asserting: renders, shows loading, shows error, shows empty, shows
content. Roughly 20 tests covering the paths users actually take.

#### 2. Two styling systems, competing

```
1,290  inline style={{ }} occurrences
4,541  lines of app.css
```

Both are in use, often in the same component. The consequence is not ugliness but **inability to
change anything globally**: there is no way to adjust spacing, dark mode or density without
touching 1,290 sites. It also makes every component locally readable and globally unknowable.

*Fix:* pick one. The stylesheet already has the vocabulary (`--text-muted`, `--border`, `btn
btn--sm`). Migrate inline styles to classes **opportunistically** — when touching a component, not
as a project. A codemod would produce 1,290 mechanical diffs nobody can review.

#### 3. Accessibility is largely absent

```
303  <button> elements
 16  aria-label
 11  role=
```

Roughly one accessible label per nineteen interactive elements. Many buttons contain only an icon,
which a screen reader announces as nothing at all. There is no visible focus management, no skip
link, and no keyboard path audited.

This matters commercially as well as ethically: universities and institutions — named in your own
marketing as a target — frequently require an accessibility statement before procurement.

*Fix:* an audit pass over the four core pages. Icon-only buttons get `aria-label`, forms get
associated labels, focus order gets checked, one skip link. Then `eslint-plugin-jsx-a11y` in CI so
it cannot regress.

#### 4. One error boundary for 42 pages

```
ErrorBoundary referenced in 2 files
```

Any render error in any page blanks the whole application. With a route-level boundary, a broken
Mentor would leave the feed working.

*Fix:* a boundary per route in the layout, with a reset that re-renders rather than reloads.

#### 5. The main bundle is large

```
index.js  332 kB  (104 kB gzip)
```

Lazy loading is working — page chunks are small — so this is the shared core. On a mid-range
Android phone on 3G, 104 kB gzip of JavaScript before anything renders is several seconds. Your
users are in Lagos, not on fibre.

*Fix:* measure before cutting. `rollup-plugin-visualizer` will name the occupants; the usual
answers are date libraries, icon barrels and router internals. The icon map imports ~70 lucide
icons eagerly — tree-shaking should handle it, but it should be verified rather than assumed.

### Two more worth naming

- **`LearningMentor.tsx` is 1,367 lines.** Also `Learn.tsx` at 902 and `Opportunities.tsx` at 863.
  These hold quiz state, chat streaming, study plans and review in one file. Split when the fold
  touches them, not before — splitting for its own sake creates churn without changing behaviour.
- **18 pre-existing lint warnings**, mostly `set-state-in-effect`. Each is a cascading-render risk.
  They have been stable all session; the value is in fixing them before adding to them.

### Suggested order

Ordered by *risk removed per hour*, not by visibility.

| | Work | Why first |
|---|---|---|
| 1 | Route-level error boundaries | Smallest change, stops one broken page taking the app down |
| 2 | Component tests for the four core pages | Would have caught the emoji regressions; required before the fold moves them |
| 3 | The fold itself, steps 1–6 | Needs 2 in place to be safe |
| 4 | Accessibility pass + CI rule | Unblocks institutional sales; cheap once scoped to four pages |
| 5 | Bundle measurement | Measure first; may need nothing |
| 6 | Inline-style migration | Continuous, never a project |

### What I cannot judge

- **Visual design.** I have read the code and two screenshots, not used the product.
- **Whether the information architecture is right for your users.** The fold is reasoned from your
  stated personas, not from usage data. If you have analytics, they beat this document.
- **Performance on real devices.** The bundle figure is a build artefact, not a measurement from a
  phone in Lagos on a real network.
