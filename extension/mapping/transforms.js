// Pure transforms. Each entry is (value, opts?) -> value.
// Steps can be either a string ("trim") or an object ({ fn: "toDate", ... }).

const TRANSFORMS = {
  trim:        (v) => (typeof v === 'string' ? v.trim() : v),
  upper:       (v) => (typeof v === 'string' ? v.toUpperCase() : v),
  lower:       (v) => (typeof v === 'string' ? v.toLowerCase() : v),
  nullIfEmpty: (v) => (v === '' || v === undefined ? null : v),
  default:     (v, opts) => (v == null ? opts.value : v),
  toDate:      (v, opts) => normalizeDate(v, opts),
  parseAdvocateList: (v) => parseAdvocateList(v),
  parseActTable:     (v) => parseActTable(v)
};

// Extracts the text of the last <td> in an HTML table and comma-splits it.
// data.history.act arrives as an HTML <table>; the act/section names sit in
// the final cell as a comma-separated list. Returns [] when the input is
// missing, contains no <td>, or the last cell is empty.
function parseActTable(v) {
  if (v == null) return [];
  const html = String(v);
  const cells = [...html.matchAll(/<td\b[^>]*>([\s\S]*?)<\/td>/gi)];
  if (!cells.length) return [];
  const text = cells[cells.length - 1][1]
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/\s+/g, ' ')
    .trim();
  if (!text) return [];
  return text.split(',').map((s) => s.trim()).filter(Boolean);
}

// Parses eCourts str_error / str_error1 strings into [{ name, advocateName }].
// Input is HTML-laced text shaped like:
//   "1) Petitioner One Advocate - Adv One <br>2) Petitioner Two Advocate - Adv Two"
// Item boundaries are 1), 2), 3) ...; within each item the name precedes
// "Advocate -" and the advocate name follows it. Entries missing an
// "Advocate -" separator still produce a row with advocateName=null.
function parseAdvocateList(v) {
  if (v == null) return [];
  const cleaned = String(v)
    .replace(/<\s*br\s*\/?\s*>/gi, ' ')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/\s+/g, ' ')
    .trim();
  if (!cleaned) return [];

  const parts = cleaned.split(/\s*\d+\)\s*/).filter((s) => s && s.trim());
  const out = [];
  for (const part of parts) {
    const idx = part.search(/Advocate\s*-/i);
    let name, advocateName;
    if (idx === -1) {
      name = part.trim();
      advocateName = null;
    } else {
      name = part.slice(0, idx).trim();
      advocateName = part.slice(idx).replace(/^Advocate\s*-\s*/i, '').trim();
    }
    if (!name && !advocateName) continue;
    out.push({
      name: name || null,
      advocateName: advocateName || null
    });
  }
  return out;
}

// Normalise eCourts dates to DD/MM/YYYY (or DD/MM/YY when the input year is 2-digit).
// Accepts DD-MM-YYYY, DD-MM-YY, D-M-YY(YY), and ISO YYYY-MM-DD,
// with '/', '-', or '.' separators.
function normalizeDate(v) {
  if (v == null) return null;
  const s = String(v).trim();
  if (!s) return null;
  const pad2 = (x) => (x.length === 1 ? `0${x}` : x);

  const iso = s.match(/^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$/);
  if (iso) {
    const [, yyyy, mm, dd] = iso;
    return `${pad2(dd)}/${pad2(mm)}/${yyyy}`;
  }

  const m = s.match(/^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2}|\d{4})$/);
  if (!m) return s;
  const [, dd, mm, yy] = m;
  return `${pad2(dd)}/${pad2(mm)}/${yy}`;
}

export function applyTransforms(steps, value) {
  let cur = value;
  for (const step of steps || []) {
    const name = typeof step === 'string' ? step : step.fn;
    const fn = TRANSFORMS[name];
    if (!fn) continue;
    cur = fn(cur, typeof step === 'object' ? step : {});
  }
  return cur;
}
