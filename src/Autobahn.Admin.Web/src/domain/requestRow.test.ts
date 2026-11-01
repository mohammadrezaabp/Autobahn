import { describe, expect, it } from 'vitest';
import { requestCostText, requestRowClass, requestTokensText, shortRequestId, tokensPerSecText } from './requestRow';

describe('requestRow', () => {
  it('requestRowClass_inFlight_isLiveNotError', () => {
    expect(requestRowClass({ isInFlight: true, statusCode: 0 })).toBe('row-live');
    expect(requestRowClass({ statusCode: 502 })).toBe('row-error');
    expect(requestRowClass({ statusCode: 429 })).toBe('row-warn');
    expect(requestRowClass({ statusCode: 200 })).toBe('');
    expect(requestRowClass({ statusCode: 200, errorCode: 'client_disconnected' })).toBe('row-error');
  });

  it('requestCostText_followsPricingStatus', () => {
    expect(requestCostText({ isInFlight: true })).toBe('…');
    expect(requestCostText({ pricingStatus: 'pending' })).toBe('pricing…');
    expect(requestCostText({ pricingStatus: 'unpriced' })).toBe('unpriced');
    expect(requestCostText({})).toBe('—');
    expect(requestCostText({ pricingStatus: 'priced', totalCost: 0.000014, currency: 'USD' })).toContain('0.000014');
  });

  it('requestTokensText_showsSplitOrTotal', () => {
    expect(requestTokensText({})).toBe('—');
    expect(requestTokensText({ promptTokens: 120, completionTokens: 45 })).toBe('120 → 45');
    expect(requestTokensText({ totalTokens: 2500, tokenSource: 'totalOnly' })).toBe('2.5k total');
  });

  it('tokensPerSecText_needsCompletionAndDuration', () => {
    expect(tokensPerSecText({ completionTokens: 50, durationMs: 1000 })).toBe('50');
    expect(tokensPerSecText({ completionTokens: 5, durationMs: 1000 })).toBe('5.0');
    expect(tokensPerSecText({ completionTokens: 0, durationMs: 1000 })).toBe('—');
  });

  it('shortRequestId_truncatesToEight', () => {
    expect(shortRequestId('req_849ee9eb')).toBe('req_849e');
    expect(shortRequestId(undefined)).toBe('—');
  });
});
