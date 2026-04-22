import { getValueAtPath } from '../resolvers.js';

// Looks up `path` against `cases`. Returns null on miss; the engine's
// spec-level `default` fills in when the result is null.
export default {
  name: 'switch',
  evaluate(spec, node) {
    const v = getValueAtPath(node, spec.path);
    const cases = spec.cases || {};
    if (v != null && Object.prototype.hasOwnProperty.call(cases, v)) {
      return cases[v];
    }
    return null;
  }
};
