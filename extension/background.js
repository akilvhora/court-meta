// Court Meta - Background Service Worker
// Handles API requests to the local C# backend

const API_BASE = 'http://localhost:5000/api/court';

// Map action names to API endpoint paths and parameter builders
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
  const queryString = buildParams(params);
  const url = `${API_BASE}${path}${queryString}`;

  fetch(url, {
    method: 'GET',
    headers: {
      'Accept': 'application/json',
      'X-Court-Meta-Client': 'extension'
    }
  })
    .then((res) => {
      if (!res.ok) {
        return res.text().then((text) => {
          throw new Error(`HTTP ${res.status}: ${text}`);
        });
      }
      return res.json();
    })
    .then((data) => {
      sendResponse({ success: true, data: data });
    })
    .catch((err) => {
      sendResponse({
        success: false,
        error: err.message || 'Failed to connect to Court Meta API. Make sure the C# backend is running on http://localhost:5000'
      });
    });

  // Return true to keep the message channel open for async response
  return true;
});
