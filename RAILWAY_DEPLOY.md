# Deploy Radar V2 on Railway

This backend is an **API-only .NET 10 host** (Blazor/Razor UI retired — see
`REACT_MIGRATION.md` Phase 4). The React SPA (`web/`) is built and deployed
**separately** (Netlify/Vercel) and calls this API cross-origin with a JWT. Nothing from
`web/dist` is served by this service.

`RadarV2/Dockerfile` is already Railway-ready: it listens on `${PORT}` (Railway injects
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
   - **Dockerfile path**: `RadarV2/Dockerfile`
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

The endpoint refuses to run until this is set (`503`), so it can never be triggered accidentally.

**2. Create a second Railway service in the same project** for the cron container:

- **New Service → Deploy from the same GitHub repo**
- **Settings → Build → Dockerfile path**: `RadarV2/Dockerfile.cron`
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
