'use client';

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuthStore } from '@/stores/authStore';
import { toast } from '@/components/ui/Toaster';
import { Mail, Lock, User, Briefcase, ArrowRight } from 'lucide-react';
import axios from 'axios';

export default function RegisterPage() {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<'HRAdmin' | 'Recruiter'>('HRAdmin');
  const [loading, setLoading] = useState(false);
  const login = useAuthStore((s) => s.login);
  const router = useRouter();

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!fullName || !email || !password) {
      toast('Please fill in all fields', { type: 'error' });
      return;
    }

    setLoading(true);
    try {
      const response = await axios.post(
        `${process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000'}/api/auth/register`,
        { email, password, fullName, role }
      );

      const { token, refreshToken, email: resEmail, role: resRole, fullName: resFullName } = response.data;
      login(resEmail, resRole, token, refreshToken, resFullName);

      toast('Organization registered!', {
        description: 'Account created successfully.',
        type: 'success',
      });

      router.replace('/jobs/new');
    } catch (err: any) {
      const detail = err.response?.data?.detail || err.response?.data?.title || err.message || 'An error occurred during registration.';
      toast('Registration failed', {
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
        
        {/* Glow Element */}
        <div className="absolute -top-20 -right-20 w-40 h-40 bg-violet-600/20 blur-3xl rounded-full" />
        <div className="absolute -bottom-20 -left-20 w-40 h-40 bg-fuchsia-600/20 blur-3xl rounded-full" />

        {/* Header */}
        <div className="flex flex-col items-center text-center gap-2">
          <div className="w-12 h-12 rounded-xl bg-gradient-to-tr from-violet-600 to-fuchsia-600 flex items-center justify-center font-bold text-white text-xl shadow-lg shadow-violet-500/20 mb-2">
            R
          </div>
          <h1 className="text-2xl font-bold tracking-tight bg-gradient-to-r from-white to-neutral-400 bg-clip-text text-transparent">
            Get Started with RecruitAI
          </h1>
          <p className="text-sm text-muted-foreground">
            Scaffold a talent portal for your recruitment agency
          </p>
        </div>

        <form onSubmit={handleRegister} className="flex flex-col gap-5">
          {/* Full Name */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Full Name
            </label>
            <div className="relative">
              <User className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                type="text"
                placeholder="Jane Doe"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
            </div>
          </div>

          {/* Email */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Work Email
            </label>
            <div className="relative">
              <Mail className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                type="email"
                placeholder="jane@company.com"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
            </div>
          </div>

          {/* Password */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Password
            </label>
            <div className="relative">
              <Lock className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                type="password"
                placeholder="Create password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 pl-11 pr-4 text-sm placeholder-muted-foreground focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
            </div>
          </div>

          {/* Preferred Role */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              Your Platform Role
            </label>
            <div className="grid grid-cols-2 gap-3">
              {(['HRAdmin', 'Recruiter'] as const).map((r) => (
                <button
                  key={r}
                  type="button"
                  onClick={() => setRole(r)}
                  className={`py-2 rounded-xl text-xs font-semibold border transition-all ${
                    role === r
                      ? 'border-violet-500 bg-violet-500/10'
                      : 'border-white/5 bg-white/5 hover:bg-white/10'
                  }`}
                >
                  {r === 'HRAdmin' ? 'HR Admin' : 'Recruiter'}
                </button>
              ))}
            </div>
          </div>

          {/* Submit */}
          <button
            type="submit"
            disabled={loading}
            className="w-full mt-2 bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-lg shadow-violet-600/20 hover:shadow-violet-600/35 transition-all flex items-center justify-center gap-2 group disabled:opacity-50"
          >
            {loading ? (
              <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              <>
                Register Agency
                <ArrowRight className="w-4 h-4 transition-transform group-hover:translate-x-1" />
              </>
            )}
          </button>
        </form>

        <div className="text-center mt-2">
          <p className="text-xs text-muted-foreground">
            Already have an account?{' '}
            <button
              onClick={() => router.push('/login')}
              className="text-violet-400 hover:underline font-medium"
            >
              Sign in here
            </button>
          </p>
        </div>
      </div>
    </div>
  );
}
