import { formatNum } from './format';

/** One row of the live request feed, as `/admin/api/requests` and the push stream send it. */
export interface RequestRowFields {
  requestId?: string;
  method?: string;
  path?: string;
  modelId?: string | null;
  tenantId?: string | null;
  costCenter?: string | null;
  statusCode?: number;
  durationMs?: number;
  isStreaming?: boolean;
  isInFlight?: boolean;
  timeToFirstTokenMs?: number | null;
  errorCode?: string | null;
  timestampUtc?: string;
  promptTokens?: number | null;
  completionTokens?: number | null;
  totalTokens?: number | null;
  tokenSource?: string | null;
  inputCost?: number | null;
  outputCost?: number | null;
  totalCost?: number | null;
  currency?: string | null;
  pricingStatus?: string | null;
}

export function shortRequestId(id: unknown): string {
  const text = String(id ?? '');
  if (!text) return '—';
  return text.length > 8 ? text.slice(0, 8) : text;
}

export function formatClock(iso: unknown): string {
  if (!iso) return '—';
  const date = new Date(String(iso));
  if (Number.isNaN(date.getTime())) return String(iso);
  return date.toLocaleTimeString(undefined, { hour12: false });
}

function formatCompact(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 10_000) return `${Math.round(n / 1000)}k`;
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
}

export function hasTokens(r: RequestRowFields): boolean {
  return r.promptTokens != null || r.completionTokens != null || r.totalTokens != null;
}

export function requestTokensText(r: RequestRowFields): string {
  if (r.isInFlight) return '…';
  if (!hasTokens(r)) return '—';
  if (r.tokenSource === 'totalOnly') return `${formatCompact(Number(r.totalTokens ?? 0))} total`;
  return `${formatCompact(Number(r.promptTokens ?? 0))} → ${formatCompact(Number(r.completionTokens ?? 0))}`;
}

export function requestTokensTitle(r: RequestRowFields): string {
  if (!hasTokens(r)) return r.isInFlight ? 'Usage arrives with the response' : 'No usage recorded';
  return `${formatNum(r.promptTokens ?? 0)} prompt · ${formatNum(r.completionTokens ?? 0)} completion · ${tokenSourceLabel(r.tokenSource)}`;
}

export function tokenSourceLabel(source: unknown): string {
  if (source === 'estimated') return 'estimated (client disconnected before the usage frame)';
  if (source === 'totalOnly') return 'combined total only (no input/output split reported)';
  if (source === 'split') return 'reported by upstream';
  return '—';
}

/** Sub-cent amounts keep their digits: a $0.000014 request must not read as $0.00. */
export function formatRequestCost(value: unknown, currency?: string | null): string {
  const n = Number(value);
  if (value == null || !Number.isFinite(n)) return '—';
  const digits = n === 0 || n >= 1 ? 2 : n >= 0.01 ? 4 : 6;
  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: currency || 'USD',
      minimumFractionDigits: 2,
      maximumFractionDigits: digits,
    }).format(n);
  } catch {
    return n.toFixed(digits);
  }
}

/** How the row's cost cell should read, from the pricing state the gateway reports. */
export function requestCostText(r: RequestRowFields): string {
  if (r.isInFlight) return '…';
  if (r.pricingStatus === 'priced') return formatRequestCost(r.totalCost, r.currency);
  if (r.pricingStatus === 'pending') return 'pricing…';
  if (r.pricingStatus === 'unpriced') return 'unpriced';
  return '—';
}

export function requestCostClass(r: RequestRowFields): string {
  if (r.pricingStatus === 'priced') return 'is-priced';
  return r.pricingStatus === 'pending' ? 'is-pending' : 'is-muted';
}

export function pricingStatusLabel(r: RequestRowFields): string {
  if (r.pricingStatus === 'priced') return "Priced from the model's rate card";
  if (r.pricingStatus === 'pending') return 'Queued for pricing — costs land after the next usage flush';
  if (r.pricingStatus === 'unpriced') return 'No rate card for this model (or no billing store configured)';
  if (r.isInFlight) return 'Not yet — usage arrives with the response';
  return 'No usage recorded for this request';
}

/** In flight is tinted, not coloured like an error: nothing has gone wrong yet. */
export function requestRowClass(r: RequestRowFields): string {
  if (r.isInFlight) return 'row-live';
  const code = Number(r.statusCode);
  if (code >= 500) return 'row-error';
  if (code >= 400) return 'row-warn';
  return r.errorCode ? 'row-error' : '';
}

export function tokensPerSecText(r: RequestRowFields): string {
  if (r.isInFlight) return '…';
  const completion = Number(r.completionTokens ?? 0);
  const elapsed = Number(r.durationMs ?? 0);
  if (!(completion > 0) || !(elapsed > 0)) return '—';
  const rate = completion / (elapsed / 1000);
  return rate.toFixed(rate < 10 ? 1 : 0);
}
