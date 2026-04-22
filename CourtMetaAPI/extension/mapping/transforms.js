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

// Normalise eCourts dates to DD-MM-YYYY.
// Accepts DD-MM-YYYY, DD-MM-YY, D-M-YY(YY), with '/', '-', or '.' separators.
// Two-digit years pivot at 50: <50 -> 20YY, else 19YY.
function normalizeDate(v) {
  if (v == null) return null;
  const s = String(v).trim();
  if (!s) return null;
  const m = s.match(/^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2}|\d{4})$/);
  if (!m) return s;
  const [, dd, mm, yy] = m;
  const year = yy.length === 4 ? yy : Number(yy) < 50 ? `20${yy}` : `19${yy}`;
  const pad2 = (x) => (x.length === 1 ? `0${x}` : x);
  return `${pad2(dd)}-${pad2(mm)}-${year}`;
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
