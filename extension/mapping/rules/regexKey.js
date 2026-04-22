import { findKeyByRegex } from '../resolvers.js';

// Picks a value from `node` by the first key whose name matches `pattern`.
// If nothing matches and `fallbackIndex` is given, returns the value at that
// positional index in Object.keys(node).
export default {
  name: 'regexKey',
  evaluate(spec, node) {
    if (!node || typeof node !== 'object') return null;
    const found = findKeyByRegex(node, spec.pattern, { caseSensitive: spec.caseSensitive });
    if (found) return found.value == null ? null : found.value;
    if (typeof spec.fallbackIndex === 'number') {
      const keys = Object.keys(node);
      const k = keys[spec.fallbackIndex];
      return k != null ? node[k] ?? null : null;
    }
    return null;
  }
};
