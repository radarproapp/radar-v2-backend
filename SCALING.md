# Serving ten thousand learners

What Radar is running on, what it would take, and which half of that is code.

Written while the product is on Atlas M0 and Railway Hobby, which is the
right place to be for the number of people using it today. This is the
map for when that stops being true, not a plan to execute now.

## Where the ceilings are, in the order they bite

**Atlas M0 is the hard stop.** 512 MB of storage, shared and throttled
CPU, and a connection cap in the hundreds. The content corpus alone —
items with transcripts, paper breakdowns, opportunities — will exhaust
512 MB well before ten thousand registered learners, never mind
concurrent ones. M0 is a prototyping tier and Atlas throttles it on
purpose.

**Railway Hobby is one container.** Horizontal scaling is a Pro feature,
so today there is exactly one process serving everything.

**Model calls run inside the request.** Building a path is several
OpenRouter calls — tens of seconds — on the thread serving that HTTP
request. This is the one ceiling that no tier upgrade lifts: at real
concurrency you would need tens of thousands of in-flight requests each
holding a connection for half a minute. It has to move off the request
path.

**The YouTube quota is about a hundred searches a day, product-wide.**
`search.list` costs 100 units against a 10,000/day free allowance.
Results are cached in Mongo and shared across every user, and empty
results are cached too — so this scales with the number of *distinct
lesson subjects*, not with the number of learners. Users clustering on
common paths costs nothing extra; ten thousand learners each inventing a
unique subject hits the wall on day one.

## What is already done, on main

These needed no upgrade and are live:

- Per-caller rate limiting, with a separate and much tighter bucket for
  anything that calls a model or the metered quota.
- The navigator's day cache moved off a static `Dictionary` — it leaked,
  it was not thread-safe, and being per-process it made running a second
  instance incorrect.
- A bounded Mongo connection pool with a wait timeout, so one instance
  cannot take the whole Atlas allowance and a slow database fails rather
  than hanging the server.
- The daily batch streamed and processed in bounded parallel, for
  personalised profiles only.
- Indexes on the fields the hot queries sort and filter by.

## What is left, and what each piece needs

### 1. Model work off the request path — the big one

Every endpoint that builds a path, assesses a document or scores a
project blocks on OpenRouter. The shape that works: the request records
the intent and returns immediately, a worker picks it up, and the client
polls or is pushed to.

Radar already has half of this idea. Lessons "settle" across visits —
`ResourceAttempts` counts tries, and a lesson that has not been searched
yet is retried on a later read rather than blocking the first one. The
same pattern generalises.

Needs: a queue. Without a second process, that is a hosted service inside
the same app reading a Mongo collection — workable, and it still unblocks
the request. With Railway Pro it becomes a separate worker service.

### 2. A shared cache

`IMemoryCache` is per-process. The moment there are two instances they
cache different answers, and the session-version guard that revokes
tokens is one of them. Redis, with the three current `IMemoryCache` users
moved to it.

### 3. Read replicas and the scoring window

`GetScoringCandidatesAsync` takes the five hundred most recently
discovered opportunities and scores them in process, per request. That is
fine at tens of concurrent users and wrong at thousands. Two changes:
serve the daily batch from storage rather than rebuilding on a miss, and
push the scoring into an aggregation pipeline so the five hundred
documents do not cross the wire.

### 4. Backpressure that is visible

A refusal under load should say what it is. The rate limiter answers 429
with a retry hint; the queue needs the same — a path being built is a
state the screen can show, not an error.

## What it costs

Rough, and worth confirming against current pricing:

- Atlas M10 — around $57/month, 10 GB, dedicated CPU. The sane first
  move, and enough for ten thousand registered learners with hundreds
  active.
- Atlas M30+ with replicas — hundreds a month. Only needed for genuinely
  thousands-concurrent.
- Railway Pro — replicas and horizontal scaling.
- Redis — small, and only once there is more than one instance.

The honest sequencing is that **Atlas M10 and getting model calls off the
request path are the whole of the next step**. Everything else on this
page is for a scale that is a long way past that.
