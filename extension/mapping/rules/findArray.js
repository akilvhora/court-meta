// Walks the input tree and returns the first array of row-like objects whose
// container key matches `keyPattern`. If no keyed match is found, returns the
// first array whose rows have at least one key matching `rowKeyPattern`.
export default {
  name: 'findArray',
  evaluate(spec, node) {
    const keyRe = spec.keyPattern ? new RegExp(spec.keyPattern, 'i') : null;
    const rowRe = spec.rowKeyPattern ? new RegExp(spec.rowKeyPattern, 'i') : null;
    const maxDepth = spec.maxDepth ?? 4;

    function walk(obj, depth) {
      if (!obj || typeof obj !== 'object' || depth > maxDepth) return null;
      if (!Array.isArray(obj)) {
        for (const [k, v] of Object.entries(obj)) {
          if (keyRe && keyRe.test(k) && Array.isArray(v) && v.length && typeof v[0] === 'object') {
            return v;
          }
        }
      }
      if (Array.isArray(obj) && obj.length && typeof obj[0] === 'object') {
        if (!rowRe || Object.keys(obj[0]).some((k) => rowRe.test(k))) return obj;
      }
      for (const v of Object.values(obj)) {
        const found = walk(v, depth + 1);
        if (found) return found;
      }
      return null;
    }

    return walk(node, 0) || [];
  }
};
