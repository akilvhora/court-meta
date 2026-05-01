# Court Meta

Chrome Extension + ASP.NET Core backend for accessing eCourts India case data
from any website. The backend mirrors the mobile app's encrypted transport so
the extension can call regular HTTPS endpoints from a third-party page.

## Architecture

```
Sample / 3rd-party Website
    │  window.postMessage
    ▼
Chrome Extension (content.js + background.js)
    │  fetch — sends X-Court-Meta-Client: extension
    ▼
CourtMetaAPI (ASP.NET Core, http://localhost:5000)
    │  encrypted GET + Bearer(encryptedJWT)
    ▼
eCourts mobile API  (https://app.ecourts.gov.in/ecourt_mobile_DC/)
```

The encryption helper (AES-128-CBC with the keys / IV scheme used by the
mobile app), the JWT lifecycle, and the `X-Court-Meta-Client` middleware that
gates `/api/*` are all documented in `CLAUDE.md` and `API_DOCUMENTATION.md`.

## Setup

### 1. Start the C# Backend

Requires the **.NET 10 SDK** (`win-x64`, single-file self-contained).

```
cd CourtMetaAPI
dotnet run
```

The API binds to `http://localhost:5000` only. When run by the Windows SCM
(`sc create "Court Meta API" …`) it auto-detects and hosts itself as a
service.

### 2. Install the Chrome Extension

1. Open Chrome → `chrome://extensions`
2. Enable **Developer mode** (top right)
3. Click **Load unpacked**
4. Select the `extension/` folder

### 3. Open the Sample Website

`http://localhost:5000/` serves a demo site that exercises every endpoint.
The status bar reports **Court Meta extension connected** when the handshake
completes.

## Endpoint reference

All endpoints live under `/api/court` and require the
`X-Court-Meta-Client: extension` header (the gating middleware 403s otherwise).

### Lookups (cached in `IMemoryCache`, TTLs: 24h / 12h / 6h / 1h)

| Endpoint | Purpose |
|---|---|
| `GET /states` | List of states |
| `GET /districts?state_code=` | Districts for a state |
| `GET /complexes?state_code=&dist_code=` | Court complexes |
| `GET /courts?state_code=&dist_code=&court_code=` | Court rooms in a complex |
| `GET /courts/latlong?state_code=&dist_code=&court_code=&complex_code=` | Lat/long for premise mapping |
| `GET /case-types?state_code=&dist_code=&court_code=` | Case-type dropdown |
| `GET /token-status` | Debug — confirms the JWT is acquired |

### CNR (Workflow A)

| Endpoint | Purpose |
|---|---|
| `GET /cnr?cino=` | Back-compat — returns the raw `history` blob |
| `GET /cnr/bundle?cino=` | Structured `CaseBundle` (caseInfo, parties, advocates, court, filing, hearings, transfers, processes, orders) |
| `GET /cnr/business?…&hearingDate=` | View Business detail for a hearing |
| `GET /cnr/writ?…&app_cs_no=` | Writ / application drill-down |
| `GET /cnr/order-pdf?…&order_id=` | Streams a pretrial / final order PDF |

### Searches

| Endpoint | Purpose |
|---|---|
| `GET /search/case-number?…` | Case-number search (Workflow E) |
| `GET /search/filing-number?…` | Filing-number search (Workflow F) |
| `GET /search/advocate?scope=court&…` | Advocate / bar-code search, single court (Workflow B) |
| `GET /search/advocate?scope=state&…` | Advocate search across an entire state — returns `application/x-ndjson`, one event per district |

### Cause list

| Endpoint | Purpose |
|---|---|
| `GET /cause-list?…&flag=civ_t\|cri_t&date=` | Cases caused at a court room on a given date (Workflow D) — returns the raw HTML fragment **and** parsed rows |

## Library usage (any website)

```html
<script src="court-meta.js"></script>
<script>
CourtMeta.ready().then(async () => {
  // Geographic lookups
  const { states } = await CourtMeta.fetchStates();

  // CNR — structured bundle
  const { data: bundle } = await CourtMeta.cnrBundle('MHPU010001232022');

  // Case / filing search
  const r = await CourtMeta.searchByCaseNumber({
    state_code: '1', dist_code: '19', courts: 'MHAU01,MHAU02',
    case_type: '1', number: '123', year: '2024'
  });

  // Cause list
  const cl = await CourtMeta.causeList({
    state_code: '1', dist_code: '19', court_code: 'MHAU01',
    court_no: '1', date: '30-04-2026', flag: 'civ_t'
  });

  // Advocate search — state-wide with progress
  await CourtMeta.searchByAdvocate({
    scope: 'state', state_code: '1',
    mode: 'name', advocateName: 'John Doe',
    pendingDisposed: 'Pending',
    onProgress(event) { console.log(event); }
  });
});
</script>
```

## Telemetry

Every `/api/*` request emits one NDJSON line into
`CourtMetaAPI/logs/court-meta-<yyyy-MM-dd>.log` (rolling per UTC day). Lines:

```json
{"type":"request","timestamp":"…","endpoint":"/api/court/cnr/bundle","method":"GET","statusCode":200,"latencyMs":312.7}
{"type":"token-refresh","timestamp":"…","endpoint":"caseHistoryWebService.php"}
```

Logs are gitignored. Producers never block — telemetry drops oldest events if
the writer falls behind.

## Project layout

```
Court Meta/
├── extension/          Chrome Extension (Manifest V3) — load-unpacked source of truth
│   ├── manifest.json
│   ├── background.js   Service worker — calls the local API
│   ├── content.js      Bridge between window.postMessage and chrome.runtime
│   ├── popup.html / popup.js
│   └── mapping/        Declarative response refiner (rules + JSON configs)
├── CourtMetaAPI/       ASP.NET Core (net10.0 / win-x64 single-file)
│   ├── Controllers/    CourtController, CnrController, SearchController, CauseListController
│   ├── Services/       EcourtsClient, TokenService, EncryptionHelper,
│   │                   HistoryParser, CauseListParser,
│   │                   AdvocateSearchService, TelemetryService
│   ├── Program.cs
│   └── wwwroot/        Built-in copy of the sample site + bundled extension zip
├── sample-website/     Source-of-truth dev copy of the demo page
├── installer/          Inno Setup + extension packing scripts
├── jenkins/            CI helper scripts (PowerShell) + job definition XML
└── Jenkinsfile         End-to-end pipeline (build → publish → CRX → installer → Nexus)
```

## Distribution

`Jenkinsfile` drives the full pipeline: `BundleExtension` (msbuild) →
`dotnet build` → `dotnet publish` → `pack-extension.ps1` (creates `.crx` from
the RSA key in Jenkins credential `court-meta-extension-key`) → Inno Setup
(`installer/court-meta-setup.iss`) registers the API as the **Court Meta API**
Windows service and ships the `.exe` and `.crx` to Nexus under
`${NEXUS_URL}/repository/court-meta-raw/court-meta/<version>/`. Version is
`1.0.${BUILD_NUMBER}`.
