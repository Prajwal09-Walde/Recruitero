'use client';

import React, { useState, Suspense } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { toast } from '@/components/ui/Toaster';
import { Lock, ArrowRight, ArrowLeft, CheckCircle2, AlertTriangle } from 'lucide-react';
import axios from 'axios';
import { getApiUrl } from '@/lib/config';

function ResetPasswordForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const email = searchParams.get('email') || '';
  const token = searchParams.get('token') || '';

  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);

  const handleResetPassword = async (e: React.FormEvent) => {
    e.preventDefault();

    if (!email || !token) {
      toast('Invalid or missing email/token parameters.', { type: 'error' });
      return;
    }

    if (!password || !confirmPassword) {
      toast('Please fill in all fields', { type: 'error' });
      return;
    }

    if (password.length < 6) {
      toast('Password must be at least 6 characters long', { type: 'error' });
      return;
    }

    if (password !== confirmPassword) {
      toast('Passwords do not match', { type: 'error' });
      return;
    }

    setLoading(true);
    try {
      await axios.post(
        `${getApiUrl()}/api/auth/reset-password`,
        { email, token, newPassword: password }
      );
      setSuccess(true);
      toast('Password reset successful', {
        description: 'You can now sign in with your new password.',
        type: 'success',
      });
    } catch (err: any) {
      const detail = err.response?.data?.detail || err.response?.data?.title || err.message || 'Failed to reset password.';
      toast('Reset failed', {
        description: detail,
        type: 'error',
      });
    } finally {
      setLoading(false);
    }
  };

  const isLinkInvalid = !email || !token;

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
          <h1 className="text-2xl font-bold tracking-tight bg-gradient-to-r from-neutral-900 to-neutral-500 dark:from-white dark:to-neutral-400 bg-clip-text text-transparent">
            Choose New Password
          </h1>
          <p className="text-sm text-muted-foreground">
            {success ? 'Success! Your password is updated' : 'Create a secure new password for your account'}
          </p>
        </div>

        {isLinkInvalid ? (
          <div className="flex flex-col gap-5 text-center items-center">
            <div className="w-12 h-12 rounded-full bg-rose-500/10 flex items-center justify-center text-rose-500">
              <AlertTriangle className="w-6 h-6" />
            </div>
            <div className="space-y-1">
              <h3 className="font-bold text-sm text-foreground">Invalid Reset Link</h3>
              <p className="text-xs text-muted-foreground max-w-xs leading-relaxed mx-auto">
                This password reset link is invalid or incomplete. Please request a new verification email from the Forgot Password section.
              </p>
            </div>
            <button
              onClick={() => router.push('/forgot-password')}
              className="w-full bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-md transition-all flex items-center justify-center gap-2"
            >
              Request New Link
            </button>
          </div>
        ) : success ? (
          <div className="flex flex-col gap-5 text-center items-center">
            <div className="w-12 h-12 rounded-full bg-emerald-500/10 flex items-center justify-center text-emerald-500 animate-bounce">
              <CheckCircle2 className="w-6 h-6" />
            </div>
            <div className="space-y-1">
              <h3 className="font-bold text-sm text-foreground">Password Reset Successfully</h3>
              <p className="text-xs text-muted-foreground max-w-xs leading-relaxed mx-auto">
                Your password has been successfully updated. All previous active sessions have been revoked.
              </p>
            </div>
            <button
              onClick={() => router.push('/login')}
              className="w-full bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-md transition-all flex items-center justify-center gap-2"
            >
              Proceed to Sign In
              <ArrowRight className="w-4 h-4" />
            </button>
          </div>
        ) : (
          <form onSubmit={handleResetPassword} className="flex flex-col gap-5">
            {/* Email display */}
            <div className="bg-muted/30 border border-border rounded-xl p-3.5 flex flex-col gap-0.5">
              <span className="text-[10px] uppercase font-bold text-muted-foreground tracking-wider">Resetting account for</span>
              <span className="text-xs font-semibold text-foreground truncate">{email}</span>
            </div>

            {/* Password */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                New Password
              </label>
              <div className="relative">
                <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                <input
                  type="password"
                  placeholder="At least 6 characters"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="w-full bg-background dark:bg-white/5 border border-border dark:border-white/10 text-foreground rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
                />
              </div>
            </div>

            {/* Confirm Password */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                Confirm New Password
              </label>
              <div className="relative">
                <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                <input
                  type="password"
                  placeholder="Re-enter password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="w-full bg-background dark:bg-white/5 border border-border dark:border-white/10 text-foreground rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
                />
              </div>
            </div>

            {/* Actions */}
            <div className="flex flex-col gap-3">
              <button
                type="submit"
                disabled={loading}
                className="w-full bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-lg shadow-violet-600/20 hover:shadow-violet-600/35 transition-all flex items-center justify-center gap-2 group disabled:opacity-50"
              >
                {loading ? (
                  <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
                ) : (
                  <>
                    Update Password
                    <ArrowRight className="w-4 h-4 transition-transform group-hover:translate-x-1" />
                  </>
                )}
              </button>

              <button
                type="button"
                onClick={() => router.push('/forgot-password')}
                className="w-full border border-border bg-background hover:bg-muted text-foreground rounded-xl py-3 font-semibold text-sm transition-all flex items-center justify-center gap-2"
              >
                <ArrowLeft className="w-4 h-4" />
                Back
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}

export default function ResetPasswordPage() {
  return (
    <Suspense fallback={
      <div className="w-full max-w-lg p-8 glass-panel rounded-2xl flex items-center justify-center">
        <div className="w-6 h-6 border-2 border-violet-600 border-t-transparent rounded-full animate-spin" />
      </div>
    }>
      <ResetPasswordForm />
    </Suspense>
  );
}
