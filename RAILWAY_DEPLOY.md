# Deploy Radar V2 on Railway

This backend is an **API-only .NET 10 host** (Blazor/Razor UI retired — see
`REACT_MIGRATION.md` Phase 4). The React SPA (`web/`) is built and deployed
**separately** (Netlify/Vercel) and calls this API cross-origin with a JWT. Nothing from
`web/dist` is served by this service.

`Dockerfile` is already Railway-ready: it listens on `${PORT}` (Railway injects
`PORT`) and falls back to 80. No `railway.json` is required — point Railway at the repo
and it builds the `RadarV2/` Dockerfile.

## Prerequisites
- A Railway account and a project with a **MongoDB** service (or an external Atlas cluster)
- Your MongoDB connection string — **rotate the Atlas credential first if you are using
  Atlas**; a prior version of this repo committed it in plaintext
- The SPA deployed somewhere with an HTTPS origin (Netlify/Vercel)

---

## Step 1: Create the service

1. Railway → **New Project** → **Deploy from GitHub repo** → pick this repo.
2. In the service **Settings → Build**, set:
   - **Dockerfile path**: `Dockerfile`

  > Paths are relative to the **repository root**, and this repository's root *is* the `RadarV2`
  > folder. So it is `Dockerfile`, not `RadarV2/Dockerfile` — the latter fails the build with
  > "couldn't locate the dockerfile at path". The prefix is a leftover from when this project
  > lived one directory below the workspace root.
   - (Root directory can stay at the repo root; the Dockerfile copies its own context.)
3. Railway builds and deploys. First boot will log the ingestion worker starting up.

> If you add a **MongoDB** plugin inside the same Railway project, Railway exposes
> `MONGO_URL` / `MONGOHOST` etc. to linked services. The simplest path is to set
> `MongoDB__ConnectionString` explicitly from that URL.

---

## Step 2: Environment variables

Set these on the Railway service (**Variables**):

| Name | Value |
|---|---|
| `MongoDB__ConnectionString` | your Mongo/Atlas connection string |
| `MongoDB__DatabaseName` | `radar` |
| `Auth__Jwt__Key` | a long random secret — **required** in Production (the app refuses to start with the checked-in dev key) |
| `Auth__Jwt__Issuer` | `Radar` (optional; defaults to `Radar`) |
| `Auth__Jwt__Audience` | `Radar` (optional; defaults to `Radar`) |
| `OpenRouter__ApiKey` | your OpenRouter key (optional — AI features fall back deterministically when empty) |
| `Cors__AllowedOrigins__0` | your SPA origin, e.g. `https://your-app.netlify.app` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

**Notes**
- Railway injects `PORT`; the Dockerfile already binds to it — do not set `ASPNETCORE_URLS`.
- `https://*.vercel.app` and `https://*.netlify.app` origins are always allowed by the
  CORS policy (`Program.cs`), so `Cors__AllowedOrigins__0` is only needed for a custom
  domain.

---

## Step 3: Deploy the SPA separately

The frontend is its own build and its own host:

1. `cd web && npm run build` produces `dist/`.
2. Deploy `dist/` to Netlify or Vercel (the repo ships `web/netlify.toml` with the SPA
   redirect already configured).
3. Set `VITE_API_BASE_URL` at build time to the Railway service URL
   (e.g. `https://radar-v2-api.up.railway.app`). Without it the app falls back to
   `http://127.0.0.1:5080`, which is dev-only.
4. Add the SPA origin to `Cors__AllowedOrigins__0` (skip for `*.netlify.app` /
   `*.vercel.app`).

---

## Step 4: Verify

1. `curl https://<railway-url>/api/feed` → `401` with a JSON body
   (`{"error":"Unauthorized"}`). Confirms the API-only host is up.
2. `curl https://<railway-url>/api/nope` → `404` with a JSON body.
   (There is no landing page — unknown routes return JSON, not HTML.)
3. Open the deployed SPA, sign in, confirm `/feed` loads. The browser network tab should
   show requests to the Railway URL with `Authorization: Bearer …` and
   `Access-Control-Allow-Origin` on the responses.
