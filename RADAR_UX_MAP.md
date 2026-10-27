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

| State | What the feed is | What it says |
| --- | --- | --- |
| Nothing asked | Latest, unranked | **Latest** — and a standing invitation to personalise |
| Personalised | Ranked on interests, goals, opportunities | **For you** |
| Personalised + used | Ranked, plus what they actually read | **For you** |

The unpersonalised state must be *labelled as unpersonalised*. Deferring the question only works
if the feed does not pretend to be a personalised one that happens to be bad at its job. A new
user seeing sport and entertainment under a heading that promises relevance learns that Radar
does not know them — and that lesson is hard to undo.

### The invitation

Not a modal and not a wall. A card at the top of the feed, in the stream, with the reason
visible:

> **For Radar to serve you better**
> Three short questions and your feed becomes yours.
> *Personalise my Radar*

It persists until answered, because it is a requirement for the product to do its job — but it
never blocks the door. Someone who wants to look around first can.

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

## 7. Open questions

1. **Does the product owner accept Opportunities as a destination inside Intelligence** rather
   than a third product? The guide implies it; it does not say it outright.
2. **What does the unpersonalised feed actually contain?** "Latest" is honest but currently means
   sport and entertainment, because the source list skews that way. This needs the source work
   finished before deferred personalisation is safe to ship.
3. **Does "requirement" mean soft or hard?** This document assumes soft: a persistent invitation,
   enforced per feature at the point of use, never a wall at the door.
