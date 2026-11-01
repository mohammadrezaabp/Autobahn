import type { JSX } from 'solid-js';

const BASE = {
  xmlns: 'http://www.w3.org/2000/svg',
  width: 16,
  height: 16,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  'stroke-width': 2,
  'stroke-linecap': 'round',
  'stroke-linejoin': 'round',
} as const;

/**
 * The shapes are built once, when the icon is declared, and a DOM node has one parent. Each use
 * therefore gets its own copy; without that an icon shown in two places appears only in the last.
 */
function copyOf(children: JSX.Element): JSX.Element {
  if (Array.isArray(children)) return children.map(copyOf);
  return children instanceof Node ? children.cloneNode(true) : children;
}

export function createIcon(children: JSX.Element, overrides?: Record<string, string | number>) {
  return (props: { class?: string; width?: number; height?: number }) => (
    <svg {...BASE} {...overrides} class={props.class} width={props.width ?? BASE.width} height={props.height ?? BASE.height}>
      {copyOf(children)}
    </svg>
  );
}
