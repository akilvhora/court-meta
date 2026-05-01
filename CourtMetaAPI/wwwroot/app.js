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
      // Populate the state dropdowns on the new tabs from the same data source.
      populateAuxStates();
    })
    .catch(() => {
      setStatusBar('error', 'Court Meta extension not detected. See installation instructions below.');
    });

  // ── Shared cascading helper used by the three new tabs ─────────────────────
  // Each tab supplies the IDs of its state/district/complex selects; this wires
  // up the same state→district→complex cascade as the Court Browser tab without
  // duplicating the loaders.
  function wireCascade({ stateId, districtId, complexId, onComplexSelected }) {
    const sState    = el(stateId);
    const sDistrict = el(districtId);
    const sComplex  = el(complexId);

    sState.addEventListener('change', function () {
      sDistrict.innerHTML = '<option value="">— Select District —</option>';
      sDistrict.disabled = true;
      sComplex.innerHTML = '<option value="">— Select District first —</option>';
      sComplex.disabled = true;
      if (onComplexSelected) onComplexSelected(null);

      const stateCode = this.value;
      if (!stateCode) return;

      CourtMeta.fetchDistricts(stateCode)
        .then(data => {
          const districts = data.districts || [];
          sDistrict.innerHTML = '<option value="">— Select District —</option>' +
            districts.map(d =>
              `<option value="${escapeHtml(d.dist_code)}">${escapeHtml(d.dist_name)}</option>`
            ).join('');
          sDistrict.disabled = false;
        })
        .catch(err => console.error('[CourtMeta] districts:', err));
    });

    sDistrict.addEventListener('change', function () {
      sComplex.innerHTML = '<option value="">— Select Complex —</option>';
      sComplex.disabled = true;
      if (onComplexSelected) onComplexSelected(null);

      const stateCode = sState.value;
      const distCode = this.value;
      if (!distCode) return;

      CourtMeta.fetchComplexes(stateCode, distCode)
        .then(data => {
          const complexes = data.complexes || [];
          sComplex.innerHTML = '<option value="">— Select Complex —</option>' +
            complexes.map(c =>
              `<option value="${escapeHtml(c.njdg_est_code)}">${escapeHtml(c.court_complex_name)}</option>`
            ).join('');
          sComplex.disabled = false;
        })
        .catch(err => console.error('[CourtMeta] complexes:', err));
    });

    sComplex.addEventListener('change', function () {
      const ctx = {
        state_code: sState.value,
        dist_code: sDistrict.value,
        court_code: this.value
      };
      if (onComplexSelected) onComplexSelected(this.value ? ctx : null);
    });
  }

  function populateAuxStates() {
    CourtMeta.fetchStates().then(data => {
      const states = (data.states || []);
      const opts = '<option value="">— Select State —</option>' +
        states.map(s => `<option value="${escapeHtml(s.state_code)}">${escapeHtml(s.state_name)}</option>`).join('');
      ['csState', 'fsState', 'clState', 'advState'].forEach(id => {
        const sel = el(id);
        if (sel) sel.innerHTML = opts;
      });
    }).catch(() => { /* status bar already shows the error */ });
  }

  // Switch to the CNR tab and populate / submit it. Used by result-row click-throughs.
  function jumpToCnr(cino) {
    if (!cino) return;
    document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
    document.querySelector('.tab-btn[data-tab="cnr"]').classList.add('active');
    el('tab-cnr').classList.add('active');
    el('cnrNumber').value = cino;
    el('cnrSearchBtn').click();
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

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

  // ═══════════════════════════════════════════════════════════════════════════
  // TAB 3 – Case Number Search
  // ═══════════════════════════════════════════════════════════════════════════
  let csCtx = null;        // { state_code, dist_code, court_code } once complex is selected
  const csCaseType = el('csCaseType');

  wireCascade({
    stateId: 'csState',
    districtId: 'csDistrict',
    complexId: 'csComplex',
    onComplexSelected: (ctx) => {
      csCtx = ctx;
      csCaseType.innerHTML = '<option value="">— Select complex first —</option>';
      csCaseType.disabled = true;
      if (!ctx) return;
      CourtMeta.fetchCaseTypes(ctx)
        .then(data => {
          const types = data.caseTypes || [];
          csCaseType.innerHTML = '<option value="">— Select Case Type —</option>' +
            types.map(t => `<option value="${escapeHtml(t.id)}">${escapeHtml(t.name)}</option>`).join('');
          csCaseType.disabled = false;
        })
        .catch(err => showAlert(el('csError'), 'Failed to load case types: ' + err.message));
    }
  });

  el('csSearchBtn').addEventListener('click', function () {
    hideAlert(el('csError'));
    el('csResultsWrap').style.display = 'none';

    if (!csCtx) { showAlert(el('csError'), 'Select a state, district and court complex first.'); return; }
    const caseType = csCaseType.value;
    const number = el('csNumber').value.trim();
    const year = el('csYear').value.trim();
    if (!caseType) { showAlert(el('csError'), 'Select a case type.'); return; }
    if (!number) { showAlert(el('csError'), 'Enter a case number.'); return; }
    if (!/^\d{4}$/.test(year)) { showAlert(el('csError'), 'Year must be a 4-digit year.'); return; }

    setLoading(this, true);
    CourtMeta.searchByCaseNumber({
      state_code: csCtx.state_code,
      dist_code: csCtx.dist_code,
      courts: csCtx.court_code,
      case_type: caseType,
      number,
      year
    })
      .then(data => renderSearchResults('cs', data))
      .catch(err => showAlert(el('csError'), err.message))
      .finally(() => setLoading(this, false));
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // TAB 4 – Filing Number Search
  // ═══════════════════════════════════════════════════════════════════════════
  let fsCtx = null;

  wireCascade({
    stateId: 'fsState',
    districtId: 'fsDistrict',
    complexId: 'fsComplex',
    onComplexSelected: (ctx) => { fsCtx = ctx; }
  });

  el('fsSearchBtn').addEventListener('click', function () {
    hideAlert(el('fsError'));
    el('fsResultsWrap').style.display = 'none';

    if (!fsCtx) { showAlert(el('fsError'), 'Select a state, district and court complex first.'); return; }
    const filingNumber = el('fsFiling').value.trim();
    const year = el('fsYear').value.trim();
    if (!filingNumber) { showAlert(el('fsError'), 'Enter a filing number.'); return; }
    if (!/^\d{4}$/.test(year)) { showAlert(el('fsError'), 'Year must be a 4-digit year.'); return; }

    setLoading(this, true);
    CourtMeta.searchByFilingNumber({
      state_code: fsCtx.state_code,
      dist_code: fsCtx.dist_code,
      courts: fsCtx.court_code,
      filingNumber,
      year
    })
      .then(data => renderSearchResults('fs', data))
      .catch(err => showAlert(el('fsError'), err.message))
      .finally(() => setLoading(this, false));
  });

  // Renders a flat result list from /search/case-number or /search/filing-number.
  // `prefix` is 'cs' or 'fs' — the IDs follow the same pattern.
  function renderSearchResults(prefix, data) {
    const wrap = el(prefix + 'ResultsWrap');
    const body = el(prefix + 'ResultsBody');
    const header = el(prefix + 'ResultsHeader');
    const rows = (data && data.results) || [];

    if (!rows.length) {
      header.textContent = '0 cases found';
      body.innerHTML = '<tr><td colspan="8" style="text-align:center; color:#718096; padding:20px;">No cases match this search.</td></tr>';
      wrap.style.display = 'block';
      return;
    }

    header.textContent = `${rows.length} case${rows.length === 1 ? '' : 's'} found`;
    body.innerHTML = rows.map((r, i) => {
      const cino = r.cino || '';
      const caseNo = r.case_no || r.case_no2 || '–';
      const type = r.type_name || '–';
      const year = r.reg_year || '–';
      const parties = r.petnameadArr || '–';
      const court = r.establishment_name || r.court_code || '–';
      const action = cino
        ? `<button class="btn btn-secondary" data-cino="${escapeHtml(cino)}" data-action="bundle">Open</button>`
        : '<span style="color:#a0aec0; font-size:12px;">no CNR</span>';

      return `<tr>
        <td>${i + 1}</td>
        <td><code style="font-size:12px;">${escapeHtml(cino || '–')}</code></td>
        <td>${escapeHtml(caseNo)}</td>
        <td>${escapeHtml(type)}</td>
        <td>${escapeHtml(year)}</td>
        <td>${escapeHtml(parties)}</td>
        <td>${escapeHtml(court)}</td>
        <td>${action}</td>
      </tr>`;
    }).join('');

    body.querySelectorAll('button[data-action="bundle"]').forEach(btn => {
      btn.addEventListener('click', () => jumpToCnr(btn.dataset.cino));
    });
    wrap.style.display = 'block';
  }

  // ═══════════════════════════════════════════════════════════════════════════
  // TAB 5 – Cause List
  // ═══════════════════════════════════════════════════════════════════════════
  let clCtx = null;
  const clCourtNo = el('clCourtNo');

  wireCascade({
    stateId: 'clState',
    districtId: 'clDistrict',
    complexId: 'clComplex',
    onComplexSelected: (ctx) => {
      clCtx = ctx;
      clCourtNo.innerHTML = '<option value="">— Loading court rooms… —</option>';
      clCourtNo.disabled = true;
      if (!ctx) {
        clCourtNo.innerHTML = '<option value="">— Select complex first —</option>';
        return;
      }
      CourtMeta.fetchCourts(ctx.state_code, ctx.dist_code, ctx.court_code)
        .then(data => {
          const courts = (data.courts || []).filter(c => !c.isHeader);
          if (!courts.length) {
            clCourtNo.innerHTML = '<option value="">— No courts in this complex —</option>';
            return;
          }
          clCourtNo.innerHTML = '<option value="">— Select Court Room —</option>' +
            courts.map(c => `<option value="${escapeHtml(c.code)}">${escapeHtml(c.name)}</option>`).join('');
          clCourtNo.disabled = false;
        })
        .catch(err => showAlert(el('clError'), 'Failed to load court rooms: ' + err.message));
    }
  });

  // Default the date input to today (dd-mm-yyyy).
  (function () {
    const now = new Date();
    const dd = String(now.getDate()).padStart(2, '0');
    const mm = String(now.getMonth() + 1).padStart(2, '0');
    el('clDate').value = `${dd}-${mm}-${now.getFullYear()}`;
  })();

  el('clSearchBtn').addEventListener('click', function () {
    hideAlert(el('clError'));
    el('clResultsWrap').style.display = 'none';

    if (!clCtx) { showAlert(el('clError'), 'Select a state, district and court complex first.'); return; }
    const courtNo = clCourtNo.value;
    const date = el('clDate').value.trim();
    const flag = el('clFlag').value;
    if (!courtNo) { showAlert(el('clError'), 'Select a court room.'); return; }
    if (!date) { showAlert(el('clError'), 'Enter a date.'); return; }

    setLoading(this, true);
    CourtMeta.causeList({
      state_code: clCtx.state_code,
      dist_code: clCtx.dist_code,
      court_code: clCtx.court_code,
      court_no: courtNo,
      date,
      flag
    })
      .then(data => renderCauseList(data))
      .catch(err => showAlert(el('clError'), err.message))
      .finally(() => setLoading(this, false));
  });

  // ═══════════════════════════════════════════════════════════════════════════
  // TAB 6 – Advocate Search (Workflow B)
  // ═══════════════════════════════════════════════════════════════════════════
  let advCtx = null;       // { state_code, dist_code, court_code } once complex is selected
  const advScope = el('advScope');
  const advMode  = el('advMode');
  const advCourtRow   = el('advCourtRow');
  const advNameRow    = el('advNameRow');
  const advBarcodeRow = el('advBarcodeRow');

  // Reuse the cascade helper; this populates the district + complex selects
  // for scope=court. For scope=state we hide the cascade row entirely.
  wireCascade({
    stateId: 'advState',
    districtId: 'advDistrict',
    complexId: 'advComplex',
    onComplexSelected: (ctx) => { advCtx = ctx; }
  });

  function syncAdvFormVisibility() {
    advCourtRow.style.display   = advScope.value === 'court' ? 'grid' : 'none';
    advNameRow.style.display    = advMode.value === 'name'   ? 'grid' : 'none';
    advBarcodeRow.style.display = advMode.value === 'barcode' ? 'grid' : 'none';
  }
  advScope.addEventListener('change', syncAdvFormVisibility);
  advMode .addEventListener('change', syncAdvFormVisibility);
  syncAdvFormVisibility();

  el('advSearchBtn').addEventListener('click', function () {
    hideAlert(el('advError'));
    el('advResultsWrap').style.display = 'none';
    el('advProgress').style.display = 'none';
    el('advResultsBody').innerHTML = '';

    const stateCode = el('advState').value;
    const scope = advScope.value;
    const mode  = advMode.value;
    if (!stateCode) { showAlert(el('advError'), 'Select a state.'); return; }

    const params = {
      scope,
      state_code: stateCode,
      mode,
      pendingDisposed: el('advPending').value
    };
    if (mode === 'name') {
      params.advocateName = el('advName').value.trim();
      if (!params.advocateName) { showAlert(el('advError'), 'Enter an advocate name.'); return; }
    } else {
      params.barstatecode = el('advBarState').value.trim();
      params.barcode      = el('advBarCode').value.trim();
      if (!params.barcode) { showAlert(el('advError'), 'Enter a bar code.'); return; }
    }

    if (scope === 'court') {
      if (!advCtx) { showAlert(el('advError'), 'Select a district and court complex first.'); return; }
      params.dist_code = advCtx.dist_code;
      params.courts    = advCtx.court_code;
    }

    setLoading(this, true);
    const aggregated = [];
    const seenCinos = new Set();
    const progressEl = el('advProgress');

    if (scope === 'state') {
      progressEl.style.display = 'block';
      progressEl.textContent = 'Starting state-wide search…';
    }

    params.onProgress = function (event) {
      if (event.type === 'start') {
        progressEl.textContent = `Searching ${event.totalDistricts} districts in this state…`;
        return;
      }
      if (event.type === 'complete') {
        progressEl.textContent = `Done — ${aggregated.length} cases across ${event.completedDistricts || ''} districts.`;
        return;
      }
      if (event.type === 'progress') {
        const districtRows = (event.rows || []);
        for (const row of districtRows) {
          const cino = row.cino;
          if (cino && !seenCinos.has(cino)) {
            seenCinos.add(cino);
            aggregated.push(row);
          } else if (!cino) {
            aggregated.push(row);
          }
        }
        progressEl.textContent =
          `${event.completedDistricts || 0} / ${event.totalDistricts || '?'} districts — ${aggregated.length} cases so far`;
        renderAdvocateResults(aggregated);
        if (event.error) {
          // Per-district errors are non-fatal; surface but keep going.
          console.warn('[CourtMeta] district error:', event.districtCode, event.error);
        }
      }
    };

    const promise = scope === 'state'
      ? CourtMeta.searchByAdvocate(params)
      : CourtMeta.searchByAdvocate(params).then(data => {
          (data.results || []).forEach(r => aggregated.push(r));
          renderAdvocateResults(aggregated);
        });

    promise
      .catch(err => showAlert(el('advError'), err.message))
      .finally(() => setLoading(this, false));
  });

  function renderAdvocateResults(rows) {
    const wrap = el('advResultsWrap');
    const body = el('advResultsBody');
    const header = el('advResultsHeader');

    if (!rows.length) {
      header.textContent = '0 cases found';
      body.innerHTML = '<tr><td colspan="7" style="text-align:center; color:#718096; padding:20px;">No cases match this search yet.</td></tr>';
      wrap.style.display = 'block';
      return;
    }

    header.textContent = `${rows.length} case${rows.length === 1 ? '' : 's'} found`;
    body.innerHTML = rows.map((r, i) => {
      const cino = r.cino || '';
      const type = r.type_name || '–';
      const year = r.reg_year || '–';
      const parties = r.petnameadArr || '–';
      const court = r.establishment_name || r.court_code || '–';
      const action = cino
        ? `<button class="btn btn-secondary" data-cino="${escapeHtml(cino)}" data-action="bundle">Open</button>`
        : '<span style="color:#a0aec0; font-size:12px;">no CNR</span>';
      return `<tr>
        <td>${i + 1}</td>
        <td><code style="font-size:12px;">${escapeHtml(cino || '–')}</code></td>
        <td>${escapeHtml(type)}</td>
        <td>${escapeHtml(year)}</td>
        <td>${escapeHtml(parties)}</td>
        <td>${escapeHtml(court)}</td>
        <td>${action}</td>
      </tr>`;
    }).join('');

    body.querySelectorAll('button[data-action="bundle"]').forEach(btn => {
      btn.addEventListener('click', () => jumpToCnr(btn.dataset.cino));
    });
    wrap.style.display = 'block';
  }

  function renderCauseList(data) {
    const wrap = el('clResultsWrap');
    const body = el('clResultsBody');
    const header = el('clResultsHeader');
    const rows = ((data && data.rows) || []).filter(r => !r.isHeader);

    if (!rows.length) {
      header.textContent = '0 cases listed';
      body.innerHTML = '<tr><td colspan="4" style="text-align:center; color:#718096; padding:20px;">No cases on the cause list for this date.</td></tr>';
      wrap.style.display = 'block';
      return;
    }

    header.textContent = `${rows.length} case${rows.length === 1 ? '' : 's'} on the cause list`;
    body.innerHTML = rows.map((r, i) => {
      const cino = r.cino || '';
      // Cells already stripped of HTML by the API parser; collapse to a single description column.
      const details = (r.cells || []).join(' • ');
      const action = cino
        ? `<button class="btn btn-secondary" data-cino="${escapeHtml(cino)}" data-action="bundle">Open</button>`
        : '<span style="color:#a0aec0; font-size:12px;">no CNR</span>';
      return `<tr>
        <td>${i + 1}</td>
        <td><code style="font-size:12px;">${escapeHtml(cino || '–')}</code></td>
        <td>${escapeHtml(details)}</td>
        <td>${action}</td>
      </tr>`;
    }).join('');

    body.querySelectorAll('button[data-action="bundle"]').forEach(btn => {
      btn.addEventListener('click', () => jumpToCnr(btn.dataset.cino));
    });
    wrap.style.display = 'block';
  }

})();
