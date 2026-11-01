export interface ModelPricing {
  inputPricePerMillionTokens: number;
  outputPricePerMillionTokens: number;
}

export interface ModelRow {
  id: string;
  url: string;
  aliases: string[];
  state: string;
  modelType: string | null;
  publicAccess: boolean;
  hasUpstreamCredential: boolean;
  pricing: ModelPricing | null;
  upstreamAuth: unknown;
  capabilities: unknown[];
  maxContextLength: number;
  backends: ModelBackend[];
  /** Who serves the model: `self-hosted`, or a paid provider's id. */
  provider: string;
}

/** One upstream of a model. A model registered with a single URL has one, named "default". */
export interface ModelBackend {
  name: string;
  url: string;
  maxContextLength: number;
  state: string;
  /** `standby` takes traffic only when no primary backend can. */
  priority?: string;
  /** The model name sent to this backend when it is not the model's id. */
  upstreamModel?: string | null;
}

export const SELF_HOSTED = 'self-hosted';

const PROVIDER_NAMES: Record<string, string> = {
  openrouter: 'OpenRouter',
  openai: 'OpenAI',
  deepseek: 'DeepSeek',
  deepgram: 'Deepgram',
  elevenlabs: 'ElevenLabs',
};

export const isPaidProvider = (provider: string | null | undefined) => !!provider && provider !== SELF_HOSTED;

export const providerName = (provider: string) => PROVIDER_NAMES[provider] ?? provider;

export interface ModelTypeDescriptor {
  value: string;
  label: string;
  testEndpoint: string | null;
  aliases: string[];
}

export interface ModelEditDraft {
  id: string;
  url: string;
  maxContextLength: number;
  aliasesText: string;
  apiKey: string;
  clearApiKey: boolean;
  hasUpstreamCredential: boolean;
  publicAccess: boolean;
  upstreamAuth: unknown;
  capabilities: unknown[];
  modelType: string;
  inputPricePerMillion: string | number;
  outputPricePerMillion: string | number;
  _hadPricing: boolean;
  _existing: boolean;
  _originalId: string;
  /** The model's backends as last loaded; with more than one, the URL fields describe none of them. */
  _backends: ModelBackend[];
}

export interface ModelWriteBody {
  model: {
    id: string;
    url: string;
    maxContextLength: number;
    aliases: string[];
    publicAccess: boolean;
    modelType: string | null;
    capabilities: unknown[];
    upstreamAuth?: unknown;
  };
  apiKey: string | null;
  clearApiKey: boolean;
  pricing: ModelPricing | null;
  clearPricing: boolean;
}

export interface ModelTestResult {
  ok: boolean;
  modelId?: string;
  modelType?: string;
  endpoint?: string;
  supported?: boolean;
  latencyMs?: number;
  statusCode?: number;
  detail?: string;
  content?: string;
  hint?: string;
}

export const DEFAULT_MODEL_DRAFT: ModelEditDraft = {
  id: '',
  url: '',
  maxContextLength: 8192,
  aliasesText: '',
  apiKey: '',
  clearApiKey: false,
  hasUpstreamCredential: false,
  publicAccess: false,
  upstreamAuth: null,
  capabilities: [],
  modelType: 'text-generation',
  inputPricePerMillion: '',
  outputPricePerMillion: '',
  _hadPricing: false,
  _existing: false,
  _originalId: '',
  _backends: [],
};

export function normalizeModelItem(raw: Record<string, unknown>): ModelRow {
  const nested = (raw.model as Record<string, unknown> | undefined) ?? raw;
  const pricingRaw = (raw.pricing ?? nested.pricing) as ModelPricing | null | undefined;
  return {
    id: String(nested.id ?? ''),
    url: String(nested.url ?? ''),
    aliases: Array.isArray(nested.aliases) ? nested.aliases.map(String) : [],
    state: String(nested.state ?? raw.state ?? 'serving'),
    modelType: nested.modelType != null ? String(nested.modelType) : null,
    publicAccess: !!(nested.publicAccess ?? raw.publicAccess),
    hasUpstreamCredential: !!(raw.hasUpstreamCredential ?? nested.hasUpstreamCredential),
    pricing: pricingRaw
      ? {
          inputPricePerMillionTokens: Number(pricingRaw.inputPricePerMillionTokens ?? 0),
          outputPricePerMillionTokens: Number(pricingRaw.outputPricePerMillionTokens ?? 0),
        }
      : null,
    upstreamAuth: nested.upstreamAuth ?? null,
    capabilities: Array.isArray(nested.capabilities) ? nested.capabilities : [],
    maxContextLength: Number(nested.maxContextLength ?? 8192),
    backends: Array.isArray(nested.backends)
      ? (nested.backends as Record<string, unknown>[]).map((b) => ({
          name: String(b.name ?? 'default'),
          url: String(b.url ?? ''),
          maxContextLength: Number(b.maxContextLength ?? 0),
          state: String(b.state ?? 'serving'),
          priority: String(b.priority ?? 'primary'),
          upstreamModel: b.upstreamModel ? String(b.upstreamModel) : null,
        }))
      : [],
    provider: String(raw.provider ?? nested.provider ?? SELF_HOSTED),
  };
}

