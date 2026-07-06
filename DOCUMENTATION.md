# OTT Platform — User & Admin Documentation

How to **access**, **watch**, and **upload/manage** content on your OTT platform — for both
**Viewers** and **Admins/Studios**. Covers the app UI and the REST API (for programmatic use).

- **Viewer app** (mobile / web / TV): browse, subscribe, watch.
- **Admin panel** (same app, unlocked for `admin` role): upload content, manage catalog, monetize, view analytics.
- **Backend API**: base URL `https://api.yourapp.com/api` (local dev: `http://localhost:50664/api`).

> This platform is **multi-tenant** — each studio/brand is a *tenant*. Every request carries a tenant
> (from the JWT once logged in, or the `X-Tenant-ID` header before login). The default tenant slug is `default`.

---

# PART 1 — Access & Accounts

## 1.1 Roles
| Role | Can do |
|---|---|
| **Viewer** | Browse, subscribe, watch, download, watch-party, community |
| **Creator** | Viewer + submit content via the Creator Portal (`/creator`) |
| **Admin** | Full admin panel (`/admin/*`): upload, catalog, users, monetization, analytics |

A user becomes admin when their account `Role = "admin"` (set in the database or by another admin).

## 1.2 Signing in (Viewer)
In the app: **Splash → Login / Register**.
- **Register:** email, password, name, phone → an OTP is emailed → verify.
- **Login:** email + password (or **Google / Apple / Facebook** social login).
- **Profiles:** after login, pick a profile (multiple per account, incl. **Kids** profiles with a **parental PIN**).

## 1.3 Accessing the Admin panel
Log in with an **admin** account, then open **`/admin`** (or the "Admin" entry in the app menu).
The left nav exposes: Dashboard, Content, Live, Banners, Rows, Users, Plans, Promos, Analytics, Revenue, Branding, Config.

---

# PART 2 — Viewer Guide

## 2.1 Browse & discover
| Where | What |
|---|---|
| **Home** (`/home`) | Personalized rows: continue watching, trending, new releases, genres, banners |
| **Search** (`/search`) | Title / description / director search with filters (type, language, year) |
| **Live** (`/live`) / **Live TV** (`/livetv`) | Live events and IPTV channels (filter by country/language/category) |
| **Community** (`/community`) | Posts & polls |
| **Downloads** (`/downloads`) | Offline titles |

## 2.2 Watching a title
1. Open a title → **Content detail** (`/content/:id`) — synopsis, cast, related titles, rating.
2. Tap **Play** (`/play/:id`). If the title is paywalled and you're not entitled, you're prompted to **subscribe**.
3. **Player features:** quality selector (HD/Auto), subtitles/CC, multi-audio tracks, **resume** (picks up where you left off), **Skip Intro**, playback speed, fullscreen, cast.
4. Your **progress**, **ratings**, and **watchlist** sync to your profile. "Continue Watching" and watch history are automatic.

## 2.3 Subscriptions & payments
- **Plans** (`/subscribe`): view tiers (price, quality, max streams/devices).
- **Checkout:** pay via **Razorpay** (web/Android) or **in-app purchase** (Google Play / Apple).
- **Promo codes:** apply at checkout (validated against active promos).
- **Billing** (`/billing`): current subscription, invoices, cancel.
- Concurrent-stream limits are enforced per plan (`MaxStreams`) — extra devices get a "stop playback elsewhere" message.

## 2.4 Profiles & parental controls
- Up to **5 profiles** per account; each has its own recommendations, watchlist, history.
- **Kids profiles** + **parental PIN** (`/parental-control`) restrict mature content.

## 2.5 Social features
- **Watch Party:** watch in sync with friends via a shared party code (`/watch-party/:code`).
- **Community:** posts and polls per tenant.

---

# PART 3 — Admin / Studio Guide

## 3.1 Dashboard (`/admin`)
At-a-glance: total users, active subscriptions, content count, live streams, monthly revenue, plus quick actions.

## 3.2 Uploading content (the core workflow)

Content has **two parts**: *metadata* (title, genres, cast…) and *media* (the video + poster + tracks).
The video is **transcoded to adaptive HLS** and served through the CDN with signed URLs.

