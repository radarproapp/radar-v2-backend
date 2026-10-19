# Radar — Remediation Plan

_Created 2026-10-19. Sources: the original requirements doc (items 1–20) and the user's
review of what we actually shipped (Feedback.docx, 11 comments + 23 screenshots)._

**North star, stated by the user above everything else:** _"we need to simplify all
processes in ui and ux, so users can easily understand and navigate the product."_

Every task below is judged against one question: does this make Radar easier to
understand and more obviously useful, or does it add surface area?

---

## Ground truth established 2026-10-19

Before planning, every complaint was traced into code. The headline: **this is mostly
not a styling problem.** Layout, type and spacing are fine. The problem is what the
screens put inside them.

| Finding | Evidence |
|---|---|
| The professional home screen is a static mock | `ProfessionalCareerDashboard.tsx:19-113` — `CAREER_DECISIONS`, `SENIOR_OPPORTUNITIES`, impact records are const literals; "$225k+ / VP/Director / Kuda" hardcoded at `:282`, `:293`, `:310`. Routed as home from `Today.tsx:40`. |
| No cybersecurity exists in the learning engine | `MongoRoadmapService.cs:437-446` switches on product/data/entrepreneur/finance/software/policy, `_ => DataAnalyticsModules()`. No cyber branch → Excel fundamentals. `Learn.tsx:19-44` hardcodes the same data-analytics stage goals in the frontend. |
| "Your Edge" renders a raw description dump | `FeedDetail.tsx:298` falls back to `item.aiSummary`. |
| One generic paragraph for every user and item | `OpenRouterAiEngine.cs:360-371` deterministic fallback is a fixed string. |
| The slowness is blocking LLM calls on list endpoints | `MongoIntelligenceFeedService.cs:131` awaits `_aiRelevance.ApplyAsync`; `MongoOpportunityService.cs:39` does `Find(_ => true)` then awaits `_aiMatch.ApplyAsync` at `:52`. |
| Research promises a 500-word breakdown it never generates | `SearchPapersAsync` never calls `EnsureAiPaperBreakdownAsync` (only `GetPaperByIdAsync` does) → `paperBreakdown` null → `ResearchDiscovery.tsx:436` renders the raw abstract, while `:373` (`wordCount \|\| 520`) still prints "~520 words". |
| Research fabricates papers | `MongoResearchService.cs:183-207` invents authors, a hash-generated DOI and "31.4% improvement", tagged `DatabaseSource = "OpenAlex / CrossRef"` under a "Verifiable" badge. **Directly violates spec #18 §6 "Never Invent Academic Information".** |
| Capture is a no-op | `MongoCaptureService.cs:18-33` stores the raw string, sets `Source = "captured just now"` as a literal, no enrichment, no roadmap route — while the page header promises the opposite. |
| Persona change does nothing | Two parallel profile systems: `/onboarding/{student,graduate,professional}` vs the old generic `EditProfile.tsx` (finance placeholders at `:173`). Switching persona never re-runs onboarding; `onboardingComplete` stays true. |
| Library has no summaries | No summary field exists — it is a link directory. |

**Unverified, needs a live repro:** Mentor's Review Work / Mastery & Memory / Spaced
Revision. All endpoints exist and match the client calls, so "not working" is most
likely cold-start empty states with no explanation of what fills them.

**Deployment drift:** the Clips screenshot shows a source-string format that no longer
exists at HEAD, so the Railway backend was running older code than the frontend during
the test. Confirm what is actually deployed before fixing against ghosts.

---

## One conflict between the two documents

Spec item #12 defines **YOUR EDGE** as a core part of the intelligence output. The later
review says: _"Like I said, remove the 'your edge'. It's useless."_

**Resolution:** the later review wins — the separate "Your Edge" block comes out. But the
*substance* the spec wanted (the specific advantage, what you can build, who to watch,
which market to explore) does not get dropped; it folds into **Why It Matters** and
**Next Move**, which is where the user said to concentrate effort. Flagged here so this
is a visible decision, not a silent one.

