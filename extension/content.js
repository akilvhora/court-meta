// Court Meta - Content Script
// Bridges window.postMessage from the web page to the background service worker

(function () {
  'use strict';

  function isExtensionAlive() {
    try {
      // Accessing chrome.runtime.id throws if the context is invalidated
      return !!chrome.runtime?.id;
    } catch {
      return false;
    }
  }

  function onPageMessage(event) {
    if (event.source !== window) return;

    const message = event.data;
    if (!message) return;

    // ── Ping/pong handshake ──────────────────────────────────────────────────
    if (message.type === 'COURT_META_PING') {
      if (!isExtensionAlive()) {
        // Extension was reloaded; stop listening and let the page detect the error
        window.removeEventListener('message', onPageMessage);
        return;
      }
      window.postMessage({ type: 'COURT_META_READY' }, '*');
      return;
    }

    // ── API request relay ────────────────────────────────────────────────────
    if (message.type !== 'COURT_META_REQUEST') return;

    const requestId = message.requestId;
    if (!requestId) return;

    // Guard: if the extension was reloaded the runtime context is gone
    if (!isExtensionAlive()) {
      window.removeEventListener('message', onPageMessage);
      window.postMessage(
        {
          type: 'COURT_META_RESPONSE',
          requestId,
          success: false,
          error: 'Extension was reloaded. Please refresh the page.'
        },
        '*'
      );
      return;
    }

    try {
      chrome.runtime.sendMessage(
        {
          type: 'COURT_META_REQUEST',
          action: message.action,
          params: message.params || {},
          requestId
        },
        function (response) {
          if (chrome.runtime.lastError) {
            window.postMessage(
              {
                type: 'COURT_META_RESPONSE',
                requestId,
                success: false,
                error: chrome.runtime.lastError.message || 'Extension communication error'
              },
              '*'
            );
            return;
          }

          window.postMessage(
            {
              type: 'COURT_META_RESPONSE',
              requestId,
              success: response ? response.success : false,
              data: response ? response.data : null,
              error: response ? response.error : 'No response from background'
            },
            '*'
          );
        }
      );
    } catch (err) {
      // chrome.runtime.sendMessage itself throws when the context is invalidated
      window.removeEventListener('message', onPageMessage);
      window.postMessage(
        {
          type: 'COURT_META_RESPONSE',
          requestId,
          success: false,
          error: 'Extension context lost. Please refresh the page.'
        },
        '*'
      );
    }
  }

  window.addEventListener('message', onPageMessage);

  // ── Stream-event push from background → page ─────────────────────────────
  // Streaming actions (e.g. state-wide advocate search) acknowledge the page
  // synchronously and then push subsequent events via chrome.tabs.sendMessage.
  // We just relay each event onto the window message bus so court-meta.js can
  // dispatch it to the matching `onProgress` callback by `requestId`.
  chrome.runtime.onMessage.addListener(function (message) {
    if (!message || message.type !== 'COURT_META_STREAM_EVENT') return;
    window.postMessage(message, '*');
  });
})();
