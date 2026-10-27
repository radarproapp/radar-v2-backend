# Radar: the UX interpretation

A map from the product owner's implementation guide to screens, written before building so the
disagreements happen here rather than in a diff.

The guide lists around seventeen numbered features. The failure mode is reading them as seventeen
screens — which is how Radar got to eleven navigation entries and the verdict *"this app is too
congested, so many informations at once"*. **Features are not navigation.** They are things that
happen inside a verb.

---

## 1. Two products, six verbs

The guide's own framing, and the whole structure follows from it:

| Radar Intelligence | Radar Learn |
| --- | --- |
| **discover** what matters | **learn** what you need |
| **understand** why it matters to you | **practise** until you understand |
| **act** on it | **demonstrate** what you can do |

Everything else is a destination inside one of these, or it does not exist.

---

## 2. Where every feature lands

### Radar Intelligence

| Destination | Verb | Guide section |
| --- | --- | --- |
| **Today** | discover | §4 personalised feed, condensed |
| **Feed** | discover, understand | §4–§7 feed, cards, controls, ranking |
| **Opportunities** | act | §21 verification, deadlines, pipeline |
| **Saved** | act | §6 save for later |
| **Captures** | discover | bring your own link or note |
| **Notes** | understand | write while reading |

### Radar Learn

| Destination | Verb | Guide section |
| --- | --- | --- |
| **My path** | learn | §8–§9 goal box, path generator, §22 quality |
| **Hub** | learn | §11 learning hub, §12 research discovery |
| **Mentor** | practise | §13 explain, quiz, gaps, guided prep, next action |
| **Projects** | demonstrate | §17 project studio |
| **Progress** | demonstrate | §15 study plan, §24 learning analytics |

### Not a destination

- **Ask Radar** — a verb available from anywhere, not a place. Already in the sidebar footer.
- **Document assessment** (§16) — lives where the document arrives: upload in Ask, assess in
  Mentor. A separate screen would duplicate both.
- **Quizzes** (§13B) — the guide is explicit: inside Mentor, "not a separate top-level product
  feature".
- **Analytics** (§23–§24) — measured, never a user-facing screen.

---

## 3. What changes from today

Three things. Everything else stays.

1. **Opportunities stops being a third top-level product** and becomes a destination inside
   Intelligence, reached the same way Feed types already are. The guide says two products; a
   third switcher entry contradicts it. Nothing is lost — the pipeline and deadline tracking
   come with it.
2. **Research Discovery returns, inside the Hub.** The backend was never removed: search, fetch,
   citation, explain and gaps all still serve. Only the page was deleted.
3. **Personalisation gets asked.** See below.

### What explicitly does not change

The sidebar drill-down, the empty states, the three Learn questions, the honest matching, the
mobile hamburger, profile switching. These were built, measured and corrected; the guide
ratifies all of them.

---

## 4. The personalisation moment

The guide asks three boxes — interests, goals, opportunities — immediately after login, before
the user has seen anything.

**We ask later, at the point of use.** The questions are the same; the moment is not.

Asking before anyone has seen the product is the moment we deleted a six-step flow to escape:
people answer carelessly or leave, because nothing has yet shown them what the answer is for. By
the time someone is looking at a feed and thinking "this isn't quite me", the question answers
itself.

Radar already does this in two places. Learn says *your path is empty, build it*. Opportunities
says *start a path or save something and I will match*. **Intelligence is the one that doesn't
ask** — it just serves a generic feed. This makes it consistent rather than inventing a pattern.

### Three states, honestly labelled

> Written before the product owner answered. Where this section and §7 disagree, §7 is the
> decision: the unpersonalised feed is not a full unranked one, and the prompt is the screen
> rather than a card in a stream. The reasoning for asking later rather than at signup stands.

| State | What the feed is | What it says |
| --- | --- | --- |
| Nothing asked | Two general tier-1 items, unranked | **Latest**, under the three questions |
| Personalised | Ranked on interests, goals, opportunities | **For you** |
| Personalised + used | Ranked, plus what they actually read | **For you** |