### A. Via the Admin UI
1. **Content → Add** (`/admin/content` → upload). Enter metadata: **Title, Type** (movie / series), description, language, release year, **genres**, **cast**, maturity rating.
2. **Poster / thumbnail:** upload artwork (16:9 banner, 2:3 poster).
3. **Video source — choose one:**
   - **Upload a file** → it's stored and **transcoded to HLS** (multiple bitrates). Large files use a **direct-to-S3 pre-signed upload**.
   - **Link YouTube / Vimeo** → validated and embedded (no transcoding).
4. **Tracks:** add **subtitles** (`.vtt`/`.srt`) and declare **audio tracks** (language + label).
5. **Series:** add **seasons** and **episodes**, each with its own video/tracks.
6. **Publish** — the title goes live in the catalog. (Unpublished = draft, hidden from viewers.)

### B. Via the API (programmatic upload)
> All admin calls need `Authorization: Bearer <admin-JWT>` and the tenant (JWT claim or `X-Tenant-ID`).

```bash
# 1) Log in as admin → get the token
curl -s -X POST "$BASE/api/auth/login" -H "Content-Type: application/json" \
  -H "X-Tenant-ID: $TENANT" \
  -d '{"email":"admin@you.com","password":"••••"}'   # → { data: { accessToken, ... } }

# 2) Create the content (metadata) → returns the new contentId
curl -s -X POST "$BASE/api/admin/contents" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"title":"My Movie","type":"movie","description":"...","genreIds":["<guid>"]}'

# 3a) Upload the poster (multipart)
curl -X POST "$BASE/api/admin/contents/$ID/image" -H "Authorization: Bearer $TOKEN" \
  -F "file=@poster.jpg" -F "kind=poster"

# 3b) Upload the video (multipart) → kicks off HLS transcoding
curl -X POST "$BASE/api/admin/contents/$ID/video" -H "Authorization: Bearer $TOKEN" \
  -F "file=@movie.mp4"
#    OR for large files: request a pre-signed S3 URL and PUT the file directly:
curl -s "$BASE/api/admin/contents/$ID/presigned-upload?contentType=video/mp4" \
  -H "Authorization: Bearer $TOKEN"        # → { uploadUrl }  then:  curl -X PUT --upload-file movie.mp4 "<uploadUrl>"

# 4) Add a subtitle track
curl -X POST "$BASE/api/admin/contents/$ID/subtitles" -H "Authorization: Bearer $TOKEN" \
  -F "file=@english.vtt" -F "language=en" -F "label=English (CC)" -F "format=vtt"

# 5) Publish
curl -X POST "$BASE/api/admin/contents/$ID/publish" -H "Authorization: Bearer $TOKEN"
```

### What happens after a video upload (the pipeline)
`Upload → stored (S3) → FFmpeg transcode job (Hangfire worker) → adaptive HLS renditions → CloudFront`.
Transcoding runs in the background (the **worker** process / `worker` Docker profile). Until it finishes, the
title can exist as a draft; publish once the HLS master is ready. Playback URLs are **signed** and only issued
to **entitled** viewers.

## 3.3 Catalog & merchandising
| Screen | Purpose |
|---|---|
| **Content** (`/admin/content`) | List / edit / delete / publish titles |
| **Banners** (`/admin/banners`) | Home-page hero banners (with CTA + target content) |
| **Rows** (`/admin/rows`) | Home-page rails (genre / tag / trending / new / manual) — controls what viewers see on Home |
| **Branding** (`/admin/branding`) | Logo, colors, app theme per tenant |
| **Config** (`/admin/config`) | Feature flags & runtime settings |

## 3.4 Live streaming & IPTV
- **Live streams:** create a stream → get an **RTMP ingest key** → **Start/Stop**; push from OBS/encoder to the RTMP host (Ant Media). Regenerate the key if leaked.
- **IPTV / Live TV** (`/admin/live`): sync channel lists; viewers browse them under `/livetv`.

## 3.5 Monetization
| Screen | Purpose |
|---|---|
| **Plans** (`/admin/plans`) | Create subscription tiers (price, quality, max streams) |
| **Promos** (`/admin/promos`) | Discount codes (percentage/fixed, max uses, expiry) |
| **Revenue** (`/admin/revenue`) | Revenue over time; export CSV |

## 3.6 Users & compliance
- **Users** (`/admin/users`): search, view, **block/unblock**.
- **Audit logs** (`/api/admin/audit-logs`): every privileged admin action is recorded.
- **CSV exports:** `/api/admin/exports/revenue.csv`, `/users.csv`, `/content-analytics.csv`.