4. Confirm the `radar` database collections are created automatically.

---

## Step 5: Scheduled ingestion (Railway cron)

Ingestion also runs on an in-process 6h timer, but a Railway cron guarantees it happens even if
the service restarts, idles, or you want a fixed wall-clock schedule. It works by calling a small
protected endpoint on the API.

**1. Set the shared secret on the API service** (the same service from Step 1):

| Name | Value |
|---|---|
| `Jobs__CronKey` | a long random secret |

> **Two underscores, not one.** ASP.NET Core maps `__` to the `:` separator, so `Jobs__CronKey`
> becomes `Jobs:CronKey`. A single underscore makes a flat key nothing reads: the variable shows in
> the dashboard, the feature stays off, and the only symptom is a `503` identical to the variable
> not existing. The same applies to `MongoDB__ConnectionString`, `OpenRouter__ApiKey`,
> `YouTube__ApiKey` and `Auth__Jwt`. The service logs a warning at startup if it spots a
> single-underscore variant, and `GET /api/version` reports which secrets it can actually see.

The endpoint refuses to run until this is set (`503`), so it can never be triggered accidentally.

**2. Create a second Railway service in the same project** for the cron container:

- **New Service → Deploy from the same GitHub repo**
- **Settings → Build → Dockerfile path**: `Dockerfile.cron`
- **Settings → Deploy → Cron Schedule**: `0 */6 * * *` (every 6 hours)
- **Variables**:

| Name | Value |
|---|---|
| `API_BASE_URL` | your API host, e.g. `https://radar-v2-backend-production.up.railway.app` |
| `CRON_KEY` | the **same** value as `Jobs__CronKey` above |

Railway starts the container on schedule; it POSTs to `/api/jobs/ingestion?async=true` and exits
immediately (the API carries the cycle on in the background, so the cron container never has to
stay alive for the whole run).

**Manual check** (also useful for debugging):
```bash
curl -X POST https://<railway-url>/api/jobs/ingestion \
  -H "X-Cron-Key: <your CronKey>" -w "\nHTTP %{http_code}\n"
# → 200 {"ran":true,...}          (ran one cycle)
#   ?async=true → 202 {"started":true,"mode":"async"}
#   401 invalid key · 503 Jobs__CronKey not set
```

Adjust the schedule to taste, e.g. `0 */3 * * *` (3-hourly) or `30 23 * * 6` (Saturdays for a
weekly brief). DST/UTC: Railway cron uses UTC.

### Adding more cron services

Each scheduled job is one more Railway service built from the **same** `Dockerfile.cron`.
They differ only in the `JOB` variable and the schedule, so adding one is a copy of Step 5.2:

| Service | `JOB` | Suggested schedule | What it does |
|---|---|---|---|
| Content ingestion | `ingestion?async=true` | `0 */6 * * *` | Pulls articles, videos, podcasts, papers |
| Opportunity ingestion | `opportunity-ingestion` | `0 3 * * *` | Fetches opportunities from their source feeds |
| Opportunity batch | `opportunity-batch` | `15 4 * * *` | Builds each user's curated 12 for the day |

Order matters for the last two: the batch should run **after** ingestion, so the day's selection
can draw on whatever arrived overnight. The gap above is deliberate.

All three take the same `API_BASE_URL` and `CRON_KEY` variables. Every job refuses to run with a
`503` until `Jobs__CronKey` is set on the API service, and returns `401` on a wrong key.

```bash
# Check any job by hand
curl -X POST https://<railway-url>/api/jobs/opportunity-ingestion   -H "X-Cron-Key: <your CronKey>" -w "
HTTP %{http_code}
"
# → 200 {"added":56}
```

---

## Troubleshooting

- **Deploy fails / app exits at boot**: most likely `Auth__Jwt__Key` is missing — the app
  throws rather than falling back to the public dev key. Set it and redeploy.
