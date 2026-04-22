// Court Meta - Declarative mapping engine.
// applyMapping(config, input) -> { result, diagnostics }
//
// Engine is pure, deterministic, zero-dependency. Rules are pluggable modules
// registered in ./rules/index.js. See cnrMapping.json for an example config.

import { getValueAtPath } from './resolvers.js';
import { applyTransforms } from './transforms.js';
import { rules as defaultRules } from './rules/index.js';

const SUPPORTED_MAJOR = 1;

export function applyMapping(config, input, { rules = defaultRules } = {}) {
  if (!config || typeof config !== 'object') {
    throw new Error('applyMapping: config is required');
  }
  const major = Number(String(config.version || '1.0.0').split('.')[0]);
  if (major !== SUPPORTED_MAJOR) {
    throw new Error(`applyMapping: unsupported config major version ${major} (engine supports ${SUPPORTED_MAJOR})`);
  }

  const root = config.root ? getValueAtPath(input, config.root) : input;
  const ctx = {
    input,
    root,
    rules,
    diagnostics: { missing: [], fallback: [] },
    path: []
  };

  const result = {};
  for (const [key, spec] of Object.entries(config.fields || {})) {
    ctx.path = [key];
    result[key] = evaluateSpec(spec, root, ctx);
  }
  return { result, diagnostics: ctx.diagnostics };
}

export function evaluateSpec(spec, node, ctx) {
  if (spec == null) return null;
  const rule = ctx.rules[spec.rule];
  if (!rule) {
    ctx.diagnostics.missing.push({
      path: ctx.path.join('.'),
      reason: `unknown rule "${spec.rule}"`
    });
    return applyFallbacks(null, spec);
  }

  let value;
  try {
    value = rule.evaluate(spec, node, ctx, evaluateSpec);
  } catch (err) {
    ctx.diagnostics.missing.push({
      path: ctx.path.join('.'),
      reason: err.message
    });
    value = null;
  }
  return applyFallbacks(value, spec, ctx);
}

function applyFallbacks(value, spec, ctx) {
  if (value == null && spec.default !== undefined) {
    if (ctx) ctx.diagnostics.fallback.push({ path: ctx.path.join('.'), used: 'default' });
    value = spec.default;
  }
  if (spec.transforms && spec.transforms.length) {
    value = applyTransforms(spec.transforms, value);
  }
  return value === undefined ? null : value;
}
