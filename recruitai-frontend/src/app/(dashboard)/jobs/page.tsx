'use client';

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useJobList } from '@/hooks/useJobLeaderboard';
import { useAuthStore } from '@/stores/authStore';
import { Briefcase, Building2, Calendar, Plus, ArrowRight } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useQueryClient } from '@tanstack/react-query';
import apiClient from '@/lib/apiClient';
import { queryKeys } from '@/lib/queryKeys';
import { toast } from '@/components/ui/Toaster';

export default function JobsListPage() {
  const router = useRouter();
  const { user } = useAuthStore();
  const { data: jobs, isLoading, error } = useJobList();
  const queryClient = useQueryClient();

  const isHrAdmin = user?.role === 'HRAdmin';


  return (
    <div className="flex flex-col gap-6 w-full max-w-5xl mx-auto py-4 animate-in fade-in duration-300">
      
      {/* Header section */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 border-b border-border pb-5">
        <div className="flex flex-col gap-1.5">
          <h1 className="text-3xl font-extrabold tracking-tight text-black dark:text-white">
            {isHrAdmin || user?.role === 'Recruiter' ? 'Jobs Dashboard' : 'Job Openings'}
          </h1>
          <p className="text-sm text-muted-foreground">
            {isHrAdmin || user?.role === 'Recruiter' 
              ? 'Manage job postings, review resume match scoring, and coordinate interviews.'
              : 'Explore our open career opportunities and submit your application.'}
          </p>
        </div>

        {isHrAdmin && (
          <div className="flex items-center gap-3">
            <button
              onClick={() => router.push('/jobs/new')}
              className="inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-xs font-bold bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white shadow-lg shadow-violet-600/15 hover:shadow-violet-600/30 transition-all shrink-0"
            >
              <Plus className="w-4 h-4" />
              Create Job Posting
            </button>
          </div>
        )}
      </div>

      {/* Loading state */}
      {isLoading && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {Array.from({ length: 4 }).map((_, i) => (
            <div key={i} className="glass-panel p-6 rounded-2xl border border-border h-44 flex flex-col justify-between animate-pulse">
              <div className="space-y-3">
                <div className="h-5 bg-black/5 dark:bg-white/5 rounded w-2/3" />
                <div className="h-3 bg-black/5 dark:bg-white/5 rounded w-1/3" />
              </div>
              <div className="h-8 bg-black/5 dark:bg-white/5 rounded w-24" />
            </div>
          ))}
        </div>
      )}

      {/* Error state */}
      {error && (
        <div className="p-6 border border-rose-500/10 bg-rose-500/5 rounded-2xl text-center">
          <p className="text-sm font-semibold text-rose-400">Failed to load job postings</p>
          <p className="text-xs text-muted-foreground mt-1">Please try refreshing the page or contact support.</p>
        </div>
      )}

      {/* Jobs grid */}
      {!isLoading && !error && jobs && (
        <>
          {jobs.length === 0 ? (
            <div className="glass-panel p-10 rounded-2xl border border-border text-center flex flex-col items-center justify-center min-h-[300px]">
              <Briefcase className="w-12 h-12 text-neutral-400/20 dark:text-white/10 mb-3" />
              <h3 className="text-lg font-bold text-foreground">No jobs posted yet</h3>
              <p className="text-sm text-muted-foreground max-w-xs mt-1">
                {isHrAdmin 
                  ? 'Get started by creating your first job posting to start scoring resumes!'
                  : 'Check back later for new career opportunities!'}
              </p>
              {isHrAdmin && (
                <div className="flex gap-3 mt-4">
                  <button
                    onClick={() => router.push('/jobs/new')}
                    className="bg-violet-600 hover:bg-violet-500 text-white rounded-lg px-4 py-2 text-xs font-semibold shadow"
                  >
                    Post a Job
                  </button>
                </div>
              )}
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
              {jobs.map((job) => (
                <div
                  key={job.id}
                  onClick={() => router.push(`/jobs/${job.id}`)}
                  className="group relative glass-panel p-6 rounded-2xl border border-border hover:border-primary/30 bg-card/40 hover:bg-card/60 transition-all cursor-pointer flex flex-col justify-between h-48 shadow-lg shadow-black/5"
                >
                  {/* Hover subtle glow */}
                  <div className="absolute inset-0 bg-gradient-to-tr from-violet-600/5 to-fuchsia-600/5 opacity-0 group-hover:opacity-100 transition-opacity rounded-2xl pointer-events-none" />

                  <div className="flex flex-col gap-2 min-w-0 z-10">
                    <div className="flex items-start justify-between gap-2">
                      <h2 className="text-lg font-bold text-foreground group-hover:text-violet-600 dark:group-hover:text-violet-400 transition-colors truncate">
                        {job.title}
                      </h2>
                      <span className="shrink-0 text-[10px] font-bold uppercase tracking-wider px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20">
                        {job.isActive ? 'Active' : 'Closed'}
                      </span>
                    </div>

                    <div className="flex flex-wrap items-center gap-3.5 text-xs text-muted-foreground mt-1">
                      <span className="flex items-center gap-1">
                        <Building2 className="w-3.5 h-3.5" /> {job.department}
                      </span>
                      <span className="flex items-center gap-1">
                        <Calendar className="w-3.5 h-3.5" /> {new Date(job.createdAt).toLocaleDateString()}
                      </span>
                    </div>
                  </div>

                  {/* Description snippet */}
                  <p className="text-xs text-muted-foreground line-clamp-2 leading-relaxed z-10 my-3">
                    {job.description}
                  </p>

                  <div className="flex items-center justify-between z-10 pt-2 border-t border-border/40">
                    <span className="text-xs text-violet-600 dark:text-violet-400 group-hover:text-violet-700 dark:group-hover:text-violet-300 font-semibold flex items-center gap-1">
                      {isHrAdmin || user?.role === 'Recruiter' ? 'View Pipeline' : 'View Details & Apply'}
                      <ArrowRight className="w-3.5 h-3.5 transition-transform group-hover:translate-x-1" />
                    </span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
