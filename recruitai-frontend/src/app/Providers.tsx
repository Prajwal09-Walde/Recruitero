'use client';

import React, { useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from '@/components/ui/Toaster';

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 5 * 60 * 1000,   // 5 min — data is fresh, no background refetch
            gcTime: 10 * 60 * 1000,     // 10 min — keep cache alive across navigations
            refetchOnWindowFocus: false,
            retry: 1,
          },
        },
      })
  );

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <Toaster />
    </QueryClientProvider>
  );
}
