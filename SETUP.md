# OTT Platform — Complete Setup Guide (from scratch on a new machine)

Everything needed to bring this project up on another desktop: system requirements, tools, SDKs,
external services, config, and run/verify steps. Two paths are offered — **Docker** (fastest, runs
every backend dependency in containers) and **Native** (install each tool directly, best for daily dev).

The stack: **.NET 8** backend (SQL Server + Redis, optional Elasticsearch) + **Flutter** app
(mobile / web / desktop). Ground truth: `docker-compose.yml`, `dotnet_api/*.csproj`, `flutter_app/pubspec.yaml`.

---

## 1. System requirements

| | Minimum | Recommended |
|---|---|---|
| OS | Windows 10 64-bit / macOS 12+ / Ubuntu 20.04+ | Windows 11 / macOS 14+ |
| CPU | 4 cores | 8+ cores (transcoding is CPU-heavy) |
| RAM | 8 GB | 16 GB+ (SQL Server + Redis + ES + Flutter tooling add up) |
| Disk | 30 GB free | 60 GB+ SSD (SDKs, emulators, Docker images, media) |
| GPU | — | Any (Android emulator / desktop player) |

> **iOS builds require macOS + Xcode — they cannot be built on Windows or Linux.**
> **webOS (LG TV) is not an official Flutter target** — ship the web build to its browser or use a native player.

---

## 2. Core tools (needed for BOTH paths)

| Tool | Version | Windows install | Verify |
|---|---|---|---|
| Git | any recent | `winget install Git.Git` | `git --version` |
| Flutter SDK | **stable 3.41.9** (Dart ≥ 3.6.2, per `pubspec.yaml`) | download from flutter.dev, add `flutter\bin` to PATH | `flutter --version` |
| .NET SDK | **8.0** (all projects target `net8.0`) | `winget install Microsoft.DotNet.SDK.8` | `dotnet --version` → 8.x |

After installing Flutter: `flutter doctor` and resolve the items for the platforms you plan to build.

---

## 3. Backend dependencies

### 3a. Databases & services

| Service | Version | Required? | Default local endpoint |
|---|---|---|---|
| SQL Server | 2022 | **Required** | `localhost:1433` (DB `ott_platform`) |
| Redis | 7 | **Required** (cache + write-behind buffers + stream slots) | `localhost:6379` |
| Elasticsearch | 8.11 | Optional (search; app runs without it) | `localhost:9200` |
| FFmpeg + ffprobe | any recent | Needed only for video upload/transcoding | on `PATH` |

### 3b. Path A — Docker (recommended for the services)

Install **Docker Desktop** (`winget install Docker.DockerDesktop`), then from `ott-platform/`:

```bash
# Just the databases (enough for backend dev):
docker compose up -d sqlserver redis

# Full stack incl. API + nginx:
docker compose up -d

# With optional search / separate worker:
docker compose --profile search --profile worker up -d
```

Containerized SQL uses SA auth: `Server=localhost,1433;User Id=sa;Password=Strong!Passw0rd;...`
(override via a `.env` file — see §6). FFmpeg is baked into the API image.

### 3c. Path B — Native installs (Windows)

```powershell
winget install Microsoft.SQLServer.2022.Developer   # or SQL Server Express
winget install Redis.Redis                           # or run Redis in Docker / WSL / Memurai
winget install Gyan.FFmpeg                            # FFmpeg + ffprobe on PATH
```

Native SQL with Windows auth matches the default `appsettings.json`:
`Server=localhost;Database=ott_platform;Trusted_Connection=True;TrustServerCertificate=True;...`

### 3d. EF Core tools + create the database schema

```bash
dotnet tool install --global dotnet-ef        # once per machine
cd dotnet_api
dotnet restore
dotnet ef database update -p OTT.Infrastructure -s OTT.API   # applies all migrations
```
(The API also auto-migrates on startup in dev, but running it explicitly is cleaner.)

### 3e. Run the backend

```bash
cd dotnet_api
dotnet run --project OTT.API
```
Local dev URLs: **http://localhost:50664** and **https://localhost:50663**
(see `OTT.API/Properties/launchSettings.json`). Extras: Swagger at `/swagger`, Hangfire at `/hangfire`, health at `/health`.

Optional separate transcoding worker: `dotnet run --project OTT.Worker`.

---

## 4. Flutter app setup

```bash
cd flutter_app
flutter pub get
```

Point the app at your local API (it defaults to the production URL otherwise):

```bash
# Web (works out of the box — Chrome/Edge):
flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:50664

# Release web build:
flutter build web --release
```

### 4a. Per-target toolchains

