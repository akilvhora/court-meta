// Court Meta Sample Website – App Logic

(function () {
  'use strict';

  function el(id) { return document.getElementById(id); }

  function escapeHtml(str) {
    return String(str ?? '–')
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function buildRows(tbody, rows) {
    tbody.innerHTML = rows
      .map((cols, i) =>
        `<tr>${[i + 1, ...cols].map(c => `<td>${escapeHtml(c)}</td>`).join('')}</tr>`)
      .join('');
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

  function fieldErr(id, msg) {
    const e = el(id);
    if (e) { e.textContent = msg; e.style.color = '#c53030'; e.style.fontSize = '12px'; }
  }
  function clearFieldErr(id) { const e = el(id); if (e) e.textContent = ''; }

  function setStatusBar(state, text) {
    el('extIndicator').className = 'status-indicator ' + state;
    el('extStatus').textContent = text;
  }

  // ── Copy buttons ─────────────────────────────────────────────────────────────
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

  // ── Tab switching ────────────────────────────────────────────────────────────
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', function () {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
      this.classList.add('active');
      el('tab-' + this.dataset.tab).classList.add('active');

      // Retry loading states when browser tab is clicked and dropdown is still empty
      if (this.dataset.tab === 'browser' && el('selState').options.length <= 1) {
        loadStates();
      }
    });
  });

  // ── Extension status ─────────────────────────────────────────────────────────
  CourtMeta.ready()
    .then(() => {
      setStatusBar('connected', 'Court Meta extension connected');
      const banner = el('installBanner');
      if (banner) banner.classList.add('hidden');
      loadStates();          // kick off state load once extension is ready
    })
    .catch(() => {
      setStatusBar('error', 'Court Meta extension not detected. See installation instructions below.');
    });

  // ═══════════════════════════════════════════════════════════════════════════
  // TAB 1 – CNR Search
  // ═══════════════════════════════════════════════════════════════════════════
  const cnrBtn = el('cnrSearchBtn');
  const cnrError = el('cnrError');

  cnrBtn.addEventListener('click', function () {
    const cino = el('cnrNumber').value.trim().toUpperCase();
    hideAlert(cnrError);
    el('cnrSplit').classList.remove('show');
    el('cnrRaw').textContent = '';
    el('cnrParsed').textContent = '';

    if (!cino) { showAlert(cnrError, 'Please enter a CNR number.'); return; }
    if (cino.length < 16) { showAlert(cnrError, 'CNR number must be at least 16 characters.'); return; }

    setLoading(cnrBtn, true);

    CourtMeta.cnrSearch(cino)
      .then(data => {
        el('cnrParsed').textContent = JSON.stringify(data.parsed, null, 2);
        el('cnrRaw').textContent    = JSON.stringify(data.raw,    null, 2);
        el('cnrSplit').classList.add('show');
      })
      .catch(err => showAlert(cnrError, err.message))
      .finally(() => setLoading(cnrBtn, false));
  });

  el('cnrNumber').addEventListener('keydown', e => { if (e.key === 'Enter') cnrBtn.click(); });

  // ═══════════════════════════════════════════════════════════════════════════
  // TAB 2 – Court Browser  (cascading: State → District → Complex → Courts)
  // ═══════════════════════════════════════════════════════════════════════════
  const selState    = el('selState');
  const selDistrict = el('selDistrict');
  const selComplex  = el('selComplex');
  const courtsSection = el('courtsSection');
  const courtsBody    = el('courtsBody');
  const courtsErr     = el('courtsErr');
  const browserLoading = el('browserLoading');

  function showBrowserLoading(on) {
    browserLoading.style.display = on ? 'block' : 'none';
  }

  function resetSelect(sel, placeholder) {
    sel.innerHTML = `<option value="">${escapeHtml(placeholder)}</option>`;
    sel.disabled = true;
  }

  // ── Load states ──────────────────────────────────────────────────────────
  function loadStates() {
    showBrowserLoading(true);
    clearFieldErr('stateErr');
    setStatusBar('checking', 'Loading states…');

    CourtMeta.fetchStates()
      .then(data => {
        const states = data.states || [];
        if (!states.length) {
          fieldErr('stateErr', 'No states returned.');
          setStatusBar('error', 'Failed to load states — no data returned.');
          return;
        }
        selState.innerHTML = '<option value="">— Select State —</option>' +
          states.map(s =>
            `<option value="${escapeHtml(s.state_code)}">${escapeHtml(s.state_name)}</option>`
          ).join('');
        selState.disabled = false;
        setStatusBar('connected', 'Court Meta extension connected');
      })
      .catch(err => {
        fieldErr('stateErr', err.message);
        setStatusBar('error', 'Failed to load states: ' + err.message);
      })
      .finally(() => showBrowserLoading(false));
  }

  // ── State changed → load districts ──────────────────────────────────────
  selState.addEventListener('change', function () {
    resetSelect(selDistrict, '— Select District —');
    resetSelect(selComplex, '— Select District First —');
    courtsSection.style.display = 'none';
    clearFieldErr('districtErr');
    clearFieldErr('complexErr');
    hideAlert(courtsErr);

    const stateCode = this.value;
    if (!stateCode) return;

    showBrowserLoading(true);

    CourtMeta.fetchDistricts(stateCode)
      .then(data => {
        const districts = data.districts || [];
        if (!districts.length) {
          fieldErr('districtErr', 'No districts found for this state.');
          return;
        }
        selDistrict.innerHTML = '<option value="">— Select District —</option>' +
          districts.map(d =>
            `<option value="${escapeHtml(d.dist_code)}">${escapeHtml(d.dist_name)}</option>`
          ).join('');
        selDistrict.disabled = false;
      })
      .catch(err => fieldErr('districtErr', err.message))
      .finally(() => showBrowserLoading(false));
  });

  // ── District changed → load complexes ───────────────────────────────────
  selDistrict.addEventListener('change', function () {
    resetSelect(selComplex, '— Select Complex —');
    courtsSection.style.display = 'none';
    clearFieldErr('complexErr');
    hideAlert(courtsErr);

    const stateCode    = selState.value;
    const districtCode = this.value;
    if (!districtCode) return;

    showBrowserLoading(true);

    CourtMeta.fetchComplexes(stateCode, districtCode)
      .then(data => {
        const complexes = data.complexes || [];
        if (!complexes.length) {
          fieldErr('complexErr', 'No court complexes found for this district.');
          return;
        }
        selComplex.innerHTML = '<option value="">— Select Complex —</option>' +
          complexes.map(c =>
            `<option value="${escapeHtml(c.njdg_est_code)}"
                     data-complex="${escapeHtml(c.complex_code)}">${escapeHtml(c.court_complex_name)}</option>`
          ).join('');
        selComplex.disabled = false;
      })
      .catch(err => fieldErr('complexErr', err.message))
      .finally(() => showBrowserLoading(false));
  });

  // ── Complex changed → load courts ───────────────────────────────────────
  selComplex.addEventListener('change', function () {
    courtsSection.style.display = 'none';
    hideAlert(courtsErr);
    courtsBody.innerHTML = '';

    const stateCode    = selState.value;
    const districtCode = selDistrict.value;
    const courtCode    = this.value;
    if (!courtCode) return;

    showBrowserLoading(true);

    CourtMeta.fetchCourts(stateCode, districtCode, courtCode)
      .then(data => {
        // rawCourtNames is included in the response for debugging
        if (data.rawCourtNames) {
          console.debug('[CourtMeta] rawCourtNames:', data.rawCourtNames);
        }

        const courts = (data.courts || []).filter(c => !c.isHeader);
        if (!courts.length) {
          showAlert(courtsErr, 'No courts found for this complex.');
          courtsSection.style.display = 'block';
          return;
        }
        buildRows(courtsBody, courts.map(c => [c.code, c.name]));
        hideAlert(courtsErr);
        courtsSection.style.display = 'block';
      })
      .catch(err => {
        showAlert(courtsErr, err.message);
        courtsSection.style.display = 'block';
      })
      .finally(() => showBrowserLoading(false));
  });

})();
