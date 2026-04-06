/**
 * Court Meta - Website Integration Library
 *
 * Communicates with the Court Meta Chrome Extension via window.postMessage.
 * If the extension is not installed, all calls reject with a helpful error.
 *
 * Usage:
 *   CourtMeta.ready().then(() => CourtMeta.fetchStates()).then(...)
 */

const CourtMeta = (() => {
  'use strict';

  let extensionReady = false;
  let readyResolvers = [];

  // Listen for the extension's READY reply (response to our ping below)
  window.addEventListener('message', function (event) {
    if (event.source !== window) return;
    const msg = event.data;
    if (!msg) return;

    if (msg.type === 'COURT_META_READY') {
      extensionReady = true;
      readyResolvers.forEach((resolve) => resolve());
      readyResolvers = [];
    }
  });

  // Ping the content script. The listener above is already registered at this
  // point, so when the content script replies with COURT_META_READY we catch it.
  window.postMessage({ type: 'COURT_META_PING' }, '*');

  /**
   * Returns a promise that resolves when the extension is detected,
   * or rejects after a timeout if not found.
   */
  function ready(timeoutMs = 2000) {
    if (extensionReady) return Promise.resolve();

    return new Promise((resolve, reject) => {
      readyResolvers.push(resolve);
      setTimeout(() => {
        const idx = readyResolvers.indexOf(resolve);
        if (idx !== -1) {
          readyResolvers.splice(idx, 1);
          reject(
            new Error(
              'Court Meta extension not detected. Please install the extension and reload the page.'
            )
          );
        }
      }, timeoutMs);
    });
  }

  /**
   * Send a request to the extension and wait for the response.
   * Returns a promise that resolves with { success, data, error }.
   */
  function request(action, params = {}) {
    return ready().then(() => {
      return new Promise((resolve, reject) => {
        const requestId = `cmr_${Date.now()}_${Math.random().toString(36).slice(2)}`;

        function handleResponse(event) {
          if (event.source !== window) return;
          const msg = event.data;
          if (!msg || msg.type !== 'COURT_META_RESPONSE' || msg.requestId !== requestId) return;

          window.removeEventListener('message', handleResponse);
          clearTimeout(timeoutHandle);

          if (msg.success) {
            resolve(msg.data);
          } else {
            reject(new Error(msg.error || 'Unknown error from Court Meta extension'));
          }
        }

        window.addEventListener('message', handleResponse);

        // Timeout after 30s
        const timeoutHandle = setTimeout(() => {
          window.removeEventListener('message', handleResponse);
          reject(new Error(`Request timed out (action: ${action})`));
        }, 30000);

        window.postMessage(
          { type: 'COURT_META_REQUEST', action, params, requestId },
          '*'
        );
      });
    });
  }

  return {
    ready,
    fetchStates: () => request('fetchStates'),
    fetchDistricts: (state_code) => request('fetchDistricts', { state_code }),
    fetchComplexes: (state_code, dist_code) => request('fetchComplexes', { state_code, dist_code }),
    fetchCourts: (state_code, dist_code, court_code) =>
      request('fetchCourts', { state_code, dist_code, court_code }),
    cnrSearch: (cino) => request('cnrSearch', { cino })
  };
})();
