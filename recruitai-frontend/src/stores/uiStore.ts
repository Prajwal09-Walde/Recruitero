import { create } from 'zustand';

interface UiState {
  sidebarOpen: boolean;
  toggleSidebar: () => void;
  activeJobId: string | null;
  setActiveJobId: (id: string | null) => void;
}

export const useUiStore = create<UiState>((set) => ({
  sidebarOpen: true,
  toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
  activeJobId: null,
  setActiveJobId: (id) => set({ activeJobId: id }),
}));
