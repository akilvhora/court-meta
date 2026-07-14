// Court Meta - Background Service Worker
// Handles API requests to the local C# backend and refines responses through
// the declarative mapping engine (see ./mapping/).

import { applyMapping } from './mapping/mapper.js';

const API_BASE = 'http://localhost:5000/api/court';

// Lazy-loaded bundled mapping configs. The same JSON ships server-side as
// embedded resources; on a paid tier the API returns a pre-parsed envelope
// (`{ schemaVersion, parsed }`) and we skip the local engine. Local mapping
// is the fallback for free tier and for older API binaries.
const MAPPING_CONFIGS = {
  cnrSearch:      'mapping/cnrMapping.json',
  cnrBundle:      'mapping/cnrMapping.json',
  hcCnrSearch:    'mapping/cnrMapping.json',
  hcCnrBundle:    'mapping/cnrMapping.json',
  fetchComplexes: 'mapping/complexesMapping.json'
};
const mappingCache = {};

function loadMapping(action) {
  const path = MAPPING_CONFIGS[action];
  if (!path) return null;
  if (!mappingCache[action]) {
    mappingCache[action] = fetch(chrome.runtime.getURL(path)).then((r) => {
      if (!r.ok) throw new Error(`Failed to load mapping ${path}: HTTP ${r.status}`);
      return r.json();
    });
  }
  return mappingCache[action];
}