---

## Phase 0 — Baseline before touching anything

- [x] 0.1 ~~Confirm what is actually deployed~~ — **partially.** Railway backend is up and returns clean JSON 401s, but there is no unauthenticated version endpoint, so the deployed commit can't be read remotely. Local frontend was 28 commits behind `origin/main`; fast-forwarded to `e289fbd`. **Add a `/api/version` endpoint** so this is answerable next time.
- [x] 0.2 ~~Get the stack running locally~~ — done against a throwaway `mongo:7` container. Clean boot, no errors, indexes created. Registered a test professional (SOC Analyst / Lagos / Cybersecurity), completed onboarding, and exercised feed, opportunities, today, research and all six mentor endpoints. Container and server torn down afterwards.
- [x] 0.3 ~~Live repro of Mentor~~ — **not broken; empty.** All six endpoints return 200 in 30–110ms with real data for a brand-new user: `mastery` 2 topics, `spaced-revision` 5 items, `memory` and `today` populated, `reviews` an empty list (correct — nothing submitted yet), `study-plan/active` a 204 that the client handles properly. So "isn't working at all" is a presentation problem: the sub-tabs render nothing useful on a cold start and never say what fills them. Moves to 9.1 as a UX task, not a bug hunt.

## Phase 1 — Remove before adding (simplify + stop shipping untrue things)

The fastest way to simplify is to delete. Also the fastest way to restore trust.