The unpersonalised state must be *labelled as unpersonalised*. Deferring the question only works
if the feed does not pretend to be a personalised one that happens to be bad at its job. A new
user seeing sport and entertainment under a heading that promises relevance learns that Radar
does not know them — and that lesson is hard to undo.

### The invitation

Not a modal. The screen itself, on Today and above the sample on the feed, with the reason
visible:

> **For Radar to serve you better**
> Three short questions and your feed becomes yours.
> *Personalise my Radar*

It persists until answered, because it is a requirement for the product to do its job. It does
not block the door either: every other destination — Learn, Opportunities, the Hub, settings —
stays reachable, and the two sample items are real content from real sources.

### Where the three boxes go

Into the existing onboarding layout rather than a new screen, so the route and the shell are
unchanged:

1. **What are you interested in?** → `Interests` *(exists)*
2. **What are your goals?** → `Goals` *(**missing** — `PrimaryGoal` is a single string and must
   become a list; box 2 allows several)*
3. **What opportunities are you looking for?** → `OpportunityPreferences` *(exists)*

Then the category — Student, Graduate, Professional — if not already set.

Type-ahead throughout, with custom entries always accepted. The guide is firm on this and it
matches what the subject picker already does: *"do not force users to choose from fixed options
when their own wording is more appropriate."*

---

## 5. The Learning Goal Box

The guide wants one prominent box: *"What do you want to learn or achieve?"* We built three
questions — subject, starting level, desired outcome.

These reconcile. The guide adds: *"Ask a short follow-up question only when necessary."* Level
and outcome are exactly that, and they are not cosmetic — they are why a 1,287-page Java textbook
came back as five correctly-levelled sections instead of one generic one. Dropping them would
measurably worsen the result.

**One box first, then the two follow-ups.** Not a rebuild; a reordering.

---

## 6. The rules we are keeping

From the guide, and from what this codebase has already paid to learn:

- Never fabricate a source, date, deadline, author or eligibility. Say *not verified* instead.
- Never infer what someone wants from what they study. Ask.
- A screen with nothing true to show says so, and says what to do about it.
- One clear primary action per screen.
- Every persona is served by one system with different recommendations — never three versions of
  the same product.
- A claim of relevance must be true. Half a phrase matching is not a match.

---

## 7. Decided

Answered by the product owner, 2026-10-27.

1. **Opportunities sits inside Intelligence.** Agreed. Two products at the top level.
2. **Personalisation is a hard requirement.** The feed does not populate until the three boxes
   are answered. Not a card among many — the prompt *is* the screen.
3. **The unpersonalised feed is empty**, save for one or two general items so it is not a void.

That third answer removes the dependency this document was worried about. If an unpersonalised
user sees an empty feed rather than a generic one, the source mix no longer has to be fixed
before this ships — nobody meets sport under a heading promising relevance, because nobody meets
a populated feed at all.

It also makes all three surfaces consistent for the first time:

| | before personalising |
| --- | --- |
| Intelligence | empty feed, with the three questions |
| Learn | empty path, with the three questions |
| Opportunities | nothing to match against, with the reason |

### How "personalised" is decided

Derived, not flagged: **at least one interest and at least one goal.**

A stored flag would need a migration and would lie about anyone who completed the older
onboarding, which never asked for goals. Deriving it means those users are correctly treated as
not yet personalised and asked once — three boxes, not six steps — and no migration is needed.

Opportunity preferences are asked for but not required. Someone who wants nothing applied for
should still get a feed.

---

## 8. Built

2026-10-27. What exists, against what §7 decided.

| Decision | Where it lives |
| --- | --- |
| Three boxes, one button | `Personalise.tsx` + `TagInput.tsx`, route `/personalise` |
| Suggestions that never constrain | `lib/personalisation.ts` — 235 subjects, 12 goals, 12 opportunity types, and anything typed |
| Asked on the first screen after signing in | `Today.tsx` withholds the brief until personalised |
| The feed is a sample, labelled as one | `MongoIntelligenceFeedService.GeneralSampleAsync` — two tier-1 items, honouring the chosen type |
| Derived, not flagged | `UserProfile.IsPersonalised` |
| Named on the wire | `FeedEmptyReason.NotPersonalisedYet` |

