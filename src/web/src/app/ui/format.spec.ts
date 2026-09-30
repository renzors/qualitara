import { Metric } from '../api/models';
import { cellBackground, cityOf, localDateTime, percent, polarityColor, signed, zLevel } from './format';

function metric(patch: Partial<Metric>): Metric {
  return {
    key: 'total', label: 'All activity', kind: 'count', status: 'normal', value: 10, expected: 10, expectedLow: 5, expectedHigh: 15,
    z: 0, direction: 'same', polarity: 'neutral', sampleSize: 10, sampleUnit: 'events', valueLow: null, valueHigh: null,
    display: { value: '10', low: '5', high: '15', expected: '10' }, reason: '', detail: null, ...patch,
  };
}

describe('format helpers', () => {
  it('signs z-scores with a true minus sign', () => {
    expect(signed(2.345)).toBe('+2.3');
    expect(signed(-3)).toBe('−3.0');
    expect(signed(0)).toBe('0.0');
  });

  it('maps polarity to the scorecard colours', () => {
    expect(polarityColor('good')).toBe('var(--good)');
    expect(polarityColor('bad')).toBe('var(--bad)');
    expect(polarityColor('neutral')).toBe('var(--neutral)');
  });

  it('tints cells only from |z| ≥ 1 and caps intensity at |z| = 4', () => {
    expect(cellBackground(metric({ z: 0.5 }))).toBeNull();
    expect(cellBackground(metric({ z: -2, polarity: 'bad' }))).toBe('color-mix(in srgb, var(--bad) 31%, var(--paper))');
    expect(cellBackground(metric({ z: 9, polarity: 'good' }))).toBe('color-mix(in srgb, var(--good) 62%, var(--paper))');
    expect(cellBackground(metric({ z: 3, status: 'insufficient_data' }))).toBeNull();
  });

  it('weights cells by status', () => {
    expect(zLevel(metric({ status: 'unusual' }))).toBe(3);
    expect(zLevel(metric({ status: 'worth_a_look' }))).toBe(2);
    expect(zLevel(metric({ status: 'normal' }))).toBe(1);
  });

  it('formats local times in the account timezone, across DST', () => {
    expect(localDateTime('2026-07-27T22:20:34Z', 'America/New_York')).toBe('Jul 27, 6:20pm');
    expect(localDateTime('2026-01-27T22:20:34Z', 'America/New_York')).toBe('Jan 27, 5:20pm');
    expect(localDateTime('2026-07-27T22:20:34Z', 'America/Phoenix')).toBe('Jul 27, 3:20pm');
  });

  it('names timezones by city and handles UTC', () => {
    expect(cityOf('America/Los_Angeles')).toBe('Los Angeles');
    expect(cityOf('UTC')).toBe('UTC');
  });

  it('never shows 0% for a small non-zero share', () => {
    expect(percent(1, 400)).toBe('<1%');
    expect(percent(0, 0)).toBe('–');
    expect(percent(5, 81)).toBe('6%');
  });
});
