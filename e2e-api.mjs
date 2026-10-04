// End-to-end API test for RadarV2.
//
// Drives the real HTTP surface against a real MongoDB (and whatever AI provider is configured),
// so it covers the wiring the unit-level code cannot: JWT auth, per-user data isolation, the
// behavioural-signal pipeline, and the AI-generated project plan / learning pathway endpoints.
//
// AI-keyed behaviour is asserted on *shape*, not on specific prose, and every AI feature has a
// deterministic fallback — so this passes with OpenRouter:ApiKey empty (the fallback path is then
// what gets verified) and with a key set (the generated path is verified).
//
// Usage:
//   ASPNETCORE_ENVIRONMENT=Development dotnet run --project RadarV2/RadarV2.csproj \
//     --no-launch-profile --urls http://127.0.0.1:5080
//   node RadarV2/e2e-api.mjs
//
// Override the target with RADAR_API_BASE. Each run registers fresh users, so it is re-runnable
// against a scratch database without cleanup.

const BASE = (process.env.RADAR_API_BASE ?? "http://127.0.0.1:5080").replace(/\/+$/, "");
const STAMP = Date.now();
const PASSWORD = "radar-e2e-pass-123";

let passed = 0;
const failures = [];

function check(name, ok, detail) {
  if (ok) {
    passed++;
    console.log(`  ok    ${name}`);
  } else {
    failures.push(name);
    console.log(`  FAIL  ${name}${detail ? ` — ${detail}` : ""}`);
  }
}

async function call(method, path, { token, body } = {}) {
  const headers = {};
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = `Bearer ${token}`;

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (res.status === 204 || res.status === 205) return { status: res.status, json: null };

  const text = await res.text();
  let json = null;
  try {
    json = text ? JSON.parse(text) : null;
  } catch {
    json = null;
  }
  return { status: res.status, json };
}

/** Case-insensitive lookup of one signal term in the signals payload. */
const signalFor = (payload, term) =>
  (payload?.signals ?? []).find((s) => s.term?.toLowerCase() === term.toLowerCase());

async function readSignals(token) {
  const res = await call("GET", "/api/me/signals?take=50", { token });
  return { res, payload: res.json };
}

console.log(`\nRadar API E2E → ${BASE}\n`);

// ── 1. Auth ───────────────────────────────────────────────────────────────
console.log("Auth");
const email = `e2e-${STAMP}@example.com`;
const register = await call("POST", "/api/auth/register", {
  body: { name: "E2E Runner", email, password: PASSWORD },
});
check("register issues a token", register.status === 200 && !!register.json?.token, `HTTP ${register.status}`);
const token = register.json?.token;

if (!token) {
  console.log("\nCannot continue without a token.");
  process.exit(1);
}

const anon = await call("GET", "/api/me");
check("GET /api/me without a token → 401", anon.status === 401, `HTTP ${anon.status}`);

const me = await call("GET", "/api/me", { token });
check("GET /api/me with a token → 200", me.status === 200, `HTTP ${me.status}`);

// ── 2. Declared signals from onboarding ───────────────────────────────────
console.log("\nDeclared signals");
const INTERESTS = ["Climate", "Public Policy", "Data Science", "Finance"];
const onboarding = await call("POST", "/api/me/onboarding-complete", {
  token,
  body: {
    name: "E2E Runner",
    persona: "Graduate",
    primaryGoal: "Become a climate policy analyst",
    region: "Africa",
    city: "Lagos",
    interests: INTERESTS,
  },
});
check("onboarding-complete → 200", onboarding.status === 200, `HTTP ${onboarding.status}`);

