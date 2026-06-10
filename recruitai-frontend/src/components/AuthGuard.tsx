'use client';

import React, { useEffect, useState } from 'react';
import { useRouter, usePathname } from 'next/navigation';
import { useAuthStore } from '@/stores/authStore';

export function AuthGuard({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, user } = useAuthStore();
  const router = useRouter();
  const pathname = usePathname();
  
  // Track client-side hydration explicitly for Zustand persist
  const [isHydrated, setIsHydrated] = useState(false);

  useEffect(() => {
    const checkHydration = () => {
      if (useAuthStore.persist.hasHydrated()) {
        setIsHydrated(true);
      } else {
        const unsub = useAuthStore.persist.onFinishHydration(() => {
          setIsHydrated(true);
          unsub();
        });
      }
    };
    checkHydration();
  }, []);

  useEffect(() => {
    if (!isHydrated) return;

    const isAuthPage = pathname === '/login' || pathname === '/register';

    if (!isAuthenticated && !isAuthPage) {
      router.replace('/login');
    } else if (isAuthenticated) {
      if (isAuthPage) {
        router.replace('/jobs');
      } else if (user?.role !== 'HRAdmin' && pathname === '/jobs/new') {
        router.replace('/jobs');
      }
    }
  }, [isHydrated, isAuthenticated, user, pathname, router]);

  // Public authentication pages should load instantly on client/server without any spinner
  const isAuthPage = pathname === '/login' || pathname === '/register';

  if (!isHydrated) {
    // Always pass through auth pages immediately — no spinner
    if (isAuthPage) {
      return <>{children}</>;
    }

    // If we already have an authenticated session in memory (e.g. just logged in),
    // skip the spinner and render the page directly. "Verifying credentials..." should
    // only appear when the page is cold-loaded (browser refresh) and we genuinely
    // need to check stored token state.
    if (isAuthenticated) {
      return <>{children}</>;
    }

    // Cold load of a protected route — still checking persisted auth state
    return (
      <div className="flex items-center justify-center min-h-screen bg-background text-foreground">
        <div className="flex flex-col items-center gap-4">
          <div className="w-10 h-10 border-4 border-primary border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-muted-foreground animate-pulse">Verifying credentials...</p>
        </div>
      </div>
    );
  }

  // If not authenticated and not on an auth page, show redirecting to login spinner
  if (!isAuthenticated && !isAuthPage) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background text-foreground">
        <div className="flex flex-col items-center gap-4">
          <div className="w-10 h-10 border-4 border-primary border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-muted-foreground animate-pulse">Redirecting to login...</p>
        </div>
      </div>
    );
  }

  // If authenticated and on an auth page, show redirecting to dashboard spinner
  if (isAuthenticated && isAuthPage) {
    return (
      <div className="flex items-center justify-center min-h-screen bg-background text-foreground">
        <div className="flex flex-col items-center gap-4">
          <div className="w-10 h-10 border-4 border-primary border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-muted-foreground animate-pulse">Redirecting to dashboard...</p>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
