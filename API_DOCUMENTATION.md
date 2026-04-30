# eCourts Mobile (`ecourt_mobile_DC`) — API Reference

Reverse-engineered from the decompiled APK at `E:\work\apk` (Cordova/PhoneGap web bundle, package `in.gov.ecourts.eCourtsServices`).
This document describes every HTTP endpoint the client calls, the encrypted transport envelope, the cleartext request payload, and the cleartext response shape.

> All evidence comes from `assets/www/js/*.js`. Where a parameter or field name appears verbatim in the source, that source path/line is cited.

---

## 1. Base URL

| Environment | Base URL |
|---|---|
| **Production (default)** | `https://app.ecourts.gov.in/ecourt_mobile_DC/` |
| Production (alt, encrypted variant) | `https://app.ecourts.gov.in/ecourt_mobile_encrypted_DC/` |
| Internal / staging hosts (commented in source) | `http://10.249.33.50/ecourt_mobile_DC/`, `http://10.153.16.219/ecourt_mobile_encrypted/...`, etc. |

Source: `assets/www/js/main.js:1-7`, `assets/www/js/main_hc.js`.

Every endpoint described below is reached as **`<base>/<endpoint.php>`**.

---

## 2. Transport, encryption and authentication

All endpoints (with one exception, see §2.4) are called the same way — through the helper `callToWebService(url, data, callback)` in `assets/www/js/main.js:1348`.

### 2.1 HTTP envelope

| Item | Value |
|---|---|
| Method | `GET` |
| Library | `cordova.plugin.http` (`cordova-plugin-advanced-http`) |
| Timeout | 180 s (`cordova.plugin.http.setRequestTimeout(180)`) |
| Query string | A single param `params=<encrypted-blob>` (see §2.2) |
| Authorization header | `Authorization: Bearer <encrypted-jwt>` (see §2.3) |
| Response body | Single AES-CBC encrypted base64 blob (see §2.2) |

In the source the call looks like:
```js
cordova.plugin.http.get(url, { params: data1 }, header, success, error);
```
where `data1` is the encrypted form of the cleartext JSON request, and `header` is either `{}` (for `appReleaseWebService.php`) or `{ Authorization: 'Bearer ' + encryptData(jwttoken) }`.

### 2.2 Request encryption (`encryptData`) — `main.js:1139`

```
key       = 4D6251655468576D5A7134743677397A          (HEX, 16 bytes / AES-128)
globaliv  = one of 6 fixed 16-hex pools, picked at random per request
            ["556A586E32723575","34743777217A2543","413F4428472B4B62",
             "48404D635166546A","614E645267556B58","655368566D597133"]
globalIndex = the index (0..5) of the chosen pool entry
randomiv  = 16 random hex chars generated per request
iv        = HEX(globaliv || randomiv)             (32 hex chars = 16 bytes)
plaintext = JSON.stringify(requestBody)
cipher    = AES-CBC(key, iv).encrypt(plaintext)
encoded   = randomiv + globalIndex + Base64(cipher)   <-- this is the value sent as `params`
```

So a request looks like:
```
GET <base>/<endpoint>.php?params=<16hex><1digit><base64> HTTP/1.1
Authorization: Bearer <encrypted JWT>
```

### 2.3 Response decryption (`decodeResponse`) — `main.js:1155`

```
key       = 3273357638782F413F4428472B4B6250          (HEX, 16 bytes / AES-128)
iv        = HEX( response.substring(0, 32) )
cipher    = response.substring(32)                   (Base64)
plaintext = AES-CBC(key, iv).decrypt(cipher)         (UTF-8 JSON)
```

After decryption the client further sanitises non-printable chars before `JSON.parse`.

### 2.4 JWT lifecycle

1. The very first call after app start is `appReleaseWebService.php`. For this call **only** the `Authorization` header is omitted (`main.js:1353-1357`).
2. Its response includes `{"token":"<JWT>"}` which is decoded with `jwt_decode` and stored in `jwttoken` (`index.js:74-75`).
3. Every subsequent request sends `Authorization: Bearer <encryptData(jwttoken)>`.
4. If the server returns `{"status":"N","status_code":"401"}`, the client appends `{"uid":"<device.uuid>:<packageName>"}` (e.g. `uid="abcd1234:in.gov.ecourts.eCourtsServices"`) to the original payload and retries once. A second 401 ends the session with “Session expired !” (`main.js:1367-1378`).
5. Each successful response can carry a refreshed `token` field that replaces the in-memory JWT.

### 2.5 Common envelope fields

Every response object may contain:

| Field | Meaning |
|---|---|
| `status` | `"N"` = error, otherwise normal |
| `status_code` | e.g. `"401"` triggers re-auth |
| `msg` | error message string shown via `showErrorMessage(...)` |
| `token` | rotated JWT (replaces the one in memory) |

---

## 3. Common request parameters

These appear repeatedly across endpoints. They are referenced below by name.

