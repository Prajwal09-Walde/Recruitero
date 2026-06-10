import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { User, Role } from '@/types';

interface AuthState {
  token: string | null;
  refreshToken: string | null;
  user: User | null;
  isAuthenticated: boolean;
  setTokens: (token: string, refreshToken: string) => void;
  login: (email: string, role: Role, token: string, refreshToken: string, fullName?: string) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      refreshToken: null,
      user: null,
      isAuthenticated: false,
      setTokens: (token, refreshToken) => set({ token, refreshToken }),
      login: (email, role, token, refreshToken, fullName) =>
        set({
          user: { email, role, fullName },
          token,
          refreshToken,
          isAuthenticated: true,
        }),
      logout: () =>
        set({
          user: null,
          token: null,
          refreshToken: null,
          isAuthenticated: false,
        }),
    }),
    {
      name: 'recruitai-auth',
    }
  )
);
