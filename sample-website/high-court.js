// Court Meta - High Court page
//
// Talks to the HC-scoped endpoints exposed by court-meta-api's HcController
// (services_HC_4.0/). The extension routes every request through the HC base
// URL when the action name starts with "hc" or "fetchHc" — see background.js.

(function () {
  'use strict';

  function el(id) { return document.getElementById(id); }

  function escapeHtml(str) {
    return String(str ?? '–')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function setLoading(btn, on) {
    btn.disabled = on;
    if (on) { btn._t = btn.innerHTML; btn.innerHTML = '<span class="spinner"></span> Loading…'; }
    else btn.innerHTML = btn._t || 'Search';
  }

  function showAlert(alertEl, msg) {
    alertEl.textContent = msg;
    alertEl.className = 'alert alert-error show';
  }
  function hideAlert(alertEl) { alertEl.className = 'alert'; alertEl.textContent = ''; }

  function setStatusBar(state, text) {
    el('extIndicator').className = 'status-indicator ' + state;
    el('extStatus').textContent = text;
  }

  // Copy buttons — same pattern as app.js
  document.querySelectorAll('.copy-btn').forEach(btn => {
    btn.addEventListener('click', function () {
      const target = el(this.dataset.copyTarget);
      const text = target ? target.textContent : '';
      if (!text) return;
      const label = this.querySelector('.copy-btn-label');
      navigator.clipboard.writeText(text).then(() => {
        const original = label ? label.textContent : '';
        if (label) label.textContent = 'Copied';
        this.classList.add('copied');
        setTimeout(() => {
          if (label) label.textContent = original;
          this.classList.remove('copied');
        }, 1200);
      });
    });
  });

  // ── Extension status + state list on ready ────────────────────────────────
  CourtMeta.ready()
    .then(() => {
      setStatusBar('connected', 'Court Meta extension connected');
      loadHcStates();
    })
    .catch(() => {
      setStatusBar('error', 'Court Meta extension not detected. Install it from the main page.');
      el('hcState').innerHTML = '<option value="">— Extension required —</option>';
    });

  // The HcController pre-normalises both responses:
  //   /hc/states  → { success, states:  [{ state_code, state_name, state_lang }] }
  //   /hc/benches → { success, benches: [{ bench_code, bench_name }] }
  // Extract from either the raw envelope or an extension-wrapped shape.
  function pickList(resp, key) {
    if (!resp) return [];
    for (const c of [resp, resp.data, resp.raw, resp.parsed && resp.parsed.raw]) {
      if (c && Array.isArray(c[key])) return c[key];
    }
    return [];
  }

  function loadHcStates() {
    const sState = el('hcState');
    CourtMeta.hc.fetchStates()
      .then(resp => {
        const states = pickList(resp, 'states');
        if (!states.length) {
          sState.innerHTML = '<option value="">— No states returned —</option>';
          return;
        }
        sState.innerHTML = '<option value="">— Select state —</option>' +
          states.map(s => `<option value="${escapeHtml(s.state_code)}">${escapeHtml(s.state_name || s.state_code)}</option>`).join('');
      })
      .catch(err => {
        console.error('[CourtMeta HC] fetchStates:', err);
        sState.innerHTML = '<option value="">— Error loading states —</option>';
      });
  }

  // ── State → Bench cascade ─────────────────────────────────────────────────
  el('hcState').addEventListener('change', function () {
    const sBench = el('hcBench');
    sBench.innerHTML = '<option value="">— Loading… —</option>';
    sBench.disabled = true;

    const stateCode = this.value;
    if (!stateCode) {
      sBench.innerHTML = '<option value="">— Select a state first —</option>';
      return;
    }

    CourtMeta.hc.fetchBenches(stateCode)
      .then(resp => {
        const benches = pickList(resp, 'benches');
        if (!benches.length) {
          sBench.innerHTML = '<option value="">— No benches —</option>';
          return;
        }
        sBench.innerHTML = '<option value="">— Select bench —</option>' +
          benches.map(b => `<option value="${escapeHtml(b.bench_code)}">${escapeHtml(b.bench_name || b.bench_code)}</option>`).join('');
        sBench.disabled = false;
      })
      .catch(err => {
        console.error('[CourtMeta HC] fetchBenches:', err);
        sBench.innerHTML = '<option value="">— Error loading benches —</option>';
      });
  });

  // ── CNR search ────────────────────────────────────────────────────────────
  const hcCnrError = el('hcCnrError');
  const hcCnrBtn   = el('hcCnrSearchBtn');

  hcCnrBtn.addEventListener('click', function () {
    const cino = el('hcCnrNumber').value.trim().toUpperCase();
    hideAlert(hcCnrError);
    el('hcCnrSplit').classList.remove('show');
    el('hcCnrRaw').textContent = '';
    el('hcCnrParsed').textContent = '';

    if (!cino) { showAlert(hcCnrError, 'Please enter a CNR number.'); return; }
    if (cino.length < 16) { showAlert(hcCnrError, 'CNR number must be at least 16 characters.'); return; }

    setLoading(this, true);
    const btn = this;
    CourtMeta.hc.cnrSearch(cino)
      .then(data => {
        el('hcCnrParsed').textContent = JSON.stringify(data.parsed, null, 2);
        el('hcCnrRaw').textContent    = JSON.stringify(data.raw,    null, 2);
        el('hcCnrSplit').classList.add('show');
      })
      .catch(err => showAlert(hcCnrError, err.message))
      .finally(() => setLoading(btn, false));
  });

  el('hcCnrNumber').addEventListener('keydown', e => { if (e.key === 'Enter') hcCnrBtn.click(); });

  // ── Bulk order download (HC-scoped) ───────────────────────────────────────
  function renderOrdersReport(report, kindLabel) {
    const box = el('hcOrdersReport');
    if (!report) { box.style.display = 'none'; return; }
    const rows = (report.results || []).map((r, i) => `
      <tr>
        <td>${i + 1}</td>
        <td>${escapeHtml(r.orderDetails || '–')}</td>
        <td><span class="order-status order-status-${escapeHtml(r.status)}">${escapeHtml(r.status || '–')}</span></td>
        <td style="font-family:monospace; font-size:11px;">${escapeHtml(r.path || r.error || '–')}</td>
        <td style="text-align:right;">${r.bytes != null ? escapeHtml(r.bytes) : '–'}</td>
      </tr>
    `).join('');

    box.innerHTML = `
      <div style="font-weight:600; margin-bottom:6px;">
        ${escapeHtml(kindLabel)} orders — ${escapeHtml(report.downloaded ?? 0)} downloaded,
        ${escapeHtml(report.failed ?? 0)} failed, ${escapeHtml(report.skipped ?? 0)} skipped
        (${escapeHtml(report.total ?? 0)} total)
      </div>
      <div style="font-size:12px; color:#4a5568; margin-bottom:8px;">
        Saved to <code>${escapeHtml(report.directory || '–')}</code>
      </div>
      ${rows ? `<div class="table-wrap"><table>
        <thead><tr><th>#</th><th>Order</th><th>Status</th><th>Path / Error</th><th>Bytes</th></tr></thead>
        <tbody>${rows}</tbody>
      </table></div>` : '<div style="color:#718096;">No orders listed on this CNR.</div>'}
    `;
    box.style.display = 'block';
  }

  function runOrderDownload(kind, btn) {
    const cino = el('hcCnrNumber').value.trim().toUpperCase();
    hideAlert(hcCnrError);
    el('hcOrdersReport').style.display = 'none';
    if (!cino) { showAlert(hcCnrError, 'Enter a CNR number before downloading orders.'); return; }
    if (cino.length < 16) { showAlert(hcCnrError, 'CNR number must be at least 16 characters.'); return; }

    setLoading(btn, true);
    const call = kind === 'final' ? CourtMeta.hc.getFinalOrders(cino) : CourtMeta.hc.getInterimOrders(cino);
    call
      .then(resp => renderOrdersReport(resp?.data ?? resp, kind === 'final' ? 'Final' : 'Interim'))
      .catch(err => showAlert(hcCnrError, err.message))
      .finally(() => setLoading(btn, false));
  }

  el('hcFinalOrdersBtn')  .addEventListener('click', function () { runOrderDownload('final',   this); });
  el('hcInterimOrdersBtn').addEventListener('click', function () { runOrderDownload('interim', this); });
})();
