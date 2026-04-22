import { getValueAtPath } from '../resolvers.js';

export default {
  name: 'exact',
  evaluate(spec, node) {
    const v = getValueAtPath(node, spec.path);
    return v == null ? null : v;
  }
};
