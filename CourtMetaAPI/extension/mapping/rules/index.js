import exact from './exact.js';
import regexKey from './regexKey.js';
import findArray from './findArray.js';
import list from './list.js';
import switchRule from './switch.js';
import literal from './literal.js';
import combine from './combine.js';
import coalesce from './coalesce.js';
import template from './template.js';

function toMap(rules) {
  const map = {};
  for (const r of rules) map[r.name] = r;
  return map;
}

// Registry: add a new rule = import it here.
export const rules = toMap([
  exact,
  regexKey,
  findArray,
  list,
  switchRule,
  literal,
  combine,
  coalesce,
  template
]);