| Target | Extra requirement | Build/run |
|---|---|---|
| **Web** | Chrome or Edge (bundled with Flutter) | `flutter run -d chrome` |
| **Android / tablet** | **Android Studio** + Android SDK (API 34) + a device/emulator; accept licenses (`flutter doctor --android-licenses`). App min SDK 21+. | `flutter run -d <device>` / `flutter build apk` |
| **iOS / iPad** | **macOS only** — Xcode + CocoaPods (`sudo gem install cocoapods`) | `flutter build ios` (on a Mac) |
| **Windows desktop** | Visual Studio 2022 with **"Desktop development with C++"** (MSVC v142+, Windows 10 SDK, C++ CMake tools) | `flutter run -d windows` |
| **Linux desktop** | `clang cmake ninja-build libgtk-3-dev` + `libmpv` (media_kit) | `flutter run -d linux` |
| **macOS desktop** | Xcode | `flutter run -d macos` |

> **Player package platform notes:** `media_kit` (HLS) → mobile + desktop; `youtube_player_flutter` → **Android/iOS only**; `flutter_inappwebview` (Vimeo) → mobile/desktop. On **web**, HLS uses the separate `hls_web.dart` path (`web` + `video_player`). Confirm actual video playback per platform — the analytics/QoE telemetry only fires where playback works.

### 4b. Code generation (if you edit models/freezed/json)

```bash
dart run build_runner build --delete-conflicting-outputs
```

---

## 5. Third-party accounts & keys (for full functionality)

The app **boots and runs locally with the placeholder values** in `appsettings.json`; these features
simply stay inert until real keys are supplied. Configure only what you need.

| Feature | Provider | Keys / files |
|---|---|---|
| Media storage + CDN | **AWS S3 + CloudFront** | Access/Secret key, bucket, CloudFront domain + KeyPairId + private key `.pem` in `dotnet_api/keys/` |
| Payments | **Razorpay** and/or **Stripe** | Key id/secret; Stripe webhook secret |
| Transactional email | **SendGrid** | API key + from-email |
| Push / analytics / crash | **Firebase** | Backend: `firebase-credentials.json`; Android: `google-services.json`; iOS: `GoogleService-Info.plist` |
| Social login | **Google / Facebook / Apple** | Client ids/secrets; Apple IAP shared secret |
| Live streaming | **Ant Media Server** | Server URL + RTMP host (optional) |

---

## 6. Configuration

**Backend** — override `appsettings.json` via `appsettings.Development.json`, environment variables
(`ConnectionStrings__DefaultConnection`, `Jwt__Secret`, …), or a `.env` for Docker Compose. Minimum to
change for a real deploy: `Jwt:Secret` (≥64 chars), DB/Redis connection strings, and any provider keys you use.

**Docker `.env`** (place next to `docker-compose.yml`) — recognized keys include:
`MSSQL_SA_PASSWORD`, `REDIS_PASSWORD`, `JWT_SECRET`, `AWS_*`, `S3_BUCKET`, `CLOUDFRONT_*`,
`RAZORPAY_*`, `SENDGRID_*`, `APP_BASE_URL`, `DEFAULT_TENANT_SLUG`, `WORKER_COUNT`.

**Flutter** — `--dart-define=API_BASE_URL=...` at run/build time (defaults to prod; localhost is
auto-detected only for web).

---

## 7. Ports reference

| Port | Service |
|---|---|
| 50664 / 50663 | Backend API (local dev http/https) |
| 8080 | Backend API (Docker) |
| 1433 | SQL Server |
| 6379 | Redis |
| 9200 / 5601 | Elasticsearch / Kibana (optional) |
| 80 / 443 | nginx reverse proxy (Docker full stack) |

---

## 8. Verify the install

```bash
# Backend
cd dotnet_api
dotnet build                                   # expect 0 errors
dotnet test OTT.Tests/OTT.Tests.csproj         # unit tests (in-memory)
curl http://localhost:50664/health             # after `dotnet run`

# Flutter
cd flutter_app
flutter analyze                                # expect 0 errors
flutter test                                   # widget/unit tests
flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:50664
```

Integration tests (`OTT.IntegrationTests`) need **Docker** (Testcontainers spin up SQL Server + Redis),
except the analytics SQL-translation smoke tests which can run against a reachable local SQL Server.

---

## 9. Quick start (TL;DR)

```bash
# 1. Install: Git, Flutter 3.41.9, .NET 8 SDK, Docker Desktop
# 2. Services
cd ott-platform && docker compose up -d sqlserver redis
# 3. Backend
cd dotnet_api && dotnet tool install -g dotnet-ef && dotnet restore \
  && dotnet ef database update -p OTT.Infrastructure -s OTT.API \
  && dotnet run --project OTT.API
# 4. App (new terminal)
cd flutter_app && flutter pub get \
  && flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:50664
```