| Field | Type | Source / Description |
|---|---|---|
| `state_code` | string (numeric) | `localStorage.state_code` — selected state ID |
| `dist_code` | string (numeric) | `localStorage.district_code` — selected district ID |
| `court_code` | string | NJDG establishment code (single court) |
| `court_code_arr` | string | comma-joined list of NJDG est codes (`"3,4,5"`) |
| `complex_code` | string | court complex code |
| `cino` / `cinum` | string | 16-char CNR Number, e.g. `MHAU010001232019` |
| `case_no` | string | internal case number |
| `language_flag` | string | `"english"`, `"hindi"`, `"marathi"`, etc. (`localStorage.LANGUAGE_FLAG`) |
| `bilingual_flag` | string | `"0"` (English only) or `"1"` (selected language has bilingual rendering) |
| `version_number` | string | client app version, e.g. `"3.2"` |

---

## 4. Endpoint catalogue

The 36 endpoints below are grouped by feature. Every entry lists:
- **Method** (always `GET`)
- **Cleartext request body** that is fed to `encryptData(...)`
- **Cleartext response body** after `decodeResponse(...)`
- **Source** locations
- **Notes / preconditions**

> **Convention.** Field names below match the JS source exactly. All values are sent as **strings** even when the value is numeric (the source explicitly calls `.toString()` on every value before encryption).

---

### 4.1 Bootstrap & localisation

#### 4.1.1 `appReleaseWebService.php` — App version / JWT bootstrap

- **Source:** `index.js:59`, `index_hc.js:33`, `main.js:1353` (no `Authorization` header on this call).
- **Request:**
  ```json
  {
    "version": "3.2",
    "uid": "<device.uuid>:in.gov.ecourts.eCourtsServices"
  }
  ```
- **Response:**
  ```json
  {
    "token": "<JWT>",
    "version_compatible": "<optional warning string>",
    "appReleaseObj": {
      "version_no": "3.3",
      "release_url": "https://play.google.com/store/apps/details?id=..."
    }
  }
  ```
- **Notes:** Must be the first call; populates `jwttoken` for all later calls. If `appReleaseObj.version_no != current`, the UI shows an upgrade banner.

#### 4.1.2 `getAllLabelsWebService.php` — Multilingual UI labels

- **Source:** `main.js:1055`.
- **Request:**
  ```json
  { "language_flag": "english", "bilingual_flag": "0" }
  ```
- **Response:**
  ```json
  {
    "allLabels": ["...", "Total Number of Cases", "Party Name", "..."],
    "languages_available": [
      {"language": "English", "language_flag": "english"},
      {"language": "हिन्दी", "language_flag": "hindi"}
    ]
  }
  ```
- **Notes:** `allLabels` is an array indexed by `labelsarr[i]` calls all over the JS code. Cached in `sessionStorage.GLOBAL_LABELS`.

---

### 4.2 Geographic / court hierarchy

#### 4.2.1 `stateWebService.php` — List of states (DC) / High Courts (HC)

- **Source:** `index.js:225`, `index_hc.js:199`, `map.js:135`.
- **Request:**
  ```json
  { "action_code": "fillState", "time": "1714471234.567" }
  ```
  `time` is `Date.now()/1000` and is unused server-side per the comment in `index.js:222`.
- **Response (DC):**
  ```json
  {
    "states": [
      {
        "state_code": "1",
        "state_name": "Maharashtra",
        "state_lang": "marathi",
        "state_name_marathi": "महाराष्ट्र",
        "state_name_hindi": "..."
      }
    ]
  }
  ```
- **Response (HC, `index_hc.js`):** each entry additionally carries a `webservice` URL pointing at the HC-specific backend.

#### 4.2.2 `districtWebService.php` — Districts / benches

- **Source:** `index.js:574`, `index_hc.js:485`, `map.js:159`, also reused by caveat search.
- **Request (DC):**
  ```json
  { "state_code": "1", "test_param": "pending" }
  ```
- **Request (HC):** adds `"action_code": "benches"`.
- **Response:**
  ```json
  {
    "districts": [
      {"dist_code": "19", "dist_name": "Aurangabad", "mardist_name": "औरंगाबाद"}
    ]
  }
  ```

#### 4.2.3 `courtEstWebService.php` — Court complexes (establishments)

- **Source:** `main.js:87`, `map.js:326`.
- **Request:**
  ```json
  { "action_code": "fillCourtComplex", "state_code": "1", "dist_code": "19" }
  ```
- **Response:**
  ```json
  {
    "courtComplex": [
      {
        "njdg_est_code": "MHAU01",
        "complex_code": "1010001@1@Y",
        "court_complex_name": "District & Sessions Court, Aurangabad",
        "lcourt_complex_name": "औरंगाबाद..."
      }
    ]
  }
  ```
- **Notes:** `njdg_est_code` becomes the `court_code` used for nearly every subsequent search. Multiple complexes can be selected → joined as `"X,Y,Z"` in `court_code_arr`.

#### 4.2.4 `courtNameWebService.php` — Court rooms / judges (cause-list)

- **Source:** `cause_list.js:135`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19",
    "court_code": "MHAU01,MHAU02",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  { "courtNames": "1^Court of CJM~Disp\nD^Disabled court (heading)~..." }
  ```
- **Notes:** Returned as a single string, `#`-separated rows, `~`-separated columns. First field `D` marks a disabled (header) row.

#### 4.2.5 `policeStationWebService.php` — Police stations

