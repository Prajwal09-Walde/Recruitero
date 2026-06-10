'use client';

import React from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { useAuthStore } from '@/stores/authStore';
import { useUiStore } from '@/stores/uiStore';
import { LogOut, PlusCircle, Menu, UserCircle, Briefcase } from 'lucide-react';
import { cn } from '@/lib/utils';
import apiClient from '@/lib/apiClient';

export function MainLayoutWrapper({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const isAuthPage = pathname === '/login' || pathname === '/register';
  const { user, logout } = useAuthStore();
  const { sidebarOpen, toggleSidebar } = useUiStore();
  const router = useRouter();

  if (isAuthPage) {
    return <div className="min-h-screen w-full flex items-center justify-center">{children}</div>;
  }

  const handleLogout = async () => {
    try {
      // Revoke refresh token server-side
      await apiClient.post('/api/auth/logout');
    } catch { /* ignore — we still want to clear local state */ }
    logout();
    router.replace('/login');
  };

  const navItems = [];
  if (user?.role === 'HRAdmin') {
    navItems.push({ label: 'Jobs Dashboard', icon: Briefcase, href: '/jobs' });
    navItems.push({ label: 'Create Job', icon: PlusCircle, href: '/jobs/new' });
  } else if (user?.role === 'Recruiter') {
    navItems.push({ label: 'Jobs Dashboard', icon: Briefcase, href: '/jobs' });
  } else if (user?.role === 'Viewer') {
    navItems.push({ label: 'Job Openings', icon: Briefcase, href: '/jobs' });
  }

  return (
    <div className="flex min-h-screen bg-background text-foreground">
      {/* Sidebar backdrop on mobile */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-20 bg-black/50 backdrop-blur-sm lg:hidden"
          onClick={toggleSidebar}
        />
      )}

      {/* Sidebar */}
      <aside
        className={cn(
          "fixed top-0 bottom-0 left-0 z-30 flex flex-col glass-panel border-r transition-all duration-300 w-64",
          !sidebarOpen && "-translate-x-full lg:translate-x-0 lg:w-20"
        )}
      >
        <div className="flex items-center justify-between h-16 px-4 border-b border-white/5">
          <div className="flex items-center gap-2 overflow-hidden">
            <div className="w-8 h-8 rounded-lg bg-gradient-to-tr from-violet-600 to-fuchsia-600 flex items-center justify-center font-bold text-white shrink-0 shadow-md">
              R
            </div>
            {sidebarOpen && (
              <span className="font-bold bg-gradient-to-r from-violet-400 to-fuchsia-400 bg-clip-text text-transparent">
                RecruitAI
              </span>
            )}
          </div>
        </div>

        <nav className="flex-1 px-3 py-4 space-y-1">
          {navItems.map((item) => {
            const isActive = pathname === item.href;
            return (
              <button
                key={item.href}
                onClick={() => router.push(item.href)}
                className={cn(
                  "w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium transition-all group hover:bg-white/5 hover:text-foreground",
                  isActive ? "bg-primary text-primary-foreground hover:bg-primary/95 shadow-md shadow-primary/25" : "text-muted-foreground"
                )}
              >
                <item.icon className="w-5 h-5 shrink-0" />
                {sidebarOpen && <span>{item.label}</span>}
              </button>
            );
          })}
        </nav>

        <div className="p-3 border-t border-white/5 flex flex-col gap-2">
          {user && (
            <div className="flex items-center gap-3 px-3 py-2">
              <UserCircle className="w-8 h-8 text-violet-400 shrink-0" />
              {sidebarOpen && (
                <div className="flex flex-col min-w-0">
                  <span className="text-xs font-semibold text-foreground truncate">{user.fullName || user.email}</span>
                  <span className="text-[10px] text-muted-foreground truncate">{user.email}</span>
                  <span className="text-[10px] text-muted-foreground uppercase tracking-wider font-bold">{user.role}</span>
                </div>
              )}
            </div>
          )}
          <button
            onClick={handleLogout}
            className="w-full flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium text-rose-400 hover:bg-rose-500/10 transition-colors"
          >
            <LogOut className="w-5 h-5 shrink-0" />
            {sidebarOpen && <span>Log Out</span>}
          </button>
        </div>
      </aside>

      {/* Main Content Area */}
      <div
        className={cn(
          "flex-1 flex flex-col transition-all duration-300 min-w-0",
          sidebarOpen ? "lg:pl-64" : "lg:pl-20"
        )}
      >
        <header className="sticky top-0 z-20 flex items-center justify-between h-16 px-6 glass-panel border-b border-white/5">
          <button
            onClick={toggleSidebar}
            className="p-2 -ml-2 rounded-lg hover:bg-white/5 text-muted-foreground hover:text-foreground transition-colors"
          >
            <Menu className="w-5 h-5" />
          </button>
          <div className="flex items-center gap-4">
            <span className="text-xs text-muted-foreground bg-white/5 border border-white/5 px-2.5 py-1 rounded-full font-medium">
              API Status: Online
            </span>
          </div>
        </header>
        <main className="flex-1 p-6 md:p-8 max-w-7xl w-full mx-auto">
          {children}
        </main>
      </div>
    </div>
  );
}
