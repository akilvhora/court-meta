// Expands ${name} placeholders from an object of named sub-sources.
// Missing values are substituted as empty strings.
export default {
  name: 'template',
  evaluate(spec, node, ctx, evaluateSpec) {
    const tpl = spec.template || '';
    const sources = spec.sources || {};
    const resolved = {};
    for (const [k, s] of Object.entries(sources)) {
      resolved[k] = evaluateSpec(s, node, ctx);
    }
    return tpl.replace(/\$\{(\w+)\}/g, (_, k) => {
      const v = resolved[k];
      return v == null ? '' : String(v);
    });
  }
};