- **Source:** `search_by_fir_number.js:404`, `search_by_application.js:704`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code": "MHAU01",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  {
    "policeStations": [
      {"ps_code": "001", "ps_name": "Aurangabad City Chowk", "uniform_code": "MHA-..."}
    ]
  }
  ```
  (The client uses `uniform_code` as `uniform_code` in subsequent FIR search.)

#### 4.2.6 `actWebService.php` — Acts directory

- **Source:** `search_by_act.js:154`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code": "MHAU01",
    "searchText": "narcotic", "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  {
    "actsList": [
      { "acts": "act_id~Act Name#act_id2~Another Act#..." }
    ]
  }
  ```
- **Notes:** Items are `#`-separated rows, each row `act_id~act_name`.

#### 4.2.7 `caseNumberWebService.php` — Case-type list (DC)

- **Source:** `search_by_case_number.js:196`, `search_by_case_type.js:180`, `search_by_case_number_hc.js:139`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code": "MHAU01",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  {
    "case_types": [
      { "case_type": "1~Civil Suit#2~Criminal Misc#3~..." }
    ]
  }
  ```
  Same `#`/`~` separator scheme as acts.

#### 4.2.8 `caseTypeCaveat_hc.php` — Case-type list (HC caveat behavior)

- **Source:** `search_by_caveat_hc.js:1130`.
- **Request:**
  ```json
  { "state_code": "1", "dist_code": "19", "court_code": "MHAU01" }
  ```
- **Response:**
  ```json
  { "CourtCodeHC": [ {"case_code": "AC", "type_name": "Appeal Civil"} ] }
  ```

#### 4.2.9 `causeListBenchWebService.php` — Benches for HC cause list

- **Source:** `cause_list_hc.js:84`.
- **Request:**
  ```json
  {
    "state_code": "27", "dist_code": "1",
    "court_code": "DLHC01,DLHC02",
    "date": "30-04-2026"
  }
  ```
- **Response:**
  ```json
  { "benches": { "benchesStr": "1~Bench of Hon. ...^2~Division Bench^..." } }
  ```
  Items `^`-separated, each `bench_id~bench_name`.

---

### 4.3 CNR & case detail

#### 4.3.1 `listOfCasesWebService.php` — Resolve a CNR

- **Source:** `index.js:499`.
- **Request:**
  ```json
  {
    "cino": "MHAU010001232019",
    "version_number": "3.2",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  { "case_number": "12345" }
  ```
- **Behaviour:** if `case_number` is `null`, the CNR is a *filing* case → caller switches to `filingCaseHistory.php`; otherwise → `caseHistoryWebService.php`.

#### 4.3.2 `caseHistoryWebService.php` — Full case history (registered case)

- **Source:** `main.js:160`, `index.js:540`, `index_hc.js:456`, `my_cases1.js:248`, `my_cases_hc.js:254`, `main_hc.js:70`.
- **Request — by CNR:**
  ```json
  { "cinum": "MHAU010001232019",
    "language_flag": "english", "bilingual_flag": "0" }
  ```
- **Request — from a search-result row:**
  ```json
  {
    "state_code": "1", "dist_code": "19",
    "case_no": "98765", "court_code": "MHAU01",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  { "history": "<html or json blob to render in case_history.html>" }
  ```
  The client stores `data.history` directly into `sessionStorage.case_history` and renders it inside `case_history.html` / `case_history_hc.html`. View Business and Writ Info detail buttons inside this HTML call `s_show_business.php` and `s_show_app.php` (see below).

#### 4.3.3 `filingCaseHistory.php` — Case history for *filing* cases

- **Source:** `main.js:234`, `index.js:512`, `my_cases1.js:321`.
- **Request — by CNR:**
  ```json
  { "cino": "MHAU010F00012019",
    "language_flag": "english", "bilingual_flag": "0" }
  ```