- **502 / health check failing**: the container must listen on `PORT`. Confirm you did not
  override `ASPNETCORE_URLS`; the Dockerfile entrypoint handles it.
- **SPA gets CORS errors**: the SPA origin isn't allowed. Add it via
  `Cors__AllowedOrigins__0` (custom domains) — `*.netlify.app` / `*.vercel.app` are
  allowed automatically.
- **No content in the feed**: the ingestion worker runs on a 6h/24h cycle; a fresh
  database starts empty. Seeded demo content inserts only when the collection is empty.
- **Logs**: Railway → your service → **Deployments → View logs**.

---

# Staging and production

**The deployment that exists today stays production.** It reports `environment: Production`, writes
to the `radar` database, and already holds real accounts — the reviewer's among them. Staging is
the *new* environment, stood up alongside it and starting empty.

That direction matters. Relabelling the live deployment as staging would point testers at real user
data and leave production to be recreated from nothing, losing every account on it. The rule is
simply: whichever environment already has the real data is production, regardless of which was
built first.

## What makes them separate

Two environments are only genuinely separate if **all three** of these differ. Sharing any one of
them means a staging mistake can reach real users.

| | Staging | Production |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| Mongo database | `radar_staging` | `radar` |
| OpenRouter spend cap | `5` | `25` |
| Frontend build mode | `staging` | `production` |

The database is the one that matters most. `appsettings.Staging.json` pins `DatabaseName` to
`radar_staging`, so even a staging service accidentally pointed at the production **cluster** still
writes to a different database rather than into real user data. Keep that pin.

## Backend (Railway): environments, not extra services

Railway gives you three ways to do this. Which is right depends on how much isolation you want
versus how much duplicated setup you are willing to maintain.

### Option A — Railway Environments (recommended here)

One project, two environments. "Duplicate Environment" copies the services, variables and
configuration, so the whole topology — the API and all three cron services — is recreated for you,
and each environment then keeps its own variables, private network and deployments. Changes to a
service are scoped to the environment you are in.

This is the right default for this project specifically because the topology is already four
services. Recreating and then hand-syncing four services forever is where drift comes from.

1. In the project, open the environment switcher and **Duplicate** the current environment.
2. Keep the original as `production` — it holds the real data — and name the **copy** `staging`.
3. Set per environment: `ASPNETCORE_ENVIRONMENT`, `MongoDB__ConnectionString`,
   `OpenRouter__ApiKey`, `Jobs__CronKey`, and each cron service's `API_BASE_URL` and `CRON_KEY`.
4. Confirm with `/api/version` before trusting either.

Two things Railway does **not** isolate, worth knowing:

- **Public networking.** Every service with a domain is reachable from the internet regardless of
  environment, so staging is public too. The API requires a JWT on everything except `/api/version`
  and the cron endpoints, which take a shared secret — keep it that way.
- **Service names.** Names are shared across environments, so you cannot have two services with the
  same name but independent identities.

### Option B — Two projects

Harder isolation: separate access control, separate billing view, and no way to edit the wrong
environment's variables by accident because they are not in the same place. Railway staff have
suggested this when fully independent service naming is needed.

The cost is that you recreate all four services by hand and keep them in step yourself. Choose this
if someone other than you will have dashboard access, or if isolation needs to be demonstrable
rather than merely true.

### Not recommended — a second set of services in one environment

Adding `radar-api-production` alongside `radar-api-staging` in a single environment means eight
services distinguished only by name, sharing one variable namespace. It is the easiest to set up
and the easiest to make a mistake in.

## Domains, and which side sets what

| | Frontend (Vercel) | API (Railway) |
|---|---|---|
| Production | `radarproapp.com` | `api.radarproapp.com` |
| Staging | `staging.radarproapp.com` | `stagingapi.radarproapp.com` |

Each environment sets **two** variables that point at each other. They are easy to confuse because
they describe the same relationship from opposite ends:

| Variable | Set on | Means |
|---|---|---|
| `VITE_API_BASE_URL` | Vercel | which API this site calls |
| `Cors__AllowedOrigins__0` | Railway | which site may call this API |

