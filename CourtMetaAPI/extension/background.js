// Court Meta - Background Service Worker
// Handles API requests to the local C# backend and refines responses through
// the declarative mapping engine (see ./mapping/).

import { applyMapping } from './mapping/mapper.js';

const API_BASE = 'http://localhost:5000/api/court';

// Lazy-loaded bundled mapping configs. Add new API configs by dropping a JSON
// file in ./mapping/ and registering it here.
const MAPPING_CONFIGS = {
  cnrSearch: 'mapping/cnrMapping.json'
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

// Map action names to API endpoint paths and parameter builders.
const ACTION_MAP = {
  fetchStates: {
    path: '/states',
    buildParams: () => ''
  },
  fetchDistricts: {
    path: '/districts',
    buildParams: (params) => `?state_code=${encodeURIComponent(params.state_code || '')}`
  },
  fetchComplexes: {
    path: '/complexes',
    buildParams: (params) =>
      `?state_code=${encodeURIComponent(params.state_code || '')}&dist_code=${encodeURIComponent(params.dist_code || '')}`
  },
  fetchCourts: {
    path: '/courts',
    buildParams: (params) =>
      `?state_code=${encodeURIComponent(params.state_code || '')}&dist_code=${encodeURIComponent(params.dist_code || '')}&court_code=${encodeURIComponent(params.court_code || '')}`
  },
  cnrSearch: {
    path: '/cnr',
    buildParams: (params) => `?cino=${encodeURIComponent(params.cino || '')}`
  }
};

chrome.runtime.onMessage.addListener(function (message, sender, sendResponse) {
  if (message.type !== 'COURT_META_REQUEST') return false;

  const action = message.action;
  const params = message.params || {};

  if (!ACTION_MAP[action]) {
    sendResponse({ success: false, error: `Unknown action: ${action}` });
    return false;
  }

  const { path, buildParams } = ACTION_MAP[action];
  const url = `${API_BASE}${path}${buildParams(params)}`;

  (async () => {
    try {
      const res = await fetch(url, {
        method: 'GET',
        headers: {
          Accept: 'application/json',
          'X-Court-Meta-Client': 'extension'
        }
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(`HTTP ${res.status}: ${text}`);
      }
      const data = await res.json();

      const mappingPromise = loadMapping(action);
      if (mappingPromise) {
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
