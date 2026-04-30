// Pure transforms. Each entry is (value, opts?) -> value.
// Steps can be either a string ("trim") or an object ({ fn: "toDate", ... }).

const TRANSFORMS = {
  trim:        (v) => (typeof v === 'string' ? v.trim() : v),
  upper:       (v) => (typeof v === 'string' ? v.toUpperCase() : v),
  lower:       (v) => (typeof v === 'string' ? v.toLowerCase() : v),
  nullIfEmpty: (v) => (v === '' || v === undefined ? null : v),
  default:     (v, opts) => (v == null ? opts.value : v),
  toDate:      (v, opts) => normalizeDate(v, opts)
};

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