- **Request — from search row:**
  ```json
  {
    "state_code": "1", "dist_code": "19",
    "case_no": "<filing_no>", "court_code": "MHAU01",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** `{ "history": "<rendered HTML>" }` — shown in `filing_case_history.html`.

#### 4.3.4 `s_show_business.php` — “View Business” for a hearing

- **Source:** `case_history.js:760`, `case_history_hc.js:728`, `filing_case_history.js:505`.
- **Request:**
  ```json
  {
    "court_code": "MHAU01", "dist_code": "19", "state_code": "1",
    "nextdate1": "30-04-2026",
    "case_number1": "12345",
    "disposal_flag": "P",                    // P = pending, D = disposed
    "businessDate": "30-04-2026",
    "court_no": "1",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  { "viewBusiness": "<rendered HTML for view_business.html>" }
  ```

#### 4.3.5 `s_show_app.php` — Writ info / application detail

- **Source:** `case_history.js:801`, `case_history_hc.js:757`.
- **Request:**
  ```json
  {
    "court_code": "MHAU01", "dist_code": "19", "state_code": "1",
    "case_number1": "12345", "app_cs_no": "5",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** `{ "writInfo": "<HTML for writ_information.html>" }`.

#### 4.3.6 `todaysCasesWebService.php` — Bulk refresh saved cases (My Cases tab)

- **Source:** `my_cases1.js:452`, `my_cases_hc.js:383`.
- **Request:**
  ```json
  {
    "cnr_numbers": "CNR1,CNR2,CNR3,...",
    "action_code": "list",
    "version_number": "3.2",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  {
    "todaysCiNos": [
      {
        "cino": "MHAU010001232019",
        "type_name": "C.S.", "ltype_name": "द.कि.",
        "case_no": "123", "reg_year": "2019", "reg_no": "...",
        "date_next_list": "10-05-2026",
        "date_last_list": "20-04-2026",
        "date_of_decision": "",
        "desgname": "CJM", "ldesgname": "...",
        "pet_name": "...", "lpet_name": "...",
        "res_name": "...", "lres_name": "...",
        "lcourt_name": "...", "lstate_name": "...", "ldistrict_name": "...",
        "purpose_name": "Arguments", "lpurpose_name": "...",
        "disp_name": "", "ldisp_name": ""
      }
    ],
    "deletedCiNos": [ {"cino": "..." } ]
  }
  ```

---

### 4.4 Case search (district court)

All search endpoints are invoked through `displayCasesTable(url, request_data)` (`main.js:292`), which prepends the common context fields:
```json
{
  "state_code": "1",
  "dist_code": "19",
  "court_code_arr": "MHAU01,MHAU02",
  "language_flag": "english",
  "bilingual_flag": "0"
}
```
to the per-search `request_data` listed below.

#### 4.4.1 `caseNumberSearch.php` — Search by case number

- **Source:** `search_by_case_number.js:174`, `search_by_case_number_hc.js:124`.
- **Per-search payload:**
  ```json
  { "case_number": "123", "case_type": "1", "year": "2020" }
  ```
- **Response (per-establishment, keyed by index):**
  ```json
  {
    "0": {
      "court_code": "MHAU01",
      "establishment_name": "CJM Court",
      "caseNos": [
        {
          "cino": "MHAU010001232019",
          "case_no": "123", "case_no2": "...", "reg_year": "2019",
          "type_name": "C.S.", "ltype_name": "...",
          "petnameadArr": "X vs Y",
          "filing_no": null
        }
      ]
    },
    "1": { ... }
  }
  ```

#### 4.4.2 `searchByFilingNumberWebService.php` — Search by filing number

- **Source:** `search_by_filing_number.js:154`, `search_by_filing_number_hc.js:107`.
- **Per-search payload:**
  ```json
  { "filingNumber": "456", "year": "2020" }
  ```
- **Response:** same per-establishment shape as §4.4.1.

#### 4.4.3 `searchByCaseType.php` — Search by case type

- **Source:** `search_by_case_type.js:160`, `search_by_case_type_hc.js:168`.
- **Per-search payload:**
  ```json
  { "case_type": "1", "year": "2020", "pendingDisposed": "Pending" }
  ```
  `pendingDisposed` is `"Pending"`, `"Disposed"`, or `"Both"` (radio).
- **Response:** per-establishment shape as §4.4.1.

#### 4.4.4 `searchByActWebService.php` — Search by act

- **Source:** `search_by_act.js:242`, `search_by_act_hc.js:184`.
- **Per-search payload:**
  ```json
  {
    "selectActTypeText": "<actId>",
    "underSectionText": "302",
    "pendingDisposed": "Pending",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** per-establishment shape as §4.4.1.

#### 4.4.5 `searchByAdvocateName.php` — Search by advocate name / bar code

- **Source:** `search_by_advocate_name.js:528`, `search_by_advocate_name_hc.js:388`.
- **Per-search payload:**
  ```json
  {
    "checkedSearchByRadioValue": "1",   // 1 = name, 2 = bar code
    "advocateName": "John Doe",
    "year": "",
    "barstatecode": "",
    "barcode": "",
    "pendingDisposed": "Pending",
    "date": "",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** per-establishment shape:
  ```json
  {
    "0": {
      "court_code": "MHAU01", "establishment_name": "...",
      "advocateName": "JOHN DOE",
      "caseNos": { "0": { "cino":"...", "type_name":"...", "case_no2":"...", "reg_year":"...", "petnameadArr":"..." } }
    }
  }
  ```

#### 4.4.6 `causeListWebService.php` — Advocate's cause list

- **Source:** `search_by_advocate_name.js:526`, `search_by_advocate_name_hc.js:386`.
- **Per-search payload (radio value 3):**
  ```json
  {
    "checkedSearchByRadioValue": "3",
    "advocateName": "John Doe", "year": "",
    "barstatecode": "1", "barcode": "MAH/12345/2010",
    "pendingDisposed": "",
    "date": "30-04-2026",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** same shape as §4.4.5 plus the same `advocateName` echoed.

#### 4.4.7 `firNumberSearch.php` — Search by FIR number

- **Source:** `search_by_fir_number.js:227`, `search_by_fir_number_hc.js:265`.
- **Per-search payload:**
  ```json
  {
    "police_stationcode": "001",
    "firNumber": "123",
    "year": "2024",
    "pendingDisposed": "Pending",
    "uniform_code": "MHA-..."
  }
  ```
- **Response:** per-establishment shape; each case object additionally carries `fir_no`, `fir_year`.

#### 4.4.8 `pretrialNumberSearch.php` — Search by application / bail / remand

- **Source:** `search_by_application.js:280`.
- **Per-search payload:**
  ```json
  {
    "police_stationcode": "001",
    "firNumber": "123",
    "year": "2024",
    "pre_application_type": "B",      // B = Bail, R = Remand, A = Generic application
    "fir_type": "1",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response (per establishment):**
  ```json
  {
    "0": {
      "court_code": "MHAU01", "establishment_name": "...",
      "pre_application_type": "B",
      "total_count": 7,
      "cnt_order": 2,
      "Pretrial": [
        {
          "srno": "1",
          "accused_name": "...",
          "appl_date": "01-04-2026",
          "next_date": "10-05-2026",
          "status": "Pending", "result": "",
          "appl_type": "Bail",
          "type1": "JC", "days": "30",
          "from_date": "...", "to_date": "..."
        }
      ],
      "pre_order_arr": [
        { "court_no":"1", "judge_name":"...", "order_date":"...",
          "orderYr":"2024", "order_id":"5", "crno":"MHAU01P0000032019" }
      ]
    }
  }
  ```

#### 4.4.9 `preTrialOrder_pdf.php` — Pretrial order PDF

- **Source:** `search_by_application.js:528`.
- **URL:** `<base>/preTrialOrder_pdf.php?params=<encrypted-blob>`
- **Plaintext payload (encrypted into `params`):**
  ```json
  {
    "court_code": "MHAU01", "state_code": "1", "dist_code": "19",
    "orderYr": "2024", "order_id": "5", "crno": "MHAU01P0000032019"
  }
  ```
- **Response:** A PDF stream (rendered in an in-app browser, not via `callToWebService`).

#### 4.4.10 `showDataWebService.php` — Search by party name

- **Source:** `search_by_party_name.js:211`, `search_by_party_name_hc.js:155`.
- **Per-search payload:**
  ```json
  { "pet_name": "Ramesh", "pendingDisposed": "Pending", "year": "2020" }
  ```
- **Response:** per-establishment shape as §4.4.1.

---

### 4.5 Caveat search

#### 4.5.1 `searchCaveat.php` — Caveat search (4 modes)

- **Source:** `search_by_caveat.js:485`, `search_by_caveat_hc.js:647`.
- The cleartext payload differs by `action_code`:

  | `action_code` | Mode | Mandatory fields |
  |---|---|---|
  | `"1"` | Anywhere | `caveator_name`, `caveatee_name` |
  | `"2"` | Starting with | `caveator_name` *or* `caveatee_name`, `starting_wit_RadioVal` |
  | `"4"` | Subordinate court | `subordinate_court_name`, `filing_type`, `case_type`, `case_number`, `case_year`, optional `date_of_decision` |
  | `"5"` | Caveat number | `caveat_number`, `caveat_year` |

- **Common context:** `state_code`, `dist_code`, `court_code_arr` (DC) or `court_code` (HC), plus `language_flag`/`bilingual_flag` on DC, plus HC-only `spl_behav_case_type`, `district`, `court_type`.

- **Sample request (Anywhere, DC):**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code_arr": "MHAU01",
    "caveator_name": "John", "caveatee_name": "Doe",
    "action_code": "1",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```

- **Response (per establishment):**
  ```json
  {
    "0": {
      "court_name": { "court_name": "Aurangabad CJM" },
      "totalCases": 3,
      "caveatSearchTable": "<HTML table fragment to inject>"
    }
  }
  ```
  The HTML uses `caveatHistory(caveat_number, court_code)` callbacks to drill down into a case (see §4.5.2).

#### 4.5.2 `caveatCaseHistoryWebService.php` — Caveat detail

- **Source:** `search_by_caveat.js:1001`, `search_by_caveat_hc.js:1101`.
- **Request:**
  ```json
  {
    "court_code": "MHAU01", "dist_code": "19", "state_code": "1",
    "caveat_number": "5/2024",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** `{ "caveathistory": "<rendered HTML>" }`.

#### 4.5.3 `stateWebServiceCaveat.php` — States + case-type combo for caveat

- **Source:** `search_by_caveat.js:839`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code": "MHAU01",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  {
    "states":   [ {"state_id":"1","state_name":"Maharashtra"} ],
    "lcaseType":[ {"lcase_type":"CS","type_name":"Civil Suit"} ]
  }
  ```

#### 4.5.4 `districtWebServiceCaveat.php` — Districts of a "lower" state for caveat

- **Source:** `search_by_caveat.js:902`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code": "MHAU01",
    "establishment_state_code": "27",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:** `{ "districts": [ {"dist_code":"1","dist_name":"South-East"} ] }`.

#### 4.5.5 `lowerCourtCaveat.php` — Subordinate court list

- **Source:** `search_by_caveat.js:957`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19", "court_code": "MHAU01",
    "establishment_state_code": "27", "establishment_district_code": "1",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Response:**
  ```json
  { "lowerCourt": [ {"lower_court_code":"DLSE01","oname":"Patiala House"} ] }
  ```

#### 4.5.6 `caveatCount.php` — Caveat count for a complex (HC behaviour gate)

- **Source:** `search_by_caveat_hc.js:1152`.
- **Request:** `{ "state_code":"1", "dist_code":"19", "court_code":"MHAU01" }`
- **Response:** `{ "caveatCount": 12 }` — when `> 0` the HC UI shows the “special behaviour case-type” dropdown.

---

### 4.6 Cause list

#### 4.6.1 `cases_new.php` — Cause-list cases for a date

- **Source:** `cause_list.js:348` (DC), `cause_list_hc.js:267` (HC).
- **Request — DC:**
  ```json
  {
    "state_code": "1", "dist_code": "19",
    "flag": "civ_t",                 // civ_t = civil, cri_t = criminal
    "selprevdays": "0",              // 1 = looking at past day
    "court_no": "1", "court_code": "MHAU01",
    "causelist_date": "30-04-2026",
    "language_flag": "english", "bilingual_flag": "0"
  }
  ```
- **Request — HC:**
  ```json
  {
    "state_code": "27", "dist_code": "1",
    "selprevdays": "0",
    "court_code": "DLHC01,DLHC02",
    "causelist_date": "30-04-2026",
    "bench_id": "5"
  }
  ```
- **Response:** `{ "cases": "<HTML table fragment>" }`.

---

### 4.7 Map / location

#### 4.7.1 `latlong.php` — Lat/long for a court complex

- **Source:** `map.js:472`.
- **Request:**
  ```json
  {
    "state_code": "1", "dist_code": "19",
    "court_code": "MHAU01", "complex_code": "1010001@1@Y"
  }
  ```
- **Response:**
  ```json
  {
    "latitude": "19.901054",
    "longitude": "75.21",
    "map_url": "https://bhuvan-web.nrsc.gov.in/web_view/index.php?",
    "court_complex": "DISTRICT & SESSIONS COURT, AURANGABAD"
  }
  ```
- **Notes:** The client opens `map_url + "x=<lon>&y=<lat>&buff=0"` in an in-app browser.

---

## 5. End-to-end example (cleartext)

The following is what the *plaintext* contents of a request/response pair look like for a typical CNR lookup — actually on the wire, both bodies are AES-CBC encrypted as described in §2.

### 5.1 Resolve CNR

```http
GET /ecourt_mobile_DC/listOfCasesWebService.php
    ?params=<encrypted blob>
Authorization: Bearer <encrypted JWT>
```

Plaintext request body fed to `encryptData`:
```json
{
  "cino": "MHAU010001232019",
  "version_number": "3.2",
  "language_flag": "english",
  "bilingual_flag": "0"
}
```

Plaintext response body after `decodeResponse`:
```json
{ "case_number": "12345", "token": "<rotated JWT>" }
```

### 5.2 Fetch case history (registered)

```http
GET /ecourt_mobile_DC/caseHistoryWebService.php
    ?params=<encrypted blob>
Authorization: Bearer <encrypted JWT>
```

Plaintext request:
```json
{
  "cinum": "MHAU010001232019",
  "language_flag": "english",
  "bilingual_flag": "0"
}
```

Plaintext response:
```json
{ "history": "<...HTML to render in case_history.html...>", "token": "..." }
```

---

## 6. Quick reference — endpoint × payload

| # | Endpoint | Plaintext body keys |
|---|---|---|
| 1 | `appReleaseWebService.php` | `version`, `uid` |
| 2 | `getAllLabelsWebService.php` | `language_flag`, `bilingual_flag` |
| 3 | `stateWebService.php` | `action_code`, `time` |
| 4 | `districtWebService.php` | `state_code`, `test_param` (+ `action_code` for HC) |
| 5 | `courtEstWebService.php` | `action_code`, `state_code`, `dist_code` |
| 6 | `courtNameWebService.php` | `state_code`, `dist_code`, `court_code`, `language_flag`, `bilingual_flag` |
| 7 | `policeStationWebService.php` | `state_code`, `dist_code`, `court_code`, `language_flag`, `bilingual_flag` |
| 8 | `actWebService.php` | `state_code`, `dist_code`, `court_code`, `searchText`, `language_flag`, `bilingual_flag` |
| 9 | `caseNumberWebService.php` | `state_code`, `dist_code`, `court_code`, `language_flag`, `bilingual_flag` |
| 10 | `caseTypeCaveat_hc.php` | `state_code`, `dist_code`, `court_code` |
| 11 | `causeListBenchWebService.php` | `state_code`, `dist_code`, `court_code`, `date` |
| 12 | `listOfCasesWebService.php` | `cino`, `version_number`, `language_flag`, `bilingual_flag` |
| 13 | `caseHistoryWebService.php` | `cinum` *(or)* `state_code`+`dist_code`+`case_no`+`court_code`, `language_flag`, `bilingual_flag` |
| 14 | `filingCaseHistory.php` | `cino` *(or)* `state_code`+`dist_code`+`case_no`+`court_code`, `language_flag`, `bilingual_flag` |
| 15 | `s_show_business.php` | `state_code`, `dist_code`, `court_code`, `case_number1`, `nextdate1`, `disposal_flag`, `businessDate`, `court_no`, `language_flag`, `bilingual_flag` |
| 16 | `s_show_app.php` | `state_code`, `dist_code`, `court_code`, `case_number1`, `app_cs_no`, `language_flag`, `bilingual_flag` |
| 17 | `todaysCasesWebService.php` | `cnr_numbers`, `action_code`, `version_number`, `language_flag`, `bilingual_flag` |
| 18 | `caseNumberSearch.php` | + `case_number`, `case_type`, `year` |
| 19 | `searchByFilingNumberWebService.php` | + `filingNumber`, `year` |
| 20 | `searchByCaseType.php` | + `case_type`, `year`, `pendingDisposed` |
| 21 | `searchByActWebService.php` | + `selectActTypeText`, `underSectionText`, `pendingDisposed`, `language_flag`, `bilingual_flag` |
| 22 | `searchByAdvocateName.php` | + `checkedSearchByRadioValue`, `advocateName`, `year`, `barstatecode`, `barcode`, `pendingDisposed`, `date`, `language_flag`, `bilingual_flag` |
| 23 | `causeListWebService.php` | (same as #22 with `checkedSearchByRadioValue=3`, `date` mandatory) |
| 24 | `firNumberSearch.php` | + `police_stationcode`, `firNumber`, `year`, `pendingDisposed`, `uniform_code` |
| 25 | `pretrialNumberSearch.php` | + `police_stationcode`, `firNumber`, `year`, `pre_application_type`, `fir_type`, `language_flag`, `bilingual_flag` |
| 26 | `preTrialOrder_pdf.php` | `court_code`, `state_code`, `dist_code`, `orderYr`, `order_id`, `crno` (in `params=`) |
| 27 | `showDataWebService.php` | + `pet_name`, `pendingDisposed`, `year` |
| 28 | `searchCaveat.php` | base + mode-specific keys (see §4.5.1) |
| 29 | `caveatCaseHistoryWebService.php` | `state_code`, `dist_code`, `court_code`, `caveat_number`, `language_flag`, `bilingual_flag` |
| 30 | `stateWebServiceCaveat.php` | `state_code`, `dist_code`, `court_code`, `language_flag`, `bilingual_flag` |
| 31 | `districtWebServiceCaveat.php` | + `establishment_state_code` |
| 32 | `lowerCourtCaveat.php` | + `establishment_state_code`, `establishment_district_code` |
| 33 | `caveatCount.php` | `state_code`, `dist_code`, `court_code` |
| 34 | `cases_new.php` (DC) | `state_code`, `dist_code`, `flag`, `selprevdays`, `court_no`, `court_code`, `causelist_date`, `language_flag`, `bilingual_flag` |
| 34b | `cases_new.php` (HC) | `state_code`, `dist_code`, `selprevdays`, `court_code`, `causelist_date`, `bench_id` |
| 35 | `latlong.php` | `state_code`, `dist_code`, `court_code`, `complex_code` |

Rows annotated with `+` add the listed keys to the common context payload `{state_code, dist_code, court_code_arr, language_flag, bilingual_flag}` (see §4.4).

---

## 7. Reference implementation (pseudo-code)

A non-Cordova client (e.g. Node.js, Python, Postman pre-request script) would do:

```text
function call(endpoint, body):
    # 1. Build IV
    pools     = ["556A586E32723575","34743777217A2543","413F4428472B4B62",
                 "48404D635166546A","614E645267556B58","655368566D597133"]
    idx       = randInt(0, 5)
    globaliv  = pools[idx]
    randomiv  = randomHex(16)
    iv        = bytesFromHex(globaliv + randomiv)

    # 2. Encrypt request
    plaintext = JSON.stringify(body)
    keyReq    = bytesFromHex("4D6251655468576D5A7134743677397A")
    cipher    = AES_CBC_PKCS7(keyReq, iv).encrypt(plaintext)
    params    = randomiv + idx + base64(cipher)

    # 3. Send
    headers = {}
    if endpoint != "appReleaseWebService.php":
        headers["Authorization"] = "Bearer " + encrypt(jwttoken)  # same scheme
    raw = httpGET(BASE_URL + endpoint, query={"params": params}, headers=headers)

    # 4. Decrypt response
    ivResp    = bytesFromHex(raw[:32])
    cipherB64 = raw[32:]
    keyResp   = bytesFromHex("3273357638782F413F4428472B4B6250")
    plain     = AES_CBC_PKCS7(keyResp, ivResp).decrypt(base64Decode(cipherB64))
    obj       = JSON.parse(stripControlChars(plain))

    # 5. Capture rotated JWT, handle 401
    if obj.token: jwttoken = obj.token
    if obj.status == "N" and obj.status_code == "401":
        body["uid"] = deviceUuid + ":" + packageName
        return call(endpoint, body)
    return obj
```

---

## 8. Operational notes & caveats

1. **All requests are GET.** There is no POST endpoint in the bundle — large payloads (e.g. `cnr_numbers` for refresh) are still sent as a query parameter.
2. **Field names are case-sensitive** and must use the exact spellings above (the server-side PHP files read them by literal name).
3. **Numeric values are sent as strings.** Skipping `.toString()` will hash differently after AES padding and produce backend errors.
4. **Date format** is `dd-MM-yyyy` (`30-04-2026`) wherever a date appears on the wire, *except* `s_show_business.php` which accepts the same format.
5. **The “HC” parallel set of endpoints** (`*_hc.js`) reuses many of the same URLs but with smaller payloads (no `language_flag`/`bilingual_flag` on most calls), and adds extras like `causeListBenchWebService.php` and `caseTypeCaveat_hc.php`.
6. **Re-issuing requests after 401:** the client only retries once and only after appending the `uid` (UUID + package name). A second 401 is fatal.
7. **The "encrypted" base host** (`ecourt_mobile_encrypted_DC`) appears to be an internal staging variant of the same API surface; the public production URL is the unsuffixed one.
8. **Do not rely on field nullability** — empty strings are common where a value would be missing (e.g. `result`, `fir_year`, `pendingDisposed`).
9. **PDF endpoints** (`preTrialOrder_pdf.php`) bypass `callToWebService` and are loaded directly through `cordova.InAppBrowser`. The `params` query is built with `encryptData` exactly as documented, but the response is binary PDF, not JSON.
10. **JWT decoding.** `jwt_decode(token)` is only used to inspect the payload locally; the server validates the token against its own secret. The encrypted-Bearer wrapping is therefore an *additional* layer on top of standard JWT.

---

## 9. File-to-endpoint cross-reference

| Endpoint | Primary JS source(s) |
|---|---|
| `appReleaseWebService.php` | `index.js:59`, `index_hc.js:33` |
| `getAllLabelsWebService.php` | `main.js:1055` |
| `stateWebService.php` | `index.js:225`, `index_hc.js:199`, `map.js:135` |
| `districtWebService.php` | `index.js:574`, `index_hc.js:485`, `map.js:159`, `search_by_caveat_hc.js:1005,1038`, `search_by_fir_number_hc.js:451` |
| `courtEstWebService.php` | `main.js:87`, `map.js:326` |
| `courtNameWebService.php` | `cause_list.js:135` |
| `policeStationWebService.php` | `search_by_fir_number.js:404`, `search_by_application.js:704`, `search_by_fir_number_hc.js:481` |
| `actWebService.php` | `search_by_act.js:154`, `search_by_act_hc.js:131` |
| `caseNumberWebService.php` | `search_by_case_number.js:196`, `search_by_case_type.js:180`, `search_by_case_number_hc.js:139`, `search_by_case_type_hc.js:187` |
| `caseTypeCaveat_hc.php` | `search_by_caveat_hc.js:1130` |
| `causeListBenchWebService.php` | `cause_list_hc.js:84` |
| `listOfCasesWebService.php` | `index.js:499` |
| `caseHistoryWebService.php` | `main.js:160`, `index.js:540`, `index_hc.js:456`, `main_hc.js:70`, `my_cases1.js:248`, `my_cases_hc.js:254` |
| `filingCaseHistory.php` | `main.js:234`, `index.js:512`, `my_cases1.js:321` |
| `s_show_business.php` | `case_history.js:760`, `case_history_hc.js:728`, `filing_case_history.js:505` |
| `s_show_app.php` | `case_history.js:801`, `case_history_hc.js:757` |
| `todaysCasesWebService.php` | `my_cases1.js:452`, `my_cases_hc.js:383` |
| `caseNumberSearch.php` | `search_by_case_number.js:174`, `search_by_case_number_hc.js:124` |
| `searchByFilingNumberWebService.php` | `search_by_filing_number.js:154`, `search_by_filing_number_hc.js:107` |
| `searchByCaseType.php` | `search_by_case_type.js:160`, `search_by_case_type_hc.js:168` |
| `searchByActWebService.php` | `search_by_act.js:242`, `search_by_act_hc.js:184` |
| `searchByAdvocateName.php` | `search_by_advocate_name.js:528`, `search_by_advocate_name_hc.js:388` |
| `causeListWebService.php` | `search_by_advocate_name.js:526`, `search_by_advocate_name_hc.js:386` |
| `firNumberSearch.php` | `search_by_fir_number.js:227`, `search_by_fir_number_hc.js:265` |
| `pretrialNumberSearch.php` | `search_by_application.js:280` |
| `preTrialOrder_pdf.php` | `search_by_application.js:528` |
| `showDataWebService.php` | `search_by_party_name.js:211`, `search_by_party_name_hc.js:155` |
| `searchCaveat.php` | `search_by_caveat.js:485`, `search_by_caveat_hc.js:647` |
| `caveatCaseHistoryWebService.php` | `search_by_caveat.js:1001`, `search_by_caveat_hc.js:1101` |
| `stateWebServiceCaveat.php` | `search_by_caveat.js:839`, `search_by_caveat_hc.js:971` |
| `districtWebServiceCaveat.php` | `search_by_caveat.js:902` |
| `lowerCourtCaveat.php` | `search_by_caveat.js:957`, `search_by_caveat_hc.js:1078` |
| `caveatCount.php` | `search_by_caveat_hc.js:1152` |
| `cases_new.php` | `cause_list.js:348`, `cause_list_hc.js:267` |
| `latlong.php` | `map.js:472` |
| `stateWebService_hc.php` | `search_by_fir_number_hc.js:421` (HC state list variant) |

---

## 10. Disclaimer

This documentation is reconstructed from the publicly distributed Android client. Field names and value conventions reflect the client’s expectations; the production server is authoritative. Any usage of these endpoints must comply with the eCourts Services terms of use and Indian law.
