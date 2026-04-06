# Court Meta

Chrome Extension + C# ASP.NET Core backend for accessing eCourts India case data.

## Architecture

```
Sample Website
    │  window.postMessage
    ▼
Chrome Extension (content.js + background.js)
    │  fetch (http://localhost:5000)
    ▼
C# ASP.NET Core Web API  (CourtMetaAPI)
    │  HTTP POST (form-encoded)
    ▼
eCourts India API  (https://app.ecourts.gov.in/ecourt_mobile_DC/)
```

## Setup

### 1. Start the C# Backend

Requires .NET 8 SDK.

```
cd CourtMetaAPI
dotnet run
```

The API will be available at `http://localhost:5000`.

**Endpoints:**
| Endpoint | Description |
|---|---|
| `GET /api/court/states` | Fetch all states |
| `GET /api/court/districts?state_code=` | Fetch districts for a state |
| `GET /api/court/complexes?state_code=&dist_code=` | Fetch court complexes |
| `GET /api/court/courts?state_code=&dist_code=&court_code=` | Fetch court names |
| `GET /api/court/cnr?cino=` | CNR case search |

### 2. Install Chrome Extension

1. Open Chrome → `chrome://extensions`
2. Enable **Developer mode** (top right toggle)
3. Click **Load unpacked**
4. Select the `extension/` folder
5. The Court Meta extension icon appears in the toolbar

### 3. Open Sample Website

Open `sample-website/index.html` in Chrome (with the extension installed and C# backend running).

The status bar will show **"Court Meta extension connected"** if everything is working.

## API Usage (from any website)

Include `court-meta.js` in your page:

```html
<script src="court-meta.js"></script>
```

Then use:

```js
// Wait for extension to be ready
CourtMeta.ready().then(() => {
  // Fetch states
  CourtMeta.fetchStates().then(data => console.log(data.states));

  // Fetch districts
  CourtMeta.fetchDistricts('24').then(data => console.log(data.districts));

  // Fetch court complexes
  CourtMeta.fetchComplexes('24', '1').then(data => console.log(data.complexes));

  // Fetch courts
  CourtMeta.fetchCourts('24', '1', '1010101').then(data => console.log(data.courts));

  // CNR Search
  CourtMeta.cnrSearch('MHPU010001232022').then(data => {
    console.log(data.type);       // "case" or "filing"
    console.log(data.caseNumber); // e.g. "WP/1234/2022"
    console.log(data.history);    // array of hearing history
  });
});
```

## Project Structure

```
Court Meta/
├── extension/          Chrome Extension (Manifest V3)
│   ├── manifest.json
│   ├── background.js   Service worker – calls C# API
│   ├── content.js      Injected into pages – bridges postMessage
│   ├── popup.html      Extension popup UI
│   └── popup.js
├── CourtMetaAPI/       C# ASP.NET Core Web API
│   ├── CourtMetaAPI.csproj
│   ├── Program.cs
│   └── Controllers/
│       └── CourtController.cs
└── sample-website/     Demo website
    ├── index.html
    ├── styles.css
    ├── court-meta.js   Integration library (copy to your site)
    └── app.js
```
