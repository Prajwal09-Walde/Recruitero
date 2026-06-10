'use client';

import React, { createRef, useEffect } from 'react';
import { create } from 'zustand';
import { X, CheckCircle, AlertCircle, Info } from 'lucide-react';
import { cn } from '@/lib/utils';

export type ToastType = 'success' | 'error' | 'info';

export interface ToastItem {
  id: string;
  title: string;
  description?: string;
  type?: ToastType;
  duration?: number;
}

interface ToastState {
  toasts: ToastItem[];
  addToast: (toast: Omit<ToastItem, 'id'>) => void;
  removeToast: (id: string) => void;
}

const useToastStore = create<ToastState>((set) => ({
  toasts: [],
  addToast: (toast) => {
    const id = Math.random().toString(36).substring(2, 9);
    set((state) => ({
      toasts: [...state.toasts, { ...toast, id }],
    }));
  },
  removeToast: (id) =>
    set((state) => ({
      toasts: state.toasts.filter((t) => t.id !== id),
    })),
}));

export const toast = (title: string, options?: { description?: string; type?: ToastType; duration?: number }) => {
  useToastStore.getState().addToast({
    title,
    description: options?.description,
    type: options?.type || 'info',
    duration: options?.duration || 4000,
  });
};

export const useToast = () => {
  const { toasts, removeToast } = useToastStore();
  return {
    toasts,
    toast,
    dismiss: removeToast,
  };
};

export function Toaster() {
  const { toasts, dismiss } = useToast();

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 w-full max-w-md pointer-events-none">
      {toasts.map((item) => (
        <ToastCard key={item.id} item={item} onDismiss={dismiss} />
      ))}
    </div>
  );
}

function ToastCard({ item, onDismiss }: { item: ToastItem; onDismiss: (id: string) => void }) {
  useEffect(() => {
    const timer = setTimeout(() => {
      onDismiss(item.id);
    }, item.duration || 4000);
    return () => clearTimeout(timer);
  }, [item, onDismiss]);

  const icons = {
    success: <CheckCircle className="w-5 h-5 text-emerald-400 shrink-0" />,
    error: <AlertCircle className="w-5 h-5 text-rose-400 shrink-0" />,
    info: <Info className="w-5 h-5 text-violet-400 shrink-0" />,
  };

  const borders = {
    success: 'border-emerald-500/20 bg-emerald-500/5',
    error: 'border-rose-500/20 bg-rose-500/5',
    info: 'border-violet-500/20 bg-violet-500/5',
  };

  return (
    <div
      className={cn(
        'glass-panel border pointer-events-auto flex gap-3 p-4 rounded-xl shadow-lg transition-all duration-300 animate-in fade-in slide-in-from-bottom-5',
        borders[item.type || 'info']
      )}
    >
      {icons[item.type || 'info']}
      <div className="flex-1 flex flex-col gap-1 min-w-0">
        <h4 className="font-semibold text-sm leading-none">{item.title}</h4>
        {item.description && (
          <p className="text-xs text-muted-foreground leading-relaxed mt-1 break-words">
            {item.description}
          </p>
        )}
      </div>
      <button
        onClick={() => onDismiss(item.id)}
        className="text-muted-foreground hover:text-foreground shrink-0 self-start p-0.5 rounded-md hover:bg-white/5 transition-colors"
      >
        <X className="w-4 h-4" />
      </button>
    </div>
  );
}
