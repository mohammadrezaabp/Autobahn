import { Show, createMemo } from 'solid-js';

/** Points are 0 to 1 or null (no reading); a null breaks the line instead of drawing a zero. */
export function Sparkline(props: { points: (number | null)[]; label: string; class?: string }) {
  const width = 120;
  const height = 24;

  const path = createMemo(() => {
    const points = props.points;
    if (points.length < 2) return '';
    const step = width / (points.length - 1);
    let d = '';
    let pen = false;
    points.forEach((value, i) => {
      if (value === null) {
        pen = false;
        return;
      }
      const x = (i * step).toFixed(1);
      const y = (height - 1 - value * (height - 2)).toFixed(1);
      d += `${pen ? 'L' : 'M'}${x} ${y} `;
      pen = true;
    });
    return d;
  });

  return (
    <svg
      class={`spark ${props.class ?? ''}`}
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      role="img"
      aria-label={props.label}
    >
      <Show when={path()}>
        <path d={path()} fill="none" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" />
      </Show>
    </svg>
  );
}
