'use client';

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuthStore } from '@/stores/authStore';
import { toast } from '@/components/ui/Toaster';
import { Lock, Mail, ArrowRight, ShieldCheck } from 'lucide-react';
import axios from 'axios';
import { getApiUrl } from '@/lib/config';
import { Role } from '@/types';

export default function LoginPage() {
  const [email, setEmail]       = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading]   = useState(false);
  const login = useAuthStore((s) => s.login);
  const router = useRouter();

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      toast('Please enter email and password', { type: 'error' });
      return;
    }

    setLoading(true);
    try {
      const response = await axios.post(
        `${getApiUrl()}/api/auth/login`,
        { email, password }
      );

      const { token, refreshToken, email: resEmail, role: resRole, fullName } = response.data;
      login(resEmail, resRole as Role, token, refreshToken, fullName);

      toast(`Welcome back, ${fullName || resEmail}!`, {
        description: `Signed in as ${resRole}`,
        type: 'success',
      });

      router.replace('/jobs');
    } catch (err: any) {
      const status = err.response?.status;
      const detail = err.response?.data?.detail;
      if (status === 401) {
        toast('Incorrect email or password', {
          description: 'Please check your credentials and try again.',
          type: 'error',
        });
      } else {
        const fallbackDetail = err.response?.data?.title || err.message || 'An error occurred during authentication.';
        toast('Login failed', {
          description: detail || fallbackDetail,
          type: 'error',
        });
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="w-full max-w-lg p-1 animate-in fade-in zoom-in-95 duration-500">
      <div className="glass-panel p-8 md:p-10 rounded-2xl shadow-2xl relative overflow-hidden flex flex-col gap-6">

        {/* Glow Elements */}
        <div className="absolute -top-20 -right-20 w-40 h-40 bg-violet-600/20 blur-3xl rounded-full" />
        <div className="absolute -bottom-20 -left-20 w-40 h-40 bg-fuchsia-600/20 blur-3xl rounded-full" />

        {/* Header */}
        <div className="flex flex-col items-center text-center gap-2">
          <div className="w-12 h-12 rounded-xl bg-gradient-to-tr from-violet-600 to-fuchsia-600 flex items-center justify-center font-bold text-white text-xl shadow-lg shadow-violet-500/20 mb-2">
            R
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-black dark:text-white">
            Recruitero Intelligence
          </h1>
          <p className="text-sm text-muted-foreground">
            Sign in with your registered email and password
          </p>
        </div>

        <form onSubmit={handleLogin} className="flex flex-col gap-5">
          {/* Email */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Email Address
            </label>
            <div className="relative">
              <Mail className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                id="login-email"
                type="email"
                placeholder="you@company.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="email"
                className="w-full bg-background dark:bg-white/5 border border-border dark:border-white/10 text-foreground rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
            </div>
          </div>

          {/* Password */}
          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between">
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                Password
              </label>
              <button
                type="button"
                onClick={() => router.push('/forgot-password')}
                className="text-xs text-violet-600 dark:text-violet-400 hover:underline font-semibold"
              >
                Forgot password?
              </button>
            </div>
            <div className="relative">
              <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                id="login-password"
                type="password"
                placeholder="••••••••"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                className="w-full bg-background dark:bg-white/5 border border-border dark:border-white/10 text-foreground rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
            </div>
          </div>

          {/* Info banner */}
          <div className="flex items-start gap-2.5 bg-violet-500/5 dark:bg-violet-500/5 border border-violet-500/20 dark:border-violet-500/15 rounded-xl p-3">
            <ShieldCheck className="w-4 h-4 text-violet-600 dark:text-violet-400 mt-0.5 shrink-0" />
            <p className="text-xs text-muted-foreground leading-relaxed">

              Don&apos;t have an account?{' '}
              <button
                type="button"
                onClick={() => router.push('/register')}
                className="text-violet-600 dark:text-violet-400 hover:underline font-bold"
              >
                Register here
              </button>
            </p>
          </div>

          {/* Submit */}
          <button
            id="login-submit"
            type="submit"
            disabled={loading}
            className="w-full mt-1 bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-lg shadow-violet-600/20 hover:shadow-violet-600/35 transition-all flex items-center justify-center gap-2 group disabled:opacity-50"
          >
            {loading ? (
              <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              <>
                Sign In
                <ArrowRight className="w-4 h-4 transition-transform group-hover:translate-x-1" />
              </>
            )}
          </button>
        </form>
      </div>
    </div>
  );
}
