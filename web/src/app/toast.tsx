import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react';
import { Toast } from '../ui';

export type ToastTone = 'info' | 'success' | 'error' | 'warning';

interface ToastItem { id: number; tone: ToastTone; message: string }

type Notify = (message: string, tone?: ToastTone) => void;

const ToastContext = createContext<Notify>(() => {});

/** Bottom-centre stack of design-system Toasts; each leaves after a few seconds. */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const next = useRef(1);

  const notify = useCallback<Notify>((message, tone = 'info') => {
    const id = next.current++;
    setItems((list) => [...list.slice(-3), { id, tone, message }]);
    // Errors stay longer: they are the ones a person needs to read.
    window.setTimeout(() => setItems((list) => list.filter((t) => t.id !== id)), tone === 'error' ? 9000 : 5000);
  }, []);

  const value = useMemo(() => notify, [notify]);
  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="sz-toasts" aria-live="polite">
        {items.map((t) => <Toast key={t.id} tone={t.tone}>{t.message}</Toast>)}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): Notify {
  return useContext(ToastContext);
}