**CORS is always a backend setting.** It is a response header the API sends, and the browser
enforces the answer. A frontend cannot grant itself permission, so there is no Vercel variable for
it. The classic symptom of setting only one side is a request that fails in the browser with a CORS
error while the same request succeeds from `curl` — because `curl` is not a browser and does not
enforce it.

So, in full:

| Service | Variables |
|---|---|
| Vercel production | `VITE_API_BASE_URL=https://api.radarproapp.com` |
| Vercel staging | `VITE_API_BASE_URL=https://stagingapi.radarproapp.com` |
| Railway production | `Cors__AllowedOrigins__0=https://radarproapp.com`, `Cors__Strict=true` |
| Railway staging | `Cors__AllowedOrigins__0=https://staging.radarproapp.com`, `Cors__Strict=true` |

### Order matters

Setting `Cors__Strict=true` before the domain resolves locks you out of your own API, because the
wildcard that is currently carrying the live site stops applying.

1. Add the custom domain in Railway/Vercel and wait for DNS to resolve and the certificate to issue.
2. Confirm the site loads on the new domain and still reaches the API.
3. Set `Cors__AllowedOrigins__0` to the new frontend URL.
4. Only then set `Cors__Strict=true`.

Until step 4, production logs a warning on every boot saying the wildcard is still in use. That
warning going away is the confirmation that strict mode took.

### If you serve both apex and www

Vercel will redirect one to the other, but a redirect does not change the `Origin` header of an API
call. Whichever hostname the browser ends up on is the one the API must allow, so add both:

```
Cors__AllowedOrigins__0=https://radarproapp.com
Cors__AllowedOrigins__1=https://www.radarproapp.com
```

## Frontend (Vercel): staging is a Preview deployment

A `staging` branch deploys as a **Preview**, not Production — Vercel only treats the Production
Branch (`main`) as production. Two ways to give it a stable identity:

**Custom Environment** (Pro and above). Create an environment named `staging`, point its branch
tracking at the `staging` branch, give it its own variables and attach
`staging.radarproapp.com`.

**Branch-assigned domain** (any plan). Assign `staging.radarproapp.com` to the `staging` branch
under Preview, and set Preview variables. Branch-specific values inherit the general Preview set,
so you only override what differs.

> **A domain added in Vercel points at production by default.** Adding
> `staging.radarproapp.com` is not enough — you have to set its Git Branch to `staging` in the
> domain's own settings. Miss it and the staging hostname serves `main`: the site loads, looks
> completely normal, and is production. Nothing warns you, because from the build's point of view
> nothing is wrong. Confirm it by opening the domain and checking the amber STAGING banner is
> there; if there is no banner, the branch assignment did not take.

### DNS

A subdomain is a `CNAME`; an apex domain (`radarproapp.com`) cannot be, so it needs an `A` record.
Both Vercel and Railway show the exact record to create once you add the domain — use what the
dashboard gives you rather than a value copied from anywhere else, including this file, because
they differ per project.

All four are separate domains on two platforms:

| Hostname | Added in | Record |
|---|---|---|
| `radarproapp.com` | Vercel, production | `A` |
| `staging.radarproapp.com` | Vercel, assigned to `staging` | `CNAME` |
| `api.radarproapp.com` | Railway, production environment | `CNAME` |
| `stagingapi.radarproapp.com` | Railway, staging environment | `CNAME` |

After DNS resolves, the certificate issues on its own — usually a few minutes. Until it does the
hostname will error or warn, which is normal and not something to fix by changing settings.

#### Cloudflare: the orange cloud has to be off

If DNS is on Cloudflare, each record has a **Proxy status**. It must be **DNS only** (grey cloud)
for all four hostnames. Proxying breaks both platforms the same way, and the symptom does not look
like a DNS problem at all:

```
$ curl -sSI -L https://staging.radarproapp.com
HTTP/1.1 308 Permanent Redirect
Location: https://staging.radarproapp.com/     <- same URL it was just asked for
server: cloudflare
... forever, until the browser gives up with ERR_TOO_MANY_REDIRECTS
```