/**
 * True when the model has several backends. Its URL, context length and credential then belong to
 * the backends, each changed on its own, and the drawer edits only what belongs to the model.
 */
export function hasSeveralBackends(draft: ModelEditDraft): boolean {
  return draft._existing && draft._backends.length > 1;
}

export function resolveModelType(model: ModelRow, types: ModelTypeDescriptor[]): string {
  if (model.modelType) return model.modelType;
  const id = model.id.toLowerCase();
  for (const t of types) {
    if (t.value.toLowerCase() === id) return t.value;
    if (t.aliases.some((a) => a.toLowerCase() === id)) return t.value;
  }
  return 'text-generation';
}

export function draftFromModel(model: ModelRow, types: ModelTypeDescriptor[]): ModelEditDraft {
  return {
    id: model.id,
    url: model.url,
    maxContextLength: model.maxContextLength,
    aliasesText: model.aliases.join(', '),
    apiKey: '',
    clearApiKey: false,
    hasUpstreamCredential: model.hasUpstreamCredential,
    publicAccess: model.publicAccess,
    upstreamAuth: model.upstreamAuth,
    capabilities: model.capabilities,
    modelType: resolveModelType(model, types),
    inputPricePerMillion: model.pricing?.inputPricePerMillionTokens ?? '',
    outputPricePerMillion: model.pricing?.outputPricePerMillionTokens ?? '',
    _hadPricing: !!model.pricing,
    _existing: true,
    _originalId: model.id,
    _backends: model.backends,
  };
}

function pricingFilled(v: string | number | null | undefined): boolean {
  return v !== '' && v !== null && v !== undefined;
}

export function modelPricingError(draft: ModelEditDraft): string {
  const input = draft.inputPricePerMillion;
  const output = draft.outputPricePerMillion;
  const inputFilled = pricingFilled(input);
  const outputFilled = pricingFilled(output);
  if (inputFilled !== outputFilled) {
    return 'Set both input and output prices, or leave both blank to leave the model unpriced.';
  }
  if (!inputFilled) return '';
  for (const v of [input, output]) {
    const n = Number(v);
    if (!Number.isFinite(n) || n < 0) return 'Prices must be zero or greater.';
  }
  return '';
}

export function buildModelWriteBody(draft: ModelEditDraft): ModelWriteBody {
  const aliases = (draft.aliasesText || '')
    .split(',')
    .map((s) => s.trim())
    .filter(Boolean);
  const model: ModelWriteBody['model'] = {
    id: draft.id.trim(),
    url: draft.url.trim(),
    maxContextLength: Number(draft.maxContextLength) || 8192,
    aliases,
    publicAccess: !!draft.publicAccess,
    modelType: draft.modelType || null,
    capabilities: draft.capabilities || [],
  };
  if (
    draft._existing &&
    draft.upstreamAuth &&
    !(draft.apiKey || '').trim() &&
    !draft.clearApiKey
  ) {
    model.upstreamAuth = draft.upstreamAuth;
  }
  // The gateway refuses a credential sent for a model with several backends: it would not know
  // which one it is for.
  const apiKey = hasSeveralBackends(draft) ? '' : (draft.apiKey || '').trim();
  const hasPricing =
    pricingFilled(draft.inputPricePerMillion) && pricingFilled(draft.outputPricePerMillion);
  return {
    model,
    apiKey: apiKey || null,
    clearApiKey: !hasSeveralBackends(draft) && !!draft.clearApiKey,
    pricing: hasPricing
      ? {
          inputPricePerMillionTokens: Number(draft.inputPricePerMillion),
          outputPricePerMillionTokens: Number(draft.outputPricePerMillion),
        }
      : null,
    clearPricing: !hasPricing && !!draft._hadPricing,
  };
}

export function validateModelDraft(draft: ModelEditDraft): string {
  const write = buildModelWriteBody(draft);
  // A model whose backends are managed one by one (several, or none left) is saved without a URL.
  const urlOptional = draft._existing && draft._backends.length !== 1;
  if (!write.model.id || (!write.model.url && !urlOptional)) return 'Model name and upstream URL are required.';
  if (/localhost|127\.0\.0\.1/i.test(write.model.url)) {
    return 'Use http://host.docker.internal:<port> when the gateway runs in Docker (not localhost).';
  }
  return modelPricingError(draft);
}

export function formatModelPrice(pricing: ModelPricing | null): string {
  if (!pricing) return '—';
  return `$${pricing.inputPricePerMillionTokens}/$${pricing.outputPricePerMillionTokens}`;
}
