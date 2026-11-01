import { onMount, type JSX } from 'solid-js';

/** A modal on the browser's own <dialog>: Escape closes it and focus stays inside. */
export function Dialog(props: { title: string; onClose: () => void; children: JSX.Element }) {
  let element!: HTMLDialogElement;
  onMount(() => element.showModal());

  return (
    <dialog
      ref={element}
      class="dialog"
      onClose={props.onClose}
      // A click on the backdrop lands on the dialog element itself, not on anything inside it.
      onClick={(event) => event.target === element && props.onClose()}
    >
      <div class="dialog-body">
        <header class="dialog-head">
          <h2>{props.title}</h2>
          <button class="btn btn-quiet" type="button" aria-label="Close" onClick={props.onClose}>✕</button>
        </header>
        {props.children}
      </div>
    </dialog>
  );
}