That loop is Cloudflare's SSL/TLS mode set to **Flexible**: Cloudflare accepts HTTPS from the
browser but connects to the origin over plain HTTP, the origin redirects HTTP to HTTPS, and
Cloudflare hands that redirect back to the browser, which asks again. Neither side is wrong on its
own. Vercel also flags it as **Proxy Detected**, because proxying disables its DDoS and
bot-mitigation.

Turn the cloud grey and both resolve. Vercel and Railway each terminate TLS and run their own CDN,
so Cloudflare's proxy adds nothing here. (If you ever do want it on, the SSL/TLS mode must be
**Full (strict)** — never Flexible.)

#### "DNS is not propagating" is usually a wildcard

A proxied `*` record answers for every subdomain, including ones you never created:

```
$ nslookup nothing-here-xyz.radarproapp.com 8.8.8.8
Addresses: 104.21.71.163, 172.67.147.66        <- resolves anyway
```

So a missing `CNAME` does not fail the way you expect. The name resolves, the browser connects,
and you debug propagation for an hour over a record that was never added. Check the new hostname
against a name you know is fake — if both answer identically, the wildcard is replying and your
record is missing.

### Variables

Set these on the staging environment:

```
VITE_API_BASE_URL = https://stagingapi.radarproapp.com
VITE_ENVIRONMENT  = staging
```

A real environment variable **overrides the committed `.env.production`**, verified by building
with one set: the staging host was baked in even though the build ran in production mode. So the
default `npm run build` is fine for staging as long as both variables are set.

If you would rather be explicit than rely on that precedence, set the staging environment's Build
Command to `npm run build:staging`, which uses `--mode staging` and the committed `.env.staging`.
CI builds both modes, so a change that only breaks the staging build is caught before it ships.

### Worth knowing

- **Preview variables apply to every preview**, not just the `staging` branch, unless you scope
  them. That is usually what you want: a pull request preview should talk to staging, never to
  production.
- **`VERCEL_ENV` reports `preview` for custom environments too**, so it cannot distinguish staging
  from an ordinary preview. This app does not rely on it — it reads `VITE_ENVIRONMENT`, which you
  set explicitly.
- Any build where `VITE_ENVIRONMENT` is not `production` shows a banner naming the environment and
  the API it is calling, so a mislabelled deployment announces itself.

## Frontend (Vercel)

Vercel's **Production** environment builds `main` with `VITE_API_BASE_URL` pointing at the
production API. Point **Preview** at staging, either through `.env.staging` or by setting the
variable per environment in the dashboard (dashboard wins).

`VITE_API_BASE_URL` has **no production fallback any more**. A build without it fails loudly rather
than quietly talking to the production API, which is the worst possible default once two
environments exist. Any build where `VITE_ENVIRONMENT` is not `production` also shows a banner
naming the environment and the API it is calling.

## Cron

Each environment needs its own cron services, pointed at its own `API_BASE_URL` and holding its own
`CRON_KEY`. Staging crons against staging data are useful; staging crons against production are a
way to corrupt it. Duplicating the environment (Option A) carries the cron services across, so this
is mostly a matter of setting the right variables on each copy.

**A cron service showing "Last run succeeded" does not mean the job ran.** Railway reports whether
the container exited, and the container is a one-line `curl` — so a run where the API answered
`503 Jobs__CronKey not set` still exits and still looks green. The only reliable check is to ask
the API:

```bash
curl -X POST https://<host>/api/jobs/ingestion -H "X-Cron-Key: <key>" -w "
HTTP %{http_code}
"
# 200 = ran · 401 = wrong key · 503 = Jobs__CronKey not set on the API service
```

## Promotion

`main` builds both. Promote by letting staging settle first:

1. Push to `main`. CI must pass.
2. Staging deploys. Check `/api/version` reports the commit you just pushed.
3. Exercise the change on staging.
4. Promote the same image or redeploy production from the same commit.

A production deploy should never be the first time a commit runs anywhere.
