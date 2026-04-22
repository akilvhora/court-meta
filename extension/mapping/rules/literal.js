export default {
  name: 'literal',
  evaluate(spec) {
    return spec.value === undefined ? null : spec.value;
  }
};
