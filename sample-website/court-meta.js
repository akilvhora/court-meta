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

  // The extension's background.js rejects unknown actions with
  // "Unknown action: <name>". That almost always means an old build is still
  // installed in Chrome (manifests with the same version don't auto-replace).
  // Rewrite to a message the page-level error UI can show without context.
  function rewriteExtensionError(err, action) {
    const text = err || 'Unknown error from Court Meta extension';
    if (typeof text === 'string' && text.indexOf('Unknown action') === 0) {
      return 'Court Meta extension is out of date (action "' + action +
        '" not recognised). Open chrome://extensions, click reload on Court Meta, ' +
        'then refresh this page.';
    }
    return text;
  }

  /**
   * Streaming request. Sends a COURT_META_REQUEST whose action is wired
   * `streaming: true` in background.js. The extension acknowledges
   * synchronously, then pushes COURT_META_STREAM_EVENT messages — each one is
   * dispatched to `onProgress`. The returned promise resolves once the final
   * `done` event fires (or rejects on a fatal `error`).
   *
   * Used for state-wide advocate search where progress per district is the
   * whole point.
   */
  function streamRequest(action, params, onProgress) {
    return ready().then(() => new Promise((resolve, reject) => {
      const requestId = `cmr_${Date.now()}_${Math.random().toString(36).slice(2)}`;
      let progressCount = 0;
      let lastError = null;

      function listener(event) {
        if (event.source !== window) return;
        const msg = event.data;
        if (!msg || msg.requestId !== requestId) return;

        if (msg.type === 'COURT_META_STREAM_EVENT') {
          const ev = msg.event || {};
          if (ev.type === 'done') {
            window.removeEventListener('message', listener);
            if (lastError) reject(new Error(lastError));
            else resolve({ progressCount });
            return;
          }
          if (ev.type === 'error') {
            lastError = ev.error || 'stream error';
            // wait for the trailing `done` to actually settle the promise
            return;
          }
          progressCount += 1;
          try { if (onProgress) onProgress(ev); }
          catch (e) { console.error('[CourtMeta] onProgress threw:', e); }
          return;
        }

        // Synchronous ack from the extension. If the extension reports a
        // failure here (e.g. unknown action), bail immediately.
        if (msg.type === 'COURT_META_RESPONSE') {
          if (msg.success === false) {
            window.removeEventListener('message', listener);
            reject(new Error(rewriteExtensionError(msg.error, action)));
          }
          // success ack — keep listening for stream events
        }
      }

      window.addEventListener('message', listener);
      window.postMessage(
        { type: 'COURT_META_REQUEST', action, params, requestId },
        '*'
      );
    }));
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
            reject(new Error(rewriteExtensionError(msg.error, action)));
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

    // CNR (legacy): returns the raw history blob from eCourts.
    cnrSearch: (cino) => request('cnrSearch', { cino }),

    // CNR bundle: structured CaseBundle with caseInfo, parties, advocates, court,
    // filing, hearings, transfers, processes, orders. Workflow A entry point.
    cnrBundle: (cino) => request('cnrBundle', { cino }),

    // Resolve a cause-list row (case_no + establishment court_code) to a CNR
    // and parsed CaseBundle. Resolves to { cino, data: bundle }.
    cnrByCaseNo: ({ state_code, dist_code, court_code, case_no }) =>
      request('cnrByCaseNo', { state_code, dist_code, court_code, case_no }),

    // View Business — drill-down for a specific hearing date in a CaseBundle.
    cnrBusiness: ({ state_code, dist_code, court_code, case_number,
                    hearingDate, disposalFlag, courtNo }) =>
      request('cnrBusiness', { state_code, dist_code, court_code, case_number,
                               hearingDate, disposalFlag, courtNo }),

    // Writ / application detail — drill-down for an application row.
    cnrWrit: ({ state_code, dist_code, court_code, case_number, app_cs_no }) =>
      request('cnrWrit', { state_code, dist_code, court_code, case_number, app_cs_no }),

    // Order PDF — resolves to { dataUrl, mime, size }. dataUrl can be assigned to
    // an <iframe src=""> or used as the href of a download anchor.
    cnrOrderPdf: ({ state_code, dist_code, court_code, orderYr, order_id, crno }) =>
      request('cnrOrderPdf', { state_code, dist_code, court_code, orderYr, order_id, crno }),

    // ── Phase 3 — case / filing number search ──────────────────────────────
    // Case-type dropdown for the selected court (cached server-side).
    fetchCaseTypes: ({ state_code, dist_code, court_code }) =>
      request('fetchCaseTypes', { state_code, dist_code, court_code }),

    // Search by case number. `courts` = comma-joined njdg_est_codes.
    // Resolves to { count, results: [{ cino, court_code, ... }], raw }.
    searchByCaseNumber: ({ state_code, dist_code, courts, case_type, number, year }) =>
      request('searchByCaseNumber', { state_code, dist_code, courts, case_type, number, year }),

    // Search by filing number. Same shape as searchByCaseNumber.
    searchByFilingNumber: ({ state_code, dist_code, courts, filingNumber, year }) =>
      request('searchByFilingNumber', { state_code, dist_code, courts, filingNumber, year }),

    // ── Phase 4 — cause list ───────────────────────────────────────────────
    // Cases caused at a given court room on a date.
    //   flag: 'civ_t' | 'cri_t' (default civ_t)
    //   selprevdays: '0' (today) | '1' (past day)
    // Resolves to { count, html, rows: [{ index, isHeader, cino?, cells: [] }] }.
    causeList: ({ state_code, dist_code, court_code, court_no, date, flag, selprevdays }) =>
      request('fetchCauseList', { state_code, dist_code, court_code, court_no, date, flag, selprevdays }),

    // ── Phase 5 — advocate search ──────────────────────────────────────────
    //
    // Single-court (synchronous JSON):
    //   CourtMeta.searchByAdvocate({
    //     scope: 'court', state_code, dist_code, courts: 'MHAU01,MHAU02',
    //     mode: 'name'|'barcode', advocateName?, barstatecode?, barcode?,
    //     pendingDisposed?, year?, date?
    //   })
    //   → { count, results: [{ cino, court_code, … }], raw }
    //
    // State-wide (NDJSON stream — progress per district):
    //   CourtMeta.searchByAdvocate({
    //     scope: 'state', state_code, mode, advocateName | barcode, …,
    //     onProgress(event) { /* event.type ∈ {start, progress, complete} */ }
    //   })
    //   → resolves with { progressCount } once the final `done` event fires.
    searchByAdvocate(params) {
      const scope = (params && params.scope) || 'court';
      if (scope === 'state') {
        const onProgress = params.onProgress;
        const payload = Object.assign({}, params);
        delete payload.scope;
        delete payload.onProgress;
        return streamRequest('searchByAdvocateState', payload, onProgress);
      }
      const payload = Object.assign({}, params);
      delete payload.scope;
      delete payload.onProgress;
      return request('searchByAdvocateCourt', payload);
    }
  };
})();
