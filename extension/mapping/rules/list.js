import { setValueAtPath } from '../resolvers.js';

// Iterates an array source and applies a sub-mapping to each item.
// Item keys containing "." nest into sub-objects (same convention as top-level fields).
export default {
  name: 'list',
  evaluate(spec, node, ctx, evaluateSpec) {
    const arr = spec.source ? evaluateSpec(spec.source, node, ctx) : node;
    if (!Array.isArray(arr)) return [];
    const item = spec.item || {};
    return arr.map((row, i) => {
      const out = {};
      for (const [key, sub] of Object.entries(item)) {
        ctx.path.push(`[${i}].${key}`);
        const value = evaluateSpec(sub, row, ctx);
        if (key.includes('.')) setValueAtPath(out, key, value);
        else out[key] = value;
        ctx.path.pop();
      }
      return out;
    });
  }
};
