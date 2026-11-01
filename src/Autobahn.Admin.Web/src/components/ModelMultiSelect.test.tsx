import { createSignal } from 'solid-js';
import { fireEvent, render, screen } from '@solidjs/testing-library';
import { describe, expect, it, vi } from 'vitest';
import type { GrantChoice } from '../domain/keyGrants';
import { ModelMultiSelect } from './ModelMultiSelect';

const choices: GrantChoice[] = [
  { id: 'qwen-27b', note: '', disabled: false },
  { id: 'qwen-27b-1m', note: 'down', disabled: false },
  { id: 'gemma', note: 'not allowed for the tenant', disabled: true },
];

function mount(initial: string[] = []) {
  const [selected, setSelected] = createSignal(initial);
  render(() => <ModelMultiSelect choices={choices} selected={selected()} onChange={setSelected} />);
  return selected;
}

const openMenu = () => fireEvent.click(screen.getByRole('button', { name: /models/i, expanded: false }));

describe('ModelMultiSelect', () => {
  it('ticksAndUnticksAModel', () => {
    const selected = mount();
    openMenu();

    fireEvent.click(screen.getByRole('checkbox', { name: /qwen-27b-1m/ }));
    expect(selected()).toEqual(['qwen-27b-1m']);

    fireEvent.click(screen.getByRole('button', { name: 'Remove qwen-27b-1m' }));
    expect(selected()).toEqual([]);
  });

  it('selectAllTicksWhatTheFilterShowsAndSkipsWhatCannotBeTicked', () => {
    const selected = mount();
    openMenu();

    fireEvent.input(screen.getByRole('searchbox'), { target: { value: 'qwen' } });
    expect(screen.queryByRole('checkbox', { name: /gemma/ })).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Select all' }));
    expect(selected()).toEqual(['qwen-27b', 'qwen-27b-1m']);

    fireEvent.input(screen.getByRole('searchbox'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Select all' }));
    expect(selected()).toEqual(['qwen-27b', 'qwen-27b-1m']);
    expect((screen.getByRole('checkbox', { name: /gemma/ }) as HTMLInputElement).disabled).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
    expect(selected()).toEqual([]);
  });

  it('escapeClosesTheMenuAndNotWhatItSitsIn', () => {
    const outer = vi.fn();
    document.addEventListener('keydown', outer);
    mount();
    openMenu();

    fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'Escape' });

    expect(screen.queryByRole('searchbox')).toBeNull();
    expect(outer).not.toHaveBeenCalled();
    document.removeEventListener('keydown', outer);
  });
});
