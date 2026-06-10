import { create } from 'zustand';
import { persist } from 'zustand/middleware';

export type ThemeType = 'midnight' | 'ocean' | 'emerald' | 'steel' | 'amber';

interface ThemeState {
  theme: ThemeType;
  setTheme: (theme: ThemeType) => void;
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set) => ({
      theme: 'midnight',
      setTheme: (theme) => set({ theme }),
    }),
    {
      name: 'recruitai-theme',
    }
  )
);
