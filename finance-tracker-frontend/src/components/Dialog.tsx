import { useEffect, useRef } from "react";
import type { ReactNode } from "react";
export default function Dialog({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    const dialog = ref.current;
    dialog?.showModal();
    return () => {
      dialog?.close();
      opener?.focus();
    };
  }, []);
  return (
    <dialog
      ref={ref}
      className="finance-dialog"
      aria-labelledby="dialog-title"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
    >
      <div className="recent-title">
        <h2 id="dialog-title">{title}</h2>
        <button
          type="button"
          className="secondary-btn"
          onClick={onClose}
          aria-label="Close dialog"
        >
          Close
        </button>
      </div>
      {children}
    </dialog>
  );
}