const declared = await readSignals(token);
check("GET /api/me/signals → 200", declared.res.status === 200, `HTTP ${declared.res.status}`);
check(
  "stated interests are persisted as declared signals",
  (declared.payload?.declaredCount ?? 0) >= INTERESTS.length,
  `declaredCount=${declared.payload?.declaredCount}, expected >= ${INTERESTS.length}`,
);
for (const interest of INTERESTS) {
  check(`declared signal recorded: ${interest}`, !!signalFor(declared.payload, interest));
}
const declaredSignal = signalFor(declared.payload, "Climate");
check(
  "declared signals start strong and confident",
  (declaredSignal?.strength ?? 0) >= 0.7 && (declaredSignal?.confidence ?? 0) >= 0.8,
  `strength=${declaredSignal?.strength} confidence=${declaredSignal?.confidence}`,
);
check(
  "profile confidence is reported and above the floor",
  typeof declared.payload?.profileConfidence === "number" && declared.payload.profileConfidence > 0.2,
  `profileConfidence=${declared.payload?.profileConfidence}`,
);

// ── 3. Inferred signals from behaviour ────────────────────────────────────
console.log("\nInferred signals (gradual accumulation, overrides, decay)");
const TERM = "carbon markets";
const event = (type, metadata) => call("POST", "/api/events", { token, body: { type, metadata } });

const search1 = await event("Search", { term: TERM });
check("POST /api/events (Search) → 204", search1.status === 204, `HTTP ${search1.status}`);

const afterFirst = signalFor((await readSignals(token)).payload, TERM);
check(
  "a search term becomes an inferred signal",
  !!afterFirst && afterFirst.source === "Inferred",
  JSON.stringify(afterFirst ?? null),
);
check(
  "a fresh signal has decay applied but is not yet aged",
  typeof afterFirst?.decayedStrength === "number" &&
    Math.abs(afterFirst.decayedStrength - afterFirst.strength) < 1e-9,
  `decayedStrength=${afterFirst?.decayedStrength} strength=${afterFirst?.strength}`,
);

const strengthOne = afterFirst?.strength ?? 0;
await event("Search", { term: TERM });
const afterSecond = signalFor((await readSignals(token)).payload, TERM);
check(
  "repeating the action strengthens the signal (diminishing returns, still increasing)",
  (afterSecond?.strength ?? 0) > strengthOne,
  `${strengthOne} → ${afterSecond?.strength}`,
);

await event("LessLikeThis", { term: TERM });
const afterNegative = signalFor((await readSignals(token)).payload, TERM);
check(
  "explicit negative feedback suppresses the signal",
  (afterNegative?.strength ?? 0) < (afterSecond?.strength ?? 0),
  `${afterSecond?.strength} → ${afterNegative?.strength}`,
);
check(
  "negative feedback is recorded as an override",
  (afterNegative?.explicitFeedback ?? 0) < 0 && (afterNegative?.negativeCount ?? 0) > 0,
  `explicitFeedback=${afterNegative?.explicitFeedback} negativeCount=${afterNegative?.negativeCount}`,
);

// Save is mapped onto a signal independently of any content item, so this is deterministic
// regardless of whether the ingestion worker has enriched anything yet.
const SAVE_TERM = "energy transition";
await event("Save", { term: SAVE_TERM });
const afterSave = signalFor((await readSignals(token)).payload, SAVE_TERM);
check(
  "Save is mapped onto a signal term",
  !!afterSave && (afterSave.positiveCount ?? 0) > 0,
  JSON.stringify(afterSave ?? null),
);

// ── 4. Content-bound behaviour + personalised "why" ───────────────────────
console.log("\nFeed + personalised why");
const feed = await call("GET", "/api/feed?limit=20", { token });
check(
  "GET /api/feed → 200 page envelope",
  feed.status === 200 && Array.isArray(feed.json?.items) && "nextCursor" in (feed.json ?? {}),
  `HTTP ${feed.status}, keys=${Object.keys(feed.json ?? {}).join(",")}`,
);