- [x] 1.1 ~~**Remove Clips entirely**~~ — done: page, route, nav entry, `MongoClipsService`, `IClipsService`, `ClipsController`, `Models/Clip.cs`, the Mongo collection + index, the AI engine's clips branch, 52 CSS rule blocks, and the `Clip` type. The `ClipSaved` analytics enum value is deliberately kept so historical events still deserialize and still render in Metrics. (spec #16 — _"Remove clips. It's not needed."_)
- [x] 1.2 ~~Remove the **"Your Edge"** block~~ — done across `FeedDetail`, `Capture`, `Compare`, `SourcePage`, `TopicHub`, `Learn`, `LearnHub`, plus orphaned CSS. The backend still generates `yourEdge`; that contract gets rewritten in 4.3 rather than left half-removed. See the conflict note above.
- [x] 1.3 ~~Delete `CreateSyntheticTopicPaper`~~ — done. Search now returns an empty result honestly instead of inventing a paper, DOI and statistic (spec #18 §6).
- [x] 1.4 ~~Kill the fake `wordCount || 520` badge~~ — done. The 5-minute-summary badge only renders when a breakdown exists; the fallback box is now labelled "Abstract (from the source)"; page copy no longer claims every paper already has a breakdown.
- [x] 1.5 ~~Strip the hardcoded demo data~~ — done as part of 3.1. Both career dashboards turned out to make **zero API calls**: 1,372 lines of pure fabrication. Deleted outright, along with `CareerHub` and the `/career*` routes and nav entry.
- [x] 1.6 ~~Simplify the Opportunity Hub's filter wall~~ — done. Four rows of chips (~28 controls) are now collapsed behind one **Filters** button that shows the active count, names the active filters inline when collapsed, and offers **Clear all**. Default state is "show me the opportunities", not "configure a query first".
- [x] 1.7 ~~Hide empty intelligence blocks~~ — done via a new `IntelBlock` component plus guards in `FeedDetail` and `Capture`. Also removed the invented next-move catch-all ("Apply this insight to a project…"), which disguised a missing recommendation as a real one.

## Phase 2 — Make it fast (review items 1 and 10, repeated three times)

- [x] 2.1 ~~Move `_aiRelevance.ApplyAsync` off the feed request path~~ — done, and the bigger find: **the OpenRouter HttpClient had no timeout at all**, so it inherited the 100s default; with the engine's two-attempt retry a hung provider could pin a page render for 200s+. Now: 20s client timeout, a 25s overall deadline across attempts, and `IAiEngine.TryGetCachedJson` / `WarmJson` so interactive paths use AI output when warm and warm it in the background when not. In-flight de-duplication means a burst of requests triggers one provider call.
- [x] 2.2 ~~Opportunities~~ — done. Both full-collection loads replaced with a `GetScoringCandidatesAsync` that pushes the expiry filter into Mongo and caps the candidate set at 500, backed by a new index on `Deadline`. `_aiMatch.ApplyAsync` is cache-first + background warm. Ranking stays in memory because it is per-user, so the page itself can't be pushed into Mongo — but the unbounded scan is gone.
- [~] 2.3 **Dropped as unnecessary.** Today's three queries already run in parallel via TanStack Query, so the cost is max(3), not sum(3) — and `TodayController` / `MongoNavigatorService` contain no AI calls at all. Today was slow purely because of the feed and opportunity endpoints it fans out to, which 2.1/2.2 fixed. Adding a bespoke aggregate endpoint would add backend surface for no measurable win. Revisit only if measurement in 2.4 says otherwise.
- [x] 2.4 ~~Set a budget and measure it~~ — **met, with room.** Cold: feed 386ms, opportunities 179ms, today 92ms, daily batch 41ms. Warm over 8 runs: feed 23–57ms, opportunities 15–37ms, today 8–16ms. All far inside the 1.5s p95 target. Measured with no OpenRouter key configured, which is the point: the deterministic path is now the fast path and AI never blocks a render.
- [x] 2.5 ~~Learn Hub synthesis off the page-load path~~ — done, same cache-first pattern; it already had a catalog fallback to render from.

## Phase 3 — A home screen that answers "what matters to me today"

- [x] 3.1 ~~Rebuild professional/graduate home~~ — done. New `GET /api/home` (`HomeBrief` model + `HomeBriefService`) composes profile, roadmap, behaviour and real ingested content; new `HomeBriefView` renders it for **every** persona. Removed the persona branch in `Today.tsx`, which also fixes a latent React hooks-ordering violation (the branch sat above the hooks, so the hook count changed as the profile resolved).
- [x] 3.2 ~~**Top 3 things that matter today**~~ — done. Ranks a 24-item window by the per-user scorer and shows the top 3, each with a specific reason: "Matches your goal: … and your interest in AI", built from the dimensions the scorer actually fired on rather than an asserted connection.
- [x] 3.3 ~~**Radar Noticed**~~ — done, from `IBehavioralSignalService` inferred signals only (declared interests are what they told us at onboarding, not what Radar noticed), ordered by decayed strength.
- [x] 3.4 ~~**Today's Focus** names the specific skill/topic~~ — done: the next incomplete lesson in the current stage. Verified live — a cybersecurity user now gets "TCP/IP, DNS and HTTP: how traffic actually moves" where they previously got "Excel fundamentals & spreadsheet layout".
- [x] 3.5 ~~**Portfolio next move** names the specific artifact~~ — done via a new public `InterestPersonalization.PortfolioArtifactFor`, plus a cybersecurity branch the helper was missing. A SOC analyst is now told to build "a documented detection rule plus an incident write-up showing how you'd triage and contain it".
- [ ] 3.6 Feed must never be empty — fallback ladder (spec #3). Partly there already: `BuildFilterAsync` drops the interest-layer filter when it would return nothing. Still needs a final rung for a genuinely empty corpus.

## Phase 4 — Feed card + full brief (spec #11, #12)

- [ ] 4.1 Feed card = big image, title, "read brief", one line on why you're seeing it. Nothing else
- [ ] 4.2 Full brief = what happened → why it matters → next move → add to roadmap
- [x] 4.3 ~~Rewrite intelligence generation to the spec #12 standard~~ — prompt rebuilt. Names the
  reader's own goal/role/city/blocker, demands a concrete example, bans sentences that would be true
  for any reader, and drops `yourEdge` (folded into why + next move per the user's instruction).
  The prompt now also receives the `UserLearningRecord`, so it knows the learner's stage, proven
  topics, weak areas and what they keep returning to — spec #12 asks the explanation to connect to
  the current roadmap, which was impossible before.
- [x] 4.4 ~~Relevance gate~~ — the writer is asked to decide honestly *first* and return
  `canConnect: false` with a low `relevance` rather than manufacture a link. When it declines,
  generation returns null and the grounded template is used instead. The prompt names the actual
  failure ("logistics and agriculture can align with your cybersecurity skills") as the thing to
  avoid. `relevance` is surfaced on the API so the feed can later drop weak items outright.
- [ ] 4.6 **Verify against the live model.** Everything above is verified structurally, but the
  prompt's actual output quality cannot be judged without an OpenRouter key — there is none in dev.
- [ ] 4.5 Replace the one-size deterministic fallback with per-item grounded output, or suppress the section

## Phase 5 — Domain coverage + Learning Path (spec #13)

- [x] 5.1 ~~Cybersecurity roadmap template~~ — done. Added `CybersecurityModules()` (4 stages, 16 lessons: networking → SIEM/IR → detection engineering & ATT&CK → home lab, write-ups, interview prep). The silent `_ => DataAnalyticsModules()` default is replaced with `GenericModules(goal)`, which names the user's own stated goal at every stage instead of confidently serving the wrong curriculum. Verified live end to end.
- [x] 5.2 ~~Delete the hardcoded `STAGE_DEFINITIONS`~~ — done. Stage labels and goals now derive from the roadmap's own modules, with domain-neutral fallbacks for the gap before one exists. The four stage *keys* stay (they are structural); the Excel/SQL/Power BI/Python *content* is gone.
- [ ] 5.3 80% how-to / 20% current-developments resource mix per stage
- [ ] 5.4 Ordered "start with this" sequencing with time estimates and a reason per resource
- [ ] 5.5 Tutorial summaries in step-by-step format; article/video summaries in what-happened format
- [ ] 5.6 Every resource answers "why am I learning this" against the user's goal (spec #13 §7)
- [ ] 5.7 Project Studio: domain-aware, specific projects; fix duplicate rows

## Phase 5b — Verified channel catalogue (added 2026-10-19 at the user's request)

- [x] 5b.1 ~~Collate YouTube channels for the courses~~ — `LEARNING_CHANNELS.md`. Every handle was
  verified by loading the channel page and reading back its real title; handles that did not resolve
  were dropped, not guessed. **`@ProductSchool` turned out not to be Product School** (it now belongs
  to a person named Werner Müller) — the real one is `@ProductSchoolSanFrancisco`. Guessing would
  have shipped that.
- [x] 5b.2 ~~Wire cybersecurity resources into the catalogue~~ — `CybersecurityResourceCatalog.cs`,
  12 resources across the 4 stages, routed ahead of the other keyword branches. Verified live: a
  cybersecurity roadmap now carries 12 resources where it previously showed "0 ordered resources".
- [x] 5b.3 ~~Link to channel topic searches, not hardcoded video IDs~~ — single videos rot and a
  wrong ID is invisible in review; `@handle/search?query=...` always lands on that channel's current
  videos. Same principle as dropping the fabricated papers.
- [ ] 5b.4 Replace the remaining hardcoded video IDs in `LearningResourceCatalog.cs` (the other six
  domains) the same way — they carry the same staleness and fabrication risk.

## Phase 5c — Personalisation record (user direction, 2026-10-19)

> _"Personalization is the core like you said even in the road maps."_
> _"Cyber security was only an example of personalization o."_
> _"Radar Learn is for building skills and demonstration of capabilities by learning and building
> projects at the same time."_
> _"Simple English applies all over the app."_

- [x] 5c.1 ~~`UserLearningRecord`~~ — one consolidated record of what Radar knows: declared answers,
  inferred signals, per-topic evidence, strong/weak areas, quiz history, recommended start stage,
  priority topics, a 0–1 confidence score and a plain-English summary. **Derived on read** from
  profile + signals + roadmap, so it can never drift from its sources and needs no migration.
- [x] 5c.2 ~~Roadmaps actually use it~~ — `RoadmapPersonalizer` runs on create **and on every read**,
  because evidence keeps arriving. Below 0.35 confidence it stays close to what the user declared
  rather than inventing a path from noise.
- [x] 5c.3 ~~Every stage carries a project~~ — new `StageProject` on `RoadmapModule`. Learn is
  building skills *and* showing capability at the same time, so the project runs alongside the
  lessons instead of being deferred to a final "portfolio" stage.
- [x] 5c.4 ~~Domains are data, not code branches~~ — `StageProjectCatalog`. Cybersecurity is one row,
  not a special case. The earlier shape (a keyword branch in the roadmap builder, another in the
  resource catalogue, another in the personaliser) is exactly how cybersecurity fell through to data
  analytics: each new domain needed someone to remember three separate `if` statements.
- [x] 5c.5 ~~Remove the other silent wrong default~~ — `LearningResourceCatalog` fell back to
  software engineering for any unmatched goal. A nurse does not need a Python course. Returns empty
  now so callers fall back to the learner's own roadmap.
- [x] 5c.6 ~~`SubjectPhrase`~~ — goals arrive as sentences ("Become a hospital nurse manager") and
  were dropped straight into titles, producing "Try Become a hospital nurse manager Nursing on
  something small". Routing text (deliberately widened for keyword matching) is now separate from
  display text.
- [x] 5c.7 ~~Simple English everywhere~~ — `PlainEnglish.Rule` applied at the single AI boundary
  (`GenerateJsonAsync`, `GenerateTextAsync`, `StreamChatAsync`), so it covers every feature and
  cannot drift. The generic deterministic "why" paragraph is replaced with an honest placeholder.
- [x] 5c.8 ~~Expose the record~~ — `GET /api/me/learning-record`. Personalisation the user cannot
  see or correct is indistinguishable from personalisation that is not happening.
- [ ] 5c.9 Surface the record in the UI so users can correct what Radar thinks it knows.
- [ ] 5c.10 Feed the record into the feed ranking and opportunity matching too, not just roadmaps.

## Phase 5d — Production AI reality check (2026-10-19)

> _"Openrouter API key is well connected on Production."_

This corrects an earlier assumption in this document. The deterministic brain is **not** the default
path in production, so the weak "why it matters" in the review was **real LLM output**, not template
text. That moves the fix squarely to the prompt and the relevance gate (4.3 / 4.4) — the fallback
work was worth doing, but it was not the cause.

- [x] 5d.1 ~~Budget gate was silent~~ — `WithinBudgetAsync` returned false with no log. Once the
  `MonthlySpendCapUsd` (default **$25/month**) is reached, every feature silently drops to template
  text, mid-month, with nothing in the logs. Now logs a warning naming the feature, spend and cap.
- [x] 5d.2 ~~Dashboard showed green while degraded~~ — `IsCloudLlmActive` only checked that a key
  existed, so Metrics read "● Cloud LLM Active" even with the cap hit. Now reflects actual state,
  plus `IsBudgetExhausted` and a plain-English `DegradedReason` shown on the page.
- [x] 5d.3 ~~Check the live spend~~ — **ruled out.** OpenRouter shows **$1.06 total** (DeepSeek V3,
  Oct 12–18) against a $25 cap. The budget gate never fired. 5d.1/5d.2 remain worth having as a
  latent trap, but the weak output was real model output, not degradation.
- [x] 5d.4 ~~Second dead spend cap~~ — `PersonalizedWhyService` carried its own
  `WhyMonthlySpendCapUsd` ($10) plus usage tracking that **nothing ever called**, left behind when
  the service moved to `IAiEngine`. Removed, along with the two dependencies it dragged in.

## Phase 5e — Tutorials follow the learner's path (user direction, 2026-10-19)

> _"Youtube tutorials should be according to what user's learning path.. This still boils down to
> personalization for all users."_ Plus the direction to use the YouTube Data API and cache results
> rather than hand-collecting and summarising videos.

- [x] 5e.1 ~~Resources derive from the roadmap~~ — `LearningPathResourceBuilder` turns each lesson
  into a tutorial and each stage into one "what changed recently" entry, roughly the 80/20 split.
  A nursing path went from **0 resources to 16**.
- [x] 5e.2 ~~YouTube Data API with caching~~ — `YouTubeSearchService` resolves real videos and
  caches every result (including empty ones) in Mongo, shared across all users. `search.list` costs
  100 units against 10,000/day, so about 100 searches a day for the whole product; lesson titles
  repeat across everyone on a path, so one cached result serves all of them.
- [x] 5e.3 ~~Never block a render on it~~ — roadmap reads hit the cache only. Misses are warmed in
  the background, capped at 4 per view so one page cannot spend a meaningful share of the day's
  quota.
- [x] 5e.4 ~~Links, not scraping~~ — official API, watch links and the supported `/embed/` player.
  No transcript extraction, which is where YouTube's terms tighten.
- [x] 5e.5 ~~`RoadmapLesson.SearchHint`~~ — readable titles make poor queries. "What hospital nurse
  manager actually involves day to day" finds nothing; "what does a hospital nurse manager do"
  finds the lesson.
- [x] 5e.6 ~~Do not scope to the wrong channel~~ — an earlier pass fell back to general teaching
  channels, so a nursing query was scoped to Khan Academy and would return an empty page. No domain
  match now means an unscoped search, which covers every subject.
- [ ] 5e.7 **Set `YouTube:ApiKey`.** Without it, resources stay as search links — useful, but not
  real resolved videos. The free quota is enough given the caching.
- [ ] 5e.8 Transcript summarisation (Supadata / TranscriptAPI) so each video carries a Radar
  summary. Deferred until 5e.7 is in and we can see real titles first.

## Phase 5f — Intelligence processing before recommending (user direction, 2026-10-19)

> _"Radar needs an intelligence processing system... where it processes information before
> recommending to users."_ Shown with a roadmap for the goal "Get Promotion".

The screenshot showed "Beginner: Beginner: Foundations of Get Promotion", "Applied Get Promotion",
"What Get Promotion actually involves day to day" — and a Python machine learning course underneath.
Four separate faults, one root cause: nothing interpreted the goal before generating against it.

- [x] 5f.1 ~~`LearningIntent`~~ — the missing distinction is that a goal and a subject are not the
  same thing. "Get promoted" is an **outcome**: the thing to learn is how to be promotable in your
  field, not the word "promotion". "Cybersecurity" is a **subject**. Reads outcome from the goal and
  domain from role/industry/interests, so an outcome never becomes the subject.
- [x] 5f.2 ~~`CareerAdvancementModules`~~ — outcome goals get a real path (know where you stand →
  do visible work → build influence → make the case), with the final stage varying by outcome:
  Interviews & Offers, Leading Your First Team, Negotiating Your Value.
- [x] 5f.3 ~~Subject mismatch~~ — modules were built from the goal while resources were keyed on the
  interest, so the page showed one subject's lessons beside another subject's videos. Outcome paths
  no longer pull a field's curated curriculum at all.
- [x] 5f.4 ~~Doubled stage prefix~~ — module titles already carry their stage, and the page printed
  it again.
- [ ] 5f.5 Extend the same interpretation to the feed and opportunity matching, which still read the
  raw goal string.

## Phase 6 — Learn Hub + Knowledge Graph (spec #14)

- [ ] 6.1 Learn Hub scoped to the active learning path: primary skill + 1–3 sub-interests
- [ ] 6.2 Multi-format mix (tutorials, podcasts, blogs, papers, docs, projects)
- [ ] 6.3 "Why this resource?" on every item
- [ ] 6.4 Knowledge graph built from actual behaviour, not stated interests
- [ ] 6.5 A single clear "what should I learn next" recommendation

## Phase 7 — Opportunity Intelligence (spec #17, review item 5)

- [ ] 7.1 Fresh opportunities every day; older ones move to a collection, not the daily view
- [ ] 7.2 Match % + a specific reason it matches
- [ ] 7.3 Deadline intelligence and reminders
- [ ] 7.4 Detail page: what it is, why it matches, requirements, benefits, how to apply, Radar's advice
- [ ] 7.5 "Help me apply" wired to the Mentor
- [ ] 7.6 Tracking states feed back into matching

## Phase 8 — Research + Intelligence Library (spec #18, #19)

> **Re-scoped 2026-10-19 after live testing.** 8.1 was based on reading `SearchPapersAsync`, which
> doesn't call `EnsureAiPaperBreakdownAsync`. But `OpenAlexClient.cs:245` already runs
> `EnrichPaperWithStrictBreakdown` on every ingested result, so at HEAD **25/25 search results come
> back with a real breakdown** (532, 555, 715, 688 words — verified live). The user's "where's the
> 500-word summary?" was the **deployed backend lagging HEAD**, not a missing feature. Deploy HEAD
> and that complaint should go away on its own. 1.4 still earns its place: it stops the badge
> claiming a summary when one genuinely is absent.

- [x] 8.1 ~~Generate the structured breakdown for search results~~ — already true at HEAD; verified live, see note above
- [ ] 8.2 Full 10-part summary format per spec #18 §5
- [ ] 8.3 "Not clearly stated in the available source" instead of inventing (spec #18 §6)
- [ ] 8.4 Label foundational/older research distinctly from 2020–2026
- [ ] 8.5 "Why this paper?" personalised to the user's research context
- [ ] 8.6 Library: 500-word summaries, last five years, original always accessible
- [ ] 8.7 Broaden the Library to 100+ areas across academic and professional interests

## Phase 9 — Mentor, Capture, Summarize (spec #20, #15, #8)

- [ ] 9.1 Fix whatever Phase 0.3 finds in Review Work / Mastery / Spaced Revision; give every one a real cold-start state
- [ ] 9.2 Ask (Tutor) suggestions seeded from the user's actual interest ("how to become a cybersecurity expert")
- [ ] 9.3 Quiz: advanced questions, and show right/wrong with an explanation of why
- [ ] 9.4 Study plan: short relevance note per topic, suggested resources, "make this my learning path"
- [ ] 9.5 Capture: make photo and voice actually work, or remove them; clarify what Note is for
- [ ] 9.6 Capture must deliver what its own header promises, or the header changes
- [ ] 9.7 Summarize file: ask which chapter/section, or all; keep output concise

## Phase 10 — Profile and persona

- [ ] 10.1 Changing persona re-runs the matching onboarding flow
- [ ] 10.2 Retire the generic `EditProfile` form in favour of the persona flows

## Explicitly not changing

- **Ask Radar** — _"You cooked for real! It's concise and very easy to understand."_
- **Summarize link** — _"also working fine. I like it"_

---

## Working order

Phases run in order. Within a phase, tasks can move. Rationale: Phase 1 deletes surface
area and stops the product asserting things that aren't true — it is the cheapest work
and the largest trust win. Phase 2 makes the app usable at all. Only then is it worth
rebuilding content quality on top (Phases 3–8), because every content fix is invisible
if the page takes 15 seconds to load or shows a static mock.
