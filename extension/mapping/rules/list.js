// Iterates an array source and applies a sub-mapping to each item.
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
        out[key] = evaluateSpec(sub, row, ctx);
        ctx.path.pop();
      }
      return out;
    });
  }
};
