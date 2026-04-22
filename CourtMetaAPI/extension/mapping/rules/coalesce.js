// Returns the first non-null, non-empty-string value from `sources`.
export default {
  name: 'coalesce',
  evaluate(spec, node, ctx, evaluateSpec) {
    for (const s of spec.sources || []) {
      const v = evaluateSpec(s, node, ctx);
      if (v != null && v !== '') return v;
    }
    return null;
  }
};