const item = feed.json?.items?.[0] ?? null;
if (item) {
  const detail = await call("GET", `/api/feed/${item.id}`, { token });
  check("GET /api/feed/{id} → 200", detail.status === 200, `HTTP ${detail.status}`);

  const saved = await call("POST", `/api/feed/${item.id}/save`, { token });
  check("save a feed item → 204", saved.status === 204, `HTTP ${saved.status}`);

  const why = await call("GET", `/api/feed/${item.id}/why`, { token });
  check(
    "GET /api/feed/{id}/why → 200 with a next move",
    why.status === 200 && typeof why.json?.nextMove === "string" && why.json.nextMove.length > 0,
    `HTTP ${why.status}, nextMove=${JSON.stringify(why.json?.nextMove)}`,
  );

  const openEvent = await event("Open", { contentItemId: item.id });
  check("content-bound event is accepted → 204", openEvent.status === 204, `HTTP ${openEvent.status}`);
} else {
  console.log("  skip  no ingested feed items available yet (ingestion worker still filling)");
}

// ── 4b. Cursor pagination, sparse fieldsets, compression ──────────────────
console.log("\nPagination, sparse fieldsets, compression");
const page1 = await call("GET", "/api/feed?limit=3", { token });
check("limit is honoured", (page1.json?.items ?? []).length <= 3, `items=${page1.json?.items?.length}`);

if (page1.json?.nextCursor) {
  const page2 = await call("GET", `/api/feed?limit=3&cursor=${encodeURIComponent(page1.json.nextCursor)}`, { token });
  check("following nextCursor returns a further page", page2.status === 200 && (page2.json?.items ?? []).length > 0, `HTTP ${page2.status}`);

  const ids1 = new Set((page1.json.items ?? []).map((i) => i.id));
  const overlap = (page2.json?.items ?? []).filter((i) => ids1.has(i.id));
  // The whole point of a cursor: a page boundary must not repeat a row. With an offset, one insert
  // between the two requests would shift the window and duplicate an item here.
  check("page 2 does not repeat any item from page 1", overlap.length === 0, `duplicates=${overlap.map((i) => i.id).join(",")}`);
  check("page 2 advances to a different cursor", page2.json?.nextCursor !== page1.json?.nextCursor, "cursor repeated");
} else {
  console.log("  skip  only one page of feed content available");
}

const badCursor = await call("GET", "/api/feed?cursor=not-a-real-cursor", { token });
// A malformed cursor is treated as "no cursor" rather than a 500: it degrades to the first page.
check("a malformed cursor does not error", badCursor.status === 200, `HTTP ${badCursor.status}`);

const sparse = await call("GET", "/api/feed?limit=2&fields=id,title,source", { token });
const sparseKeys = Object.keys(sparse.json?.items?.[0] ?? {}).sort();
check("fields projects to exactly the requested keys", JSON.stringify(sparseKeys) === JSON.stringify(["id", "source", "title"]), `keys=${sparseKeys.join(",")}`);
check("fields keeps the pagination envelope", "nextCursor" in (sparse.json ?? {}), `keys=${Object.keys(sparse.json ?? {}).join(",")}`);

const badField = await call("GET", "/api/feed?fields=id,notARealField", { token });
check("an unknown field is rejected rather than ignored", badField.status === 400, `HTTP ${badField.status}`);

// Compression: JSON must come back brotli or gzip encoded. Read the header directly rather than
// trusting fetch, which transparently decompresses.
const compressed = await fetch(`${BASE}/api/me/signals`, {
  headers: { Authorization: `Bearer ${token}`, "Accept-Encoding": "gzip, br" },
});
const encoding = compressed.headers.get("content-encoding");
check("JSON responses are compressed", encoding === "br" || encoding === "gzip", `content-encoding=${encoding}`);

// ...but the SSE stream must NOT be, or chat would be buffered until the response completed.
const sse = await fetch(`${BASE}/api/mentor/chat/stream`, {
  method: "POST",
  headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json", "Accept-Encoding": "gzip, br" },
  body: JSON.stringify({ message: "one word answer please", history: [] }),
});
check("the chat stream is still served as SSE", (sse.headers.get("content-type") ?? "").includes("text/event-stream"), `content-type=${sse.headers.get("content-type")}`);
check("the chat stream is not compression-buffered", sse.headers.get("content-encoding") === null, `content-encoding=${sse.headers.get("content-encoding")}`);
await sse.body?.cancel();

