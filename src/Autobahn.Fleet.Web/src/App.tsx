import { onMount } from 'solid-js';

// Preserve saved compute links while using the gateway's navigation and sign-in.
export function App() {
  const section = window.location.hash.replace(/^#\/?/, '');
  const page = ['gpus', 'models', 'downloads', 'activity'].includes(section) ? section : 'gpus';
  const destination = `/admin/index.html#/fleet/${page}`;
  onMount(() => window.location.replace(destination));
  return <main class="content"><a href={destination}>Open Compute</a></main>;
}
