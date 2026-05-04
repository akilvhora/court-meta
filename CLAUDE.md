# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build / Run

API targets **.NET 10** (`net10.0`, win-x64, self-contained single-file). The README still says ".NET 8 SDK" — that's stale; use .NET 10.

```bash
# Dev run (binds to http://localhost:5000)
cd CourtMetaAPI && dotnet run

# Build only
dotnet build CourtMetaAPI/CourtMetaAPI.csproj -c Release

# Publish single-file exe used by the installer
dotnet publish CourtMetaAPI/CourtMetaAPI.csproj -c Release -o CourtMetaAPI/publish

# Bundle extension into wwwroot (also runs implicitly on every Build)
dotnet msbuild CourtMetaAPI/CourtMetaAPI.csproj -t:BundleExtension
```

There is no test project, lint config, or formatter wired up — `dotnet build` is the only verification step.

## Architecture

Three components in one repo:

```
Web page  ──(window.postMessage)──▶  content.js  ──(chrome.runtime)──▶  background.js
                                                                              │ fetch + X-Court-Meta-Client header
                                                                              ▼
                                                                  CourtMetaAPI (localhost:5000)
                                                                              │ encrypted GET + Bearer(encrypted token)
                                                                              ▼
                                                              app.ecourts.gov.in/ecourt_mobile_DC/
```

### CourtMetaAPI (C# ASP.NET Core)

- **`Program.cs`** — installs CORS (permissive), a named `HttpClient` for eCourts (mobile UA + headers), static files for `wwwroot/`, and a middleware that **403s any `/api/*` request lacking `X-Court-Meta-Client: extension`** (OPTIONS is passed through). The header gate is the real auth — CORS just has to be permissive enough to let the preflight through. Bound to `localhost:5000` only. Hosts itself as a Windows Service via `UseWindowsService` when run by SCM.
- **`Services/EncryptionHelper.cs`** — AES-128-CBC port of the eCourts mobile app's `encryptData` / `decodeResponse`. Two different keys for request vs. response; outgoing IV = one of six `globaliv` strings + 16 random hex chars; the index of the chosen `globaliv` is embedded as a single digit between the IV and the ciphertext. Touch this only if the upstream mobile API changes.
- **`Services/TokenService.cs`** — singleton JWT cache. First call hits `appReleaseWebService.php` (no auth) to mint a token; every subsequent response may rotate it (`UpdateFromResponse` is called from `CallECourts`). Concurrency guarded by a `SemaphoreSlim`. The `uid` sent upstream is `Environment.MachineName:in.gov.ecourts.eCourtsServices` — this mirrors the mobile app's `device.uuid:packageName`.
- **`Controllers/CourtController.cs`** — every endpoint funnels through `CallECourts(endpoint, fields)` which (1) AES-encrypts the params, (2) attaches `Authorization: Bearer <encrypted-token>`, (3) AES-decrypts the response, (4) on `status:'N'` + `status_code:'401'` invalidates the token and retries **once**. Endpoints: `states`, `districts`, `complexes`, `courts`, `cnr`, `token-status` (debug). The CNR endpoint does a two-step lookup — `listOfCasesWebService.php` first; if `case_number` is absent it falls back to `filingCaseHistory.php`, otherwise `caseHistoryWebService.php`.

### Chrome Extension (Manifest V3)

- **`background.js`** is a module service worker. It's the only place that talks to the C# API. Adds the required `X-Court-Meta-Client: extension` header. After a successful response, if `MAPPING_CONFIGS[action]` is set, runs the result through the mapping engine and returns `{ parsed, raw }`; otherwise returns the raw payload.
- **`content.js`** is injected at `document_start` into every URL. It bridges `window.postMessage` (page) ↔ `chrome.runtime.sendMessage` (background) and handles the `COURT_META_PING` / `COURT_META_READY` handshake. It detects context invalidation (extension reloaded) by probing `chrome.runtime?.id` and tells the page to refresh.
- **`mapping/`** — declarative JSON-driven response refiner. `mapper.js` walks `config.fields`; each field's `rule` is dispatched through the registry in `rules/index.js` (`exact`, `regexKey`, `findArray`, `list`, `switch`, `literal`, `combine`, `coalesce`, `template`). Dotted field keys (e.g. `"filing.number"`) become nested objects via `setValueAtPath`. `transforms` and `default` are applied after the rule. `cnrMapping.json` is validated by `cnrMapping.schema.json` (JSON Schema draft-07). Engine supports config major version 1; bumping the major would break compat. To add a new mapped action: drop a `*.json` next to `cnrMapping.json` and register it in `MAPPING_CONFIGS` in `background.js`.

### `extension/` — single source of truth

There is **one** extension tree at the repo root: `extension/`. Both the dev load-unpacked workflow and the installer-packaged build read from it.

The csproj's `BundleExtension` target (`CourtMetaAPI/CourtMetaAPI.csproj`) sets `<ExtensionSourceDir>$(MSBuildProjectDirectory)\..\extension</ExtensionSourceDir>` and copies/zips from there into `CourtMetaAPI/wwwroot/extension/` + `wwwroot/court-meta-extension.zip` on every build.

A previous duplicate at `CourtMetaAPI/extension/` was deleted because it kept silently drifting. Do not reintroduce it — edit only `extension/`.

### Distribution pipeline

`Jenkinsfile` (root) drives Jenkins:
1. `BundleExtension` (msbuild target) → 2. `dotnet build` → 3. `dotnet publish` (self-contained single-file) → 4. `installer/pack-extension.ps1` packs `extension/` into `court-meta.crx` using the RSA key from Jenkins credential `court-meta-extension-key`, also writes `extension-id.txt` → 5. **Inno Setup** (`installer/court-meta-setup.iss`) builds the Windows installer, registering the API as the `Court Meta API` Windows service (`sc create … start= auto`) → 6. uploads `.exe` and `.crx` to Nexus at `${NEXUS_URL}/repository/court-meta-raw/court-meta/<version>/`. Version is `1.0.${BUILD_NUMBER}`.

The `jenkins/` directory contains PowerShell scripts (`create-job.ps1`, `update-job.ps1`, `verify-job.ps1`, `get-config.ps1`, `get-log.ps1`) for managing the Jenkins job remotely, plus `court-meta-ci.config.xml` (job definition). The `_*.txt` / `_*.xml` files in that directory are scratch output from those scripts and should not be committed (they're already in `.gitignore`-equivalent state but are showing as untracked).

### Sample website

`sample-website/` is the dev copy; `CourtMetaAPI/wwwroot/` is the served copy (also a build output target). `court-meta.js` is the public integration library a third-party site would include — it does the `COURT_META_PING` handshake and exposes `CourtMeta.ready()`, `fetchStates()`, `fetchDistricts()`, `fetchComplexes()`, `fetchCourts()`, `cnrSearch()`.