// ── 5. Overriding a signal ────────────────────────────────────────────────
console.log("\nExplicit overrides");
const removedTerm = "Finance";
const removed = await call("DELETE", `/api/me/signals?term=${encodeURIComponent(removedTerm)}`, { token });
check("DELETE /api/me/signals?term= → 204", removed.status === 204, `HTTP ${removed.status}`);

const afterRemoval = signalFor((await readSignals(token)).payload, removedTerm);
check(
  "a removed topic is suppressed below the ranking threshold",
  !afterRemoval || afterRemoval.strength < 0.2,
  `strength=${afterRemoval?.strength}`,
);
check(
  "the removal is stored as an explicit override",
  !afterRemoval || afterRemoval.explicitFeedback < 0,
  `explicitFeedback=${afterRemoval?.explicitFeedback}`,
);

// Dropping an interest through the profile must suppress it too, not just the DELETE endpoint.
const droppedTerm = "Data Science";
const put = await call("PUT", "/api/me", {
  token,
  body: { interests: ["Climate", "Public Policy"] },
});
check("PUT /api/me with fewer interests → 200", put.status === 200, `HTTP ${put.status}`);
const afterDrop = signalFor((await readSignals(token)).payload, droppedTerm);
check(
  "dropping an interest suppresses its declared signal",
  !afterDrop || afterDrop.strength < 0.2,
  `strength=${afterDrop?.strength}`,
);

const noTerm = await call("DELETE", "/api/me/signals", { token });
check("DELETE /api/me/signals without a term → 400", noTerm.status === 400, `HTTP ${noTerm.status}`);

// ── 6. AI learning pathway ────────────────────────────────────────────────
console.log("\nAI learning pathway");
const pathway = await call("POST", "/api/roadmaps/generate", { token });
check("POST /api/roadmaps/generate → 200", pathway.status === 200, `HTTP ${pathway.status}`);
const modules = pathway.json?.modules ?? [];
check("pathway has modules", modules.length > 0, `modules=${modules.length}`);
check(
  "every module has lessons",
  modules.length > 0 && modules.every((m) => (m.lessons ?? []).length > 0),
  JSON.stringify(modules.map((m) => (m.lessons ?? []).length)),
);
check(
  "only the first module is unlocked",
  modules.length > 0 && modules[0].isLocked === false && modules.slice(1).every((m) => m.isLocked === true),
);
check(
  "the pathway is attributed to the profile goal",
  pathway.json?.goal === "Become a climate policy analyst",
  `goal=${JSON.stringify(pathway.json?.goal)}`,
);

const active = await call("GET", "/api/roadmaps/active", { token });
check(
  "the generated pathway is the active roadmap",
  active.status === 200 && active.json?.id === pathway.json?.id,
  `HTTP ${active.status}`,
);

const firstModule = modules[0];
const firstLesson = firstModule?.lessons?.[0];
if (firstModule && firstLesson) {
  const done = await call(
    "POST",
    `/api/roadmaps/${pathway.json.id}/modules/${firstModule.id}/lessons/${firstLesson.id}/complete`,
    { token },
  );
  check("complete a lesson → 204", done.status === 204, `HTTP ${done.status}`);

  const progress = await call("GET", `/api/roadmaps/${pathway.json.id}/progress`, { token });
  check(
    "progress reflects the completed lesson",
    (progress.json?.progressPercent ?? 0) > 0,
    `progressPercent=${progress.json?.progressPercent}`,
  );
}

// ── 7. AI project plan ────────────────────────────────────────────────────
console.log("\nAI project plan");
const project = await call("POST", "/api/projects", {
  token,
  body: {
    title: "Climate finance brief",
    description: "Produce a two-page brief on carbon market mechanisms for African SMEs.",
    category: "Research",
  },
});
check("POST /api/projects → 201 with an id", project.status === 201 && !!project.json?.id, `HTTP ${project.status}`);
const projectId = project.json?.id;