Two things learned writing it, both recorded as tests:

- **The rule has to go in every entry point.** `GetFeedAsync` had it; `GetFeedPageAsync` — the
  method the feed page actually calls — only named the reason and otherwise ran the full query
  against an empty profile, so the client drew the three questions above a complete page of
  unranked content. `PersonalisationTests` now fails the build if either method queries before
  it checks.
- **The sample must not be decorated.** `DecorateAsync` writes a "why this matters to you" per
  item; on a profile that has said nothing there is no honest sentence to write, only an LLM
  call spent inventing one.

### §3.1 — Opportunities into Intelligence

Done. It is a row in the Intelligence sidebar, on the same route, with the pipeline and deadline
tracking unchanged. The switcher and the mobile tabbar now offer two products and an account.

### §3.2 — Research Discovery into the Hub

**Blocked on data, not on UI.** The backend is intact and the page can be built in an afternoon.
What it would have been built on could not ship:

`ResearchPaperCatalog` was 353 hand-written lines returned ahead of the live OpenAlex results,
describing itself as peer-reviewed work with real DOIs and verified publishers. Six of its eight
DOIs resolve to 404 at doi.org. The authors, journals, methodologies and statistics were written
by hand, and a citation endpoint would format any of them into a reference list on request.

It is deleted, along with a fabricated paper in the feed seed and sixteen seeded opportunities —
five invented fellowships, three invented job openings, and sixteen deadlines computed from
`DateTime.UtcNow` including those of eight real programmes. Three tests fail the build on a
return.

So Research Discovery is now a page over `GET /api/research/search`, which is OpenAlex and only
OpenAlex: real papers, real DOIs, real open-access links, and an empty result when nothing
matches. That is the next thing to build.

Built. A search box and a list of papers, reached from the Hub's Research stream, on `/research`
with the Hub claiming the route. Citation in five styles, a plain-language explanation and a gap
analysis, each fetched on request.

---

## 9. The AI engine was answering nothing

Found while checking the research actions worked before building UI over them. Worth recording
because it was invisible from the product and because it changes what "a feature works" means
here.

`GET /api/ai/status` reported `isCloudLlmActive: false`, `isBudgetExhausted: false`, and USD 2.55
of a 5.00 monthly cap spent. Every model call in the product was failing and falling back to the
deterministic brain. Three distinct causes, each only visible after fixing the one before it:

1. **No `max_tokens` was ever sent.** OpenRouter prices the reservation, not the answer, so each
   request asked for the model's full 16,384-token allowance and was refused outright. Now capped
   per feature — 8,000 for generation, 1,600 otherwise.
2. **402 `in_flight_budget_exhausted`.** Enrichment runs four at a time and they exhausted the
   in-flight credit against each other. The json path retried after 250 ms, which cannot help; the
   text path did not retry at all.
3. **429 from the provider.** Visible only once the text path started reporting its reason, and it
   was in the give-up bucket.

Two things the same investigation exposed that were not about credit:

- The streamed chat — Ask Radar and Mentor — builds its own request body and was missed when the
  other two call sites were capped.
- A text failure was invisible on `/api/ai/status`: the activity row read "Text generated via
  resilient brain", recorded `success: true`, whether the key was revoked, the balance was out, or
  nothing had gone wrong. The json path had been fixed for this; the text path had not.

**Still outstanding and not a code problem:** the OpenRouter key needs credit. The in-flight
budget scales with available balance, so a key near its limit sustains roughly one concurrent
request however good the backoff is.

---

## 10. Then

- The merged Learning Goal Box: one prominent box, then the two existing follow-ups.
- "Learn This Skill" on a feed card.
- Persona-shaped document assessment (essay / CV / report).
- Open from before: 9 of 20 `Video` items have no playable URL; no transcripts are stored;
  practice-exercise completion is unreachable; `radarproapp.com` is unassigned.
