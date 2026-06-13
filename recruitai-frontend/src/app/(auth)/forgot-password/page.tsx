'use client';

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { toast } from '@/components/ui/Toaster';
import { Mail, ArrowRight, ArrowLeft, Send } from 'lucide-react';
import axios from 'axios';
import { getApiUrl } from '@/lib/config';

export default function ForgotPasswordPage() {
  const [email, setEmail] = useState('');
  const [loading, setLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const router = useRouter();

  const handleForgotPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email) {
      toast('Please enter your email address', { type: 'error' });
      return;
    }

    setLoading(true);
    try {
      await axios.post(
        `${getApiUrl()}/api/auth/forgot-password`,
        { email }
      );
      setSubmitted(true);
      toast('Reset request submitted', {
        description: 'Check backend console logs for the simulated email.',
        type: 'success',
      });
    } catch (err: any) {
      const detail = err.response?.data?.detail || err.response?.data?.title || err.message || 'An error occurred.';
      toast('Request failed', {
        description: detail,
        type: 'error',
      });
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
            Reset Password
          </h1>
          <p className="text-sm text-muted-foreground">
            {submitted 
              ? 'Check the simulated email link in your terminal'
              : 'Enter your email to retrieve a reset verification link'}
          </p>
        </div>

        {submitted ? (
          <div className="flex flex-col gap-5 text-center">
            <div className="p-4 rounded-xl border border-emerald-500/20 bg-emerald-500/5 text-xs text-muted-foreground leading-relaxed text-left">
              <span className="font-semibold text-emerald-600 dark:text-emerald-400 block mb-1.5 text-sm">Simulated Email Dispatched!</span>
              We&apos;ve generated a password reset token for <span className="font-semibold text-foreground">{email}</span>. Since there is no SMTP configured, the verification email was printed directly in the <span className="font-semibold text-foreground">backend server console log</span>.
              <br /><br />
              Please check your dotnet backend terminal, copy the generated link containing the token, and paste it into your browser to complete your reset.
            </div>

            <button
              onClick={() => router.push('/login')}
              className="w-full bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-md transition-all flex items-center justify-center gap-2"
            >
              <ArrowLeft className="w-4 h-4" />
              Back to Sign In
            </button>
          </div>
        ) : (
          <form onSubmit={handleForgotPassword} className="flex flex-col gap-5">
            {/* Email */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
                Email Address
              </label>
              <div className="relative">
                <Mail className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                <input
                  type="email"
                  placeholder="you@company.com"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="w-full bg-background dark:bg-white/5 border border-border dark:border-white/10 text-foreground rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
                />
              </div>
            </div>

            {/* Simulated environment notice */}
            <div className="p-3 rounded-xl border border-amber-500/20 bg-amber-500/5 text-xs text-muted-foreground leading-relaxed">
              <span className="font-bold text-amber-600 dark:text-amber-400 block mb-0.5">Note on email delivery:</span>
              A simulated email containing the verification link will be printed directly to the terminal running the backend project instead of being sent over SMTP.
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
                    Send Verification Link
                    <Send className="w-4 h-4 transition-transform group-hover:translate-x-0.5 group-hover:-translate-y-0.5" />
                  </>
                )}
              </button>

              <button
                type="button"
                onClick={() => router.push('/login')}
                className="w-full border border-border bg-background hover:bg-muted text-foreground rounded-xl py-3 font-semibold text-sm transition-all flex items-center justify-center gap-2"
              >
                <ArrowLeft className="w-4 h-4" />
                Back to Sign In
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
