// Concatenates the evaluated values of `sources` with `separator`.
// By default, null/empty values are skipped.
export default {
  name: 'combine',
  evaluate(spec, node, ctx, evaluateSpec) {
    const sources = spec.sources || [];
    const sep = spec.separator ?? ' ';
    const skipEmpty = spec.skipEmpty !== false;
    const values = sources.map((s) => evaluateSpec(s, node, ctx));
    const filtered = skipEmpty ? values.filter((v) => v != null && v !== '') : values;
    if (!filtered.length) return null;
    return filtered.join(sep);
  }
};
