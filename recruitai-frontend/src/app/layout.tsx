import React from 'react';
import type { Metadata } from 'next';
import { Providers } from './Providers';
import { AuthGuard } from '@/components/AuthGuard';
import { MainLayoutWrapper } from '@/components/MainLayoutWrapper';
import './globals.css';

export const metadata: Metadata = {
  title: 'RecruitAI — AI Recruitment Intelligence Platform',
  description: 'AI-powered candidate ranking and pipeline management.',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body>
        <Providers>
          <AuthGuard>
            <MainLayoutWrapper>{children}</MainLayoutWrapper>
          </AuthGuard>
        </Providers>
      </body>
    </html>
  );
}