const plan1 = await call("POST", `/api/projects/${projectId}/plan`, { token });
check("POST /api/projects/{id}/plan → 200", plan1.status === 200, `HTTP ${plan1.status}`);
const milestones = plan1.json?.plan?.milestones ?? [];
check("plan has milestones", milestones.length > 0, `milestones=${milestones.length}`);
check(
  "every milestone carries concrete steps",
  milestones.length > 0 && milestones.every((m) => (m.steps ?? []).length > 0),
  JSON.stringify(milestones.map((m) => (m.steps ?? []).length)),
);
check(
  "plan reports deliverables and a completion test",
  (plan1.json?.plan?.deliverables ?? []).length > 0 && !!plan1.json?.plan?.successCriteria,
);
check("the project is marked with its plan", !!plan1.json?.plan?.generatedAt);

const plan2 = await call("POST", `/api/projects/${projectId}/plan`, { token });
// Compare at millisecond precision: Mongo stores DateTime with ms resolution, so the value that
// comes back differs from the freshly-generated one only in sub-millisecond digits.
check(
  "regenerating returns the stored plan rather than rebuilding it",
  Date.parse(plan1.json?.plan?.generatedAt) === Date.parse(plan2.json?.plan?.generatedAt) &&
    JSON.stringify((plan1.json?.plan?.milestones ?? []).map((m) => m.title)) ===
      JSON.stringify((plan2.json?.plan?.milestones ?? []).map((m) => m.title)),
  `${plan1.json?.plan?.generatedAt} vs ${plan2.json?.plan?.generatedAt}`,
);


const mine = await call("GET", "/api/projects", { token });
check(
  "GET /api/projects returns the project with its stored plan",
  (mine.json ?? []).some((p) => p.id === projectId && !!p.plan),
);
check(
  "the plan survives a fresh read from the database",
  Date.parse(mine.json?.find((p) => p.id === projectId)?.plan?.generatedAt) ===
    Date.parse(plan1.json?.plan?.generatedAt),
);

const fetched = await call("GET", `/api/projects/${projectId}`, { token });
check(
  "GET /api/projects/{id} returns the project (201 Location target resolves)",
  fetched.status === 200 && fetched.json?.id === projectId,
  `HTTP ${fetched.status}`,
);

const badPlan = await call("GET", "/api/projects/does-not-exist/plan", { token });
check("unknown project plan → 404", badPlan.status === 404, `HTTP ${badPlan.status}`);

// ── 8. Research (AI-engine backed) + isolation ────────────────────────────
console.log("\nResearch + per-user isolation");
const research = await call("GET", "/api/research/search?q=climate%20finance", { token });
check("GET /api/research/search → 200", research.status === 200, `HTTP ${research.status}`);
const noQuery = await call("GET", "/api/research/search", { token });
check("research search without a query → 400", noQuery.status === 400, `HTTP ${noQuery.status}`);

const otherEmail = `e2e-other-${STAMP}@example.com`;
const other = await call("POST", "/api/auth/register", {
  body: { name: "E2E Other", email: otherEmail, password: PASSWORD },
});
check("second user registers", other.status === 200 && !!other.json?.token, `HTTP ${other.status}`);
const otherPlan = await call("GET", `/api/projects/${projectId}/plan`, { token: other.json?.token });
check(
  "another user cannot read this project's plan → 404",
  otherPlan.status === 404,
  `HTTP ${otherPlan.status}`,
);
const otherRoadmap = await call("GET", "/api/roadmaps", { token: other.json?.token });
check(
  "another user's roadmap list is empty",
  otherRoadmap.status === 200 && (otherRoadmap.json ?? []).length === 0,
  `HTTP ${otherRoadmap.status}, count=${(otherRoadmap.json ?? []).length}`,
);

// ── Report ────────────────────────────────────────────────────────────────
console.log(`\n${passed} passed, ${failures.length} failed`);
if (failures.length) {
  console.log("\nFailures:");
  for (const failure of failures) console.log(`  - ${failure}`);
  process.exitCode = 1;
}
