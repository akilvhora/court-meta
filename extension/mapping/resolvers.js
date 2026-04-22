// Path and key resolvers used by rule modules.

// Walks a dotted path like "data.cino" or "items.0.name".
// Returns undefined if any segment is missing.
export function getValueAtPath(obj, path) {
  if (obj == null || !path) return obj;
  const parts = String(path).split('.').filter(Boolean);
  let cur = obj;
  for (const p of parts) {
    if (cur == null) return undefined;
    cur = cur[p];
  }
  return cur;
}

// First own-key of `node` matching the regex pattern.
// Returns { key, value } or null.
export function findKeyByRegex(node, pattern, { caseSensitive = false } = {}) {
  if (!node || typeof node !== 'object') return null;
  const re = pattern instanceof RegExp ? pattern : new RegExp(pattern, caseSensitive ? '' : 'i');
  for (const key of Object.keys(node)) {
    if (re.test(key)) return { key, value: node[key] };
  }
  return null;
}
