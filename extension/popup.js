// Court Meta - Popup Script

const API_BASE = 'http://localhost:5000/api/court';

const apiDot = document.getElementById('apiDot');
const apiStatus = document.getElementById('apiStatus');
const searchBtn = document.getElementById('searchBtn');
const cnrInput = document.getElementById('cnrInput');
const resultDiv = document.getElementById('result');

function setStatus(connected, message) {
  apiDot.className = 'dot ' + (connected ? 'green' : 'red');
  apiStatus.textContent = message;
  searchBtn.disabled = !connected;
}

function showResult(success, message) {
  resultDiv.className = success ? 'success' : 'error';
  resultDiv.textContent = message;
}

const EXT_HEADERS = { 'Accept': 'application/json', 'X-Court-Meta-Client': 'extension' };

// Check if C# backend is reachable
fetch(`${API_BASE}/states`, { headers: EXT_HEADERS })
  .then((res) => {
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  })
  .then((data) => {
    const count = data.states ? data.states.length : 0;
    setStatus(true, `Backend connected (${count} states loaded)`);
  })
  .catch((err) => {
    setStatus(false, 'Backend offline. Start CourtMetaAPI first.');
  });

// CNR Search
searchBtn.addEventListener('click', function () {
  const cino = cnrInput.value.trim();
  if (!cino) {
    showResult(false, 'Please enter a CNR number.');
    return;
  }
  if (cino.length < 16) {
    showResult(false, 'CNR number must be at least 16 characters.');
    return;
  }

  searchBtn.disabled = true;
  searchBtn.textContent = 'Searching...';
  resultDiv.className = '';
  resultDiv.style.display = 'none';

  fetch(`${API_BASE}/cnr?cino=${encodeURIComponent(cino)}`, { headers: EXT_HEADERS })
    .then((res) => res.json())
    .then((data) => {
      if (data.success) {
        const info = data.data;
        let text = `Case found!\n`;
        if (info.caseNumber) text += `Case No: ${info.caseNumber}\n`;
        if (info.type) text += `Type: ${info.type}`;
        showResult(true, text);
      } else {
        showResult(false, data.error || 'Case not found.');
      }
    })
    .catch((err) => {
      showResult(false, 'Error: ' + err.message);
    })
    .finally(() => {
      searchBtn.disabled = false;
      searchBtn.textContent = 'Search';
    });
});

cnrInput.addEventListener('keydown', function (e) {
  if (e.key === 'Enter') searchBtn.click();
});