function qs(params) {
  const entries = Object.entries(params || {}).filter(([, v]) => v !== undefined && v !== null && v !== '');
  if (!entries.length) return '';
  return '?' + entries.map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`).join('&');
}

// Map action names to API endpoint paths and parameter builders.
// `responseType` defaults to 'json'; set to 'blob' for binary endpoints (PDF).
const ACTION_MAP = {
  fetchStates: {
    path: '/states',
    buildParams: () => ''
  },
  fetchDistricts: {
    path: '/districts',
    buildParams: (p) => qs({ state_code: p.state_code })
  },
  fetchComplexes: {
    path: '/complexes',
    buildParams: (p) => qs({ state_code: p.state_code, dist_code: p.dist_code })
  },
  fetchCourts: {
    path: '/courts',
    buildParams: (p) => qs({ state_code: p.state_code, dist_code: p.dist_code, court_code: p.court_code })
  },
  cnrSearch: {
    path: '/cnr',
    buildParams: (p) => qs({ cino: p.cino })
  },
  cnrBundle: {
    path: '/cnr/bundle',
    buildParams: (p) => qs({ cino: p.cino })
  },
  cnrByCaseNo: {
    path: '/cnr/by-case-no',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      court_code: p.court_code,
      case_no: p.case_no
    })
  },
  cnrBusiness: {
    path: '/cnr/business',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      court_code: p.court_code,
      case_number: p.case_number,
      hearingDate: p.hearingDate,
      disposalFlag: p.disposalFlag,
      courtNo: p.courtNo
    })
  },
  cnrWrit: {
    path: '/cnr/writ',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      court_code: p.court_code,
      case_number: p.case_number,
      app_cs_no: p.app_cs_no
    })
  },
  cnrOrderPdf: {
    path: '/cnr/order-pdf',
    responseType: 'blob',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      court_code: p.court_code,
      orderYr: p.orderYr,
      order_id: p.order_id,
      crno: p.crno
    })
  },

  // ── Bulk order download — server walks the CNR's finalOrder / interimOrder
  //    array and persists each PDF under %APPDATA%/ecourt/{CNR}/. Returns a
  //    DownloadReport with per-item status; no bytes cross the wire.
  getFinalOrders: {
    path: '/cnr/orders/final',
    buildParams: (p) => qs({ cino: p.cino })
  },
  getInterimOrders: {
    path: '/cnr/orders/interim',
    buildParams: (p) => qs({ cino: p.cino })
  },

  // ── High Court scope (services_HC_4.0/) ─────────────────────────────────
  // Base URL swap happens server-side (EcourtsScope.HC); the wire shape is
  // identical to DC apart from the /hc/ path prefix.
  fetchHcStates:      { path: '/hc/states',                buildParams: () => '' },
  fetchHcBenches:     { path: '/hc/benches',               buildParams: (p) => qs({ state_code: p.state_code }) },
  hcCnrSearch:        { path: '/hc/cnr',                   buildParams: (p) => qs({ cino: p.cino }) },
  hcCnrBundle:        { path: '/hc/cnr/bundle',            buildParams: (p) => qs({ cino: p.cino }) },
  hcGetFinalOrders:   { path: '/hc/cnr/orders/final',      buildParams: (p) => qs({ cino: p.cino }) },
  hcGetInterimOrders: { path: '/hc/cnr/orders/interim',    buildParams: (p) => qs({ cino: p.cino }) },

  // ── HC batch search — every case matching the filter on a single bench.
  //    Server-side each hits the shared search endpoint on services_HC_4.0/.
  hcCauseList: {
    path: '/hc/cause-list',
    buildParams: (p) => qs({
      state_code: p.state_code, bench_code: p.bench_code, date: p.date,
      pendingDisposed: p.pendingDisposed
    })
  },
  hcSearchByCaseType: {
    path: '/hc/search/case-type',
    buildParams: (p) => qs({
      state_code: p.state_code, bench_code: p.bench_code,
      case_type: p.case_type, year: p.year, pendingDisposed: p.pendingDisposed
    })
  },
  hcSearchByParty: {
    path: '/hc/search/party',
    buildParams: (p) => qs({
      state_code: p.state_code, bench_code: p.bench_code,
      name: p.name, year: p.year, partyType: p.partyType,
      pendingDisposed: p.pendingDisposed
    })
  },
  hcSearchByAdvocate: {
    path: '/hc/search/advocate',
    buildParams: (p) => qs({
      state_code: p.state_code, bench_code: p.bench_code,
      mode: p.mode, advocateName: p.advocateName,
      barstatecode: p.barstatecode, barcode: p.barcode,
      pendingDisposed: p.pendingDisposed, year: p.year, date: p.date
    })
  },

  // ── Phase 3 — case / filing number search ───────────────────────────────
  fetchCaseTypes: {
    path: '/case-types',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      court_code: p.court_code
    })
  },
  searchByCaseNumber: {
    path: '/search/case-number',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      courts: p.courts,
      case_type: p.case_type,
      number: p.number,
      year: p.year
    })
  },
  searchByFilingNumber: {
    path: '/search/filing-number',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      courts: p.courts,
      filingNumber: p.filingNumber,
      year: p.year
    })
  },

  // ── Phase 4 — cause list ────────────────────────────────────────────────
  fetchCauseList: {
    path: '/cause-list',
    buildParams: (p) => qs({
      state_code: p.state_code,
      dist_code: p.dist_code,
      court_code: p.court_code,
      court_no: p.court_no,
      date: p.date,
      flag: p.flag,
      selprevdays: p.selprevdays
    })
  },

  // ── Phase 5 — advocate search (court scope: synchronous JSON) ───────────
  searchByAdvocateCourt: {
    path: '/search/advocate',
    buildParams: (p) => qs({
      scope: 'court',
      state_code: p.state_code,
      dist_code: p.dist_code,
      courts: p.courts,
      mode: p.mode,
      advocateName: p.advocateName,
      barstatecode: p.barstatecode,
      barcode: p.barcode,
      pendingDisposed: p.pendingDisposed,
      year: p.year,
      date: p.date
    })
  },

  // ── Phase 5 — advocate search (state scope: NDJSON stream) ──────────────
  // streaming=true triggers the chrome.tabs.sendMessage push path below.
  searchByAdvocateState: {
    path: '/search/advocate',
    streaming: true,
    buildParams: (p) => qs({
      scope: 'state',
      state_code: p.state_code,
      mode: p.mode,
      advocateName: p.advocateName,
      barstatecode: p.barstatecode,
      barcode: p.barcode,
      pendingDisposed: p.pendingDisposed,
      year: p.year,
      date: p.date
    })
  }
};

// Reads an NDJSON response body and invokes `onEvent` for each parsed line.
// Tolerant of partial chunks across read() calls.
async function consumeNdjson(response, onEvent) {
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, { stream: true });

    let idx;
    while ((idx = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, idx).trim();
      buffer = buffer.slice(idx + 1);
      if (!line) continue;
      try { onEvent(JSON.parse(line)); }
      catch (err) { console.warn('[CourtMeta] bad NDJSON line:', line, err); }
    }
  }
  // flush any trailing partial line (rare; APIs usually end with \n)
  buffer = buffer.trim();
  if (buffer) {
    try { onEvent(JSON.parse(buffer)); } catch { /* ignore */ }
  }
}

async function blobToDataUrl(blob) {
  const buf = await blob.arrayBuffer();
  const bytes = new Uint8Array(buf);
  // Chunked btoa to avoid call-stack issues on large PDFs
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
  }
  const base64 = btoa(binary);
  const mime = blob.type || 'application/pdf';
  return { dataUrl: `data:${mime};base64,${base64}`, mime, size: bytes.length };
}

chrome.runtime.onMessage.addListener(function (message, sender, sendResponse) {
  if (message.type !== 'COURT_META_REQUEST') return false;

  const action = message.action;
  const params = message.params || {};
  const handler = ACTION_MAP[action];

  if (!handler) {
    sendResponse({ success: false, error: `Unknown action: ${action}` });
    return false;
  }

  const url = `${API_BASE}${handler.path}${handler.buildParams(params)}`;

  // ── Streaming actions (NDJSON, e.g. advocate state-wide search) ───────────
  // Acknowledge the page synchronously, then push events into the originating
  // tab as they arrive. Each event is forwarded to the page by content.js
  // (look for COURT_META_STREAM_EVENT below).
  if (handler.streaming) {
    const tabId = sender?.tab?.id;
    const requestId = message.requestId;
    sendResponse({ success: true, streaming: true });

    if (tabId == null) {
      // No tab to push to — should never happen for a content-script-originated
      // message, but guard so we don't silently drop events.
      console.warn('[CourtMeta] streaming request without tab id; dropping events.');
      return false;
    }

    (async () => {
      const send = (event) => {
        try {
          chrome.tabs.sendMessage(tabId, {
            type: 'COURT_META_STREAM_EVENT',
            requestId,
            event
          });
        } catch (e) {
          console.warn('[CourtMeta] tab.sendMessage failed:', e);
        }
      };

      try {
        const res = await fetch(url, {
          method: 'GET',
          headers: { Accept: 'application/x-ndjson', 'X-Court-Meta-Client': 'extension' }
        });
        if (!res.ok) {
          const text = await res.text();
          send({ type: 'error', error: `HTTP ${res.status}: ${text}` });
          send({ type: 'done' });
          return;
        }
        await consumeNdjson(res, send);
        send({ type: 'done' });
      } catch (err) {
        send({ type: 'error', error: err.message || 'stream failed' });
        send({ type: 'done' });
      }
    })();

    return false;   // we already responded synchronously above
  }

  (async () => {
    try {
      const res = await fetch(url, {
        method: 'GET',
        headers: {
          Accept: handler.responseType === 'blob' ? 'application/pdf' : 'application/json',
          'X-Court-Meta-Client': 'extension'
        }
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`HTTP ${res.status}: ${text}`);
      }

      // Binary: convert to data URL so it survives postMessage
      if (handler.responseType === 'blob') {
        const blob = await res.blob();
        const payload = await blobToDataUrl(blob);
        sendResponse({ success: true, data: payload });
        return;
      }

      const data = await res.json();

      // Server-parsed envelope (paid tier): the API has already run the
      // mapping config and returns { schemaVersion, parsed[, raw] }. Trust
      // it and skip the local engine; this is the design's "single source
      // of truth" — both engines consume the same JSON config, so the JS
      // engine becomes a fallback for free-tier callers and for API binaries
      // older than this extension.
      const mappingPromise = loadMapping(action);
      if (data && typeof data === 'object' && data.schemaVersion && 'parsed' in data) {
        sendResponse({
          success: true,
          data: { parsed: data.parsed, raw: data.raw ?? null, schemaVersion: data.schemaVersion }
        });
      } else if (mappingPromise) {
        const config = await mappingPromise;
        const { result: parsed } = applyMapping(config, data);
        sendResponse({ success: true, data: { parsed, raw: data } });
      } else {
        sendResponse({ success: true, data });
      }
    } catch (err) {
      sendResponse({
        success: false,
        error:
          err.message ||
          'Failed to connect to Court Meta API. Make sure the C# backend is running on http://localhost:5000'
      });
    }
  })();

  // Keep the message channel open for the async response.
  return true;
});