## 3.7 Analytics & viewer insights (first-party data)
Under **Analytics** (`/admin/analytics`) with a period selector (7d / 30d / 90d / 1y):
| Report | Answers |
|---|---|
| **Top content / Regions** | Most-watched titles; viewers by country |
| **When viewers watch** | Hour-of-day + day-of-week viewing histograms |
| **Genre binge** | Watch-time share per genre |
| **Devices & platforms** | Android / iOS / web / TV split |
| **Engagement hotspots** | Where viewers **pause / skip** within a title (tap a top title) |
| **Playback quality (QoE)** | Avg **startup time**, **rebuffers/hour**, **errors**, per-platform — the top churn signal |

These are powered by first-party telemetry the player emits automatically (views, watch-time, pause/seek,
startup/rebuffer/errors). Use them to decide what to greenlight, where content drags, and which platforms have quality issues.

---

# PART 4 — API Reference (quick map)

**Base:** `/api` · **Auth:** `Authorization: Bearer <JWT>` · **Tenant:** JWT claim or `X-Tenant-ID` header.
Responses use the envelope `{ "success": bool, "message": string, "data": … }`.

### Auth — `/api/auth`
`register` · `login` · `social` · `send-otp` · `verify-otp` · `refresh` · `forgot-password` · `reset-password` · `change-password` · `verify-email` · `logout` · `select-profile`

### Catalog & playback — `/api/contents`
`home` · `featured` · `trending` · `recommendations` · `new-releases` · `search` · `{id}` · `{id}/stream` (signed URLs, entitlement-gated) · `{id}/related` · `genre/{genreId}` · `{id}/rate` · `{id}/events` (telemetry) · `{id}/qoe` (quality telemetry)
Also: `/api/series/{id}/episodes` · `/api/watch-history` (+ `/continue`, `{id}/progress`) · `/api/watchlist` · `/api/streams/heartbeat` · `/api/streams/stop`

### Subscriptions — `/api`
`plans` · `subscriptions/me` · `subscriptions/initiate` · `subscriptions/confirm` · `subscriptions/cancel` · `subscriptions/iap/google` · `subscriptions/iap/apple` · `promo/validate` · `invoices`

### Live TV — `/api/livetv`
`channels` · `filters` · (admin) `POST /api/admin/livetv/sync`

### Admin — `/api/admin`
**Content:** `contents` (GET list / POST create) · `contents/{id}` (GET/PUT/DELETE) · `contents/{id}/image` · `contents/{id}/video` · `contents/{id}/presigned-upload` · `contents/{id}/publish` · `contents/{id}/tracks` · `contents/{id}/subtitles` · `contents/{id}/audio-tracks`
**Merchandising:** `branding` · `banners` · `content-rows` · `config`
**Monetization:** `plans` · `promos` (see `/api/…`) · `revenue`
**Users & audit:** `users` · `audit-logs` · `exports/*.csv`
**Analytics:** `stats` · `analytics/top-content` · `analytics/regions` · `analytics/time-of-day` · `analytics/genres` · `analytics/devices` · `analytics/engagement/{id}` · `analytics/qoe`

### How a viewer plays (API sequence)
```bash
# 1) get stream URLs (must be entitled) → returns signed HLS + a streamSessionId
curl "$BASE/api/contents/$ID/stream" -H "Authorization: Bearer $TOKEN"
# 2) heartbeat the session every ~15s while playing
curl -X POST "$BASE/api/streams/heartbeat" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"streamSessionId":"<id>"}'
# 3) report progress (+ resume point)
curl -X POST "$BASE/api/watch-history/$ID/progress" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"watchedSeconds":120,"totalSeconds":5400}'
# 4) stop the session on exit
curl -X POST "$BASE/api/streams/stop" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"streamSessionId":"<id>"}'
```

---

# PART 5 — Operations quick reference

| Need | Where |
|---|---|
| API docs (interactive) | **Swagger** at `/swagger` |
| Background jobs (transcode/analytics) | **Hangfire** dashboard at `/hangfire` |
| Health check | `/health` |
| Deploy / local run | see **SETUP.md** + `bootstrap.ps1` |
| Environment secrets | `.env` (from `.env.example`) |

## Content states
`draft` (uploaded, not visible) → **publish** → `published` (live). Unpublish to pull a title.

## Who can watch what
Playback URLs are only issued when the viewer is **entitled** (right subscription/purchase) and within their
plan's **concurrent-stream limit**. Everything else returns a subscribe prompt (HTTP 402) or a device-limit
message (HTTP 409).
