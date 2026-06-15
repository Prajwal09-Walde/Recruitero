'use client';

import React, { useState, useEffect } from 'react';
import apiClient from '@/lib/apiClient';
import { toast } from '@/components/ui/Toaster';
import {
  BarChart3,
  Briefcase,
  Users,
  TrendingUp,
  Clock,
  ChevronRight,
  TrendingDown,
  Building2,
  AlertTriangle,
  RefreshCw
} from 'lucide-react';
import { cn } from '@/lib/utils';

interface JobBreakdown {
  jobId: string;
  title: string;
  department: string;
  applicationCount: number;
  averageScore: number;
  statusCounts: Record<string, number>;
}

interface DepartmentStat {
  department: string;
  jobsCount: number;
  applicationsCount: number;
}

interface AnalyticsData {
  totalJobs: number;
  totalApplications: number;
  funnel: Record<string, number>;
  departments: DepartmentStat[];
  jobsBreakdown: JobBreakdown[];
}

export default function AnalyticsPage() {
  const [data, setData] = useState<AnalyticsData | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const fetchAnalytics = async (silent = false) => {
    if (!silent) setIsLoading(true);
    else setRefreshing(true);
    
    setError(null);
    try {
      const res = await apiClient.get<AnalyticsData>('/api/jobs/analytics');
      setData(res.data);
    } catch (err: any) {
      console.error(err);
      setError(err?.response?.data?.detail || 'Failed to fetch analytics metrics.');
      toast('Error loading analytics', {
        description: 'Please ensure the backend server is running.',
        type: 'error'
      });
    } finally {
      setIsLoading(false);
      setRefreshing(false);
    }
  };

  useEffect(() => {
    fetchAnalytics();
  }, []);

  if (isLoading) {
    return (
      <div className="flex flex-col gap-6 items-center justify-center min-h-[400px]">
        <div className="w-8 h-8 border-3 border-violet-500 border-t-transparent rounded-full animate-spin" />
        <p className="text-xs text-muted-foreground animate-pulse">Aggregating pipeline metrics...</p>
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="flex flex-col items-center justify-center p-8 border border-border bg-card/60 rounded-2xl gap-3 text-center min-h-[300px] max-w-lg mx-auto">
        <AlertTriangle className="w-8 h-8 text-rose-500" />
        <h4 className="font-semibold text-sm">Failed to Load Analytics</h4>
        <p className="text-xs text-muted-foreground leading-relaxed">
          {error || 'Unable to connect to the recruitment database.'}
        </p>
        <button
          onClick={() => fetchAnalytics()}
          className="bg-violet-600 hover:bg-violet-500 text-white rounded-xl px-4 py-2 text-xs font-semibold mt-2 transition-all"
        >
          Retry Load
        </button>
      </div>
    );
  }

  // Derived metrics
  const scoredApps = data.jobsBreakdown.reduce((sum, j) => sum + (j.statusCounts['Scored'] || 0) + (j.statusCounts['SentToRecruiter'] || 0) + (j.statusCounts['Shortlisted'] || 0) + (j.statusCounts['Rejected'] || 0), 0);
  const avgSystemScore = data.jobsBreakdown.length > 0 && scoredApps > 0
    ? Math.round(data.jobsBreakdown.reduce((sum, j) => sum + (j.averageScore * j.applicationCount), 0) / data.totalApplications)
    : 0;

  const queuedOrProcessing = (data.funnel['Queued'] || 0) + (data.funnel['Processing'] || 0);

  // Funnel chart calculations
  const funnelStages = [
    { label: 'Ingested', count: data.totalApplications, color: 'from-blue-600 to-indigo-600', darkColor: 'from-blue-500 to-indigo-500' },
    { label: 'AI Scored', count: data.totalApplications - (data.funnel['Queued'] || 0) - (data.funnel['Processing'] || 0) - (data.funnel['Failed'] || 0), color: 'from-indigo-600 to-violet-600', darkColor: 'from-indigo-500 to-violet-500' },
    { label: 'Team Lead Review', count: (data.funnel['SentToRecruiter'] || 0) + (data.funnel['Shortlisted'] || 0) + (data.funnel['Rejected'] || 0), color: 'from-violet-600 to-fuchsia-600', darkColor: 'from-violet-500 to-fuchsia-500' },
    { label: 'Shortlisted', count: data.funnel['Shortlisted'] || 0, color: 'from-emerald-600 to-teal-600', darkColor: 'from-emerald-500 to-teal-500' }
  ];

  const maxStageCount = Math.max(...funnelStages.map(s => s.count), 1);

  return (
    <div className="flex flex-col gap-6 w-full animate-in fade-in duration-300">
      
      {/* Title Header */}
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Recruitment Analytics</h1>
          <p className="text-xs text-muted-foreground mt-0.5">
            Real-time pipeline funnel status, department alignment, and candidate scoring health.
          </p>
        </div>
        <button
          onClick={() => fetchAnalytics(true)}
          disabled={refreshing}
          className="p-2 border border-border bg-card hover:bg-muted/10 rounded-xl transition-all flex items-center justify-center text-muted-foreground hover:text-foreground disabled:opacity-50"
          title="Refresh Data"
        >
          <RefreshCw className={cn("w-4 h-4", refreshing && "animate-spin")} />
        </button>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Card 1: Active Jobs */}
        <div className="glass-panel p-5 rounded-2xl border border-border flex items-center gap-4">
          <div className="w-10 h-10 rounded-xl bg-blue-500/10 dark:bg-blue-600/10 text-blue-600 dark:text-blue-400 flex items-center justify-center shrink-0">
            <Briefcase className="w-5 h-5" />
          </div>
          <div className="min-w-0">
            <p className="text-[10px] text-muted-foreground uppercase font-bold tracking-wider">Active Jobs</p>
            <p className="text-xl font-bold tracking-tight mt-0.5">{data.totalJobs}</p>
          </div>
        </div>

        {/* Card 2: Applications */}
        <div className="glass-panel p-5 rounded-2xl border border-border flex items-center gap-4">
          <div className="w-10 h-10 rounded-xl bg-violet-500/10 dark:bg-violet-600/10 text-violet-600 dark:text-violet-400 flex items-center justify-center shrink-0">
            <Users className="w-5 h-5" />
          </div>
          <div className="min-w-0">
            <p className="text-[10px] text-muted-foreground uppercase font-bold tracking-wider">Total Resumes</p>
            <p className="text-xl font-bold tracking-tight mt-0.5">{data.totalApplications}</p>
          </div>
        </div>

        {/* Card 3: Avg Match Score */}
        <div className="glass-panel p-5 rounded-2xl border border-border flex items-center gap-4">
          <div className="w-10 h-10 rounded-xl bg-emerald-500/10 dark:bg-emerald-600/10 text-emerald-600 dark:text-emerald-400 flex items-center justify-center shrink-0">
            <TrendingUp className="w-5 h-5" />
          </div>
          <div className="min-w-0">
            <p className="text-[10px] text-muted-foreground uppercase font-bold tracking-wider">Avg Match Score</p>
            <p className="text-xl font-bold tracking-tight mt-0.5">{avgSystemScore}%</p>
          </div>
        </div>

        {/* Card 4: Queued/Processing */}
        <div className="glass-panel p-5 rounded-2xl border border-border flex items-center gap-4">
          <div className="w-10 h-10 rounded-xl bg-amber-500/10 dark:bg-amber-600/10 text-amber-600 dark:text-amber-400 flex items-center justify-center shrink-0">
            <Clock className="w-5 h-5" />
          </div>
          <div className="min-w-0">
            <p className="text-[10px] text-muted-foreground uppercase font-bold tracking-wider">Processing Queue</p>
            <p className="text-xl font-bold tracking-tight mt-0.5">{queuedOrProcessing}</p>
          </div>
        </div>
      </div>

      {/* Main Charts Layout */}
      <div className="grid grid-cols-1 lg:grid-cols-10 gap-6 items-start">
        
        {/* Left: Recruitment Funnel Chart (SVG) */}
        <div className="lg:col-span-6 w-full glass-panel p-6 rounded-2xl border border-border flex flex-col gap-6">
          <div>
            <h3 className="font-bold text-base">Application Funnel Conversion</h3>
            <p className="text-xs text-muted-foreground mt-0.5">Progress of candidates through the pipeline funnel.</p>
          </div>

          {/* SVG Custom Funnel Visualizer */}
          <div className="flex flex-col gap-5 py-2">
            {funnelStages.map((stage, idx) => {
              const widthPct = (stage.count / maxStageCount) * 100;
              const conversionRate = idx === 0 ? 100 : Math.round((stage.count / funnelStages[idx - 1].count) * 100) || 0;

              return (
                <div key={stage.label} className="space-y-1.5">
                  <div className="flex justify-between items-center text-xs font-semibold">
                    <span className="text-foreground flex items-center gap-1.5">
                      <span className="text-muted-foreground text-[10px] bg-muted/30 border border-border px-1.5 py-0.5 rounded-md">
                        Step {idx + 1}
                      </span>
                      {stage.label}
                    </span>
                    <div className="flex items-center gap-2">
                      <span className="text-muted-foreground text-[10px]">
                        {idx > 0 && `Conv: ${conversionRate}%`}
                      </span>
                      <span className="font-bold text-foreground text-sm">{stage.count} candidates</span>
                    </div>
                  </div>

                  <div className="relative w-full h-8 bg-muted/20 border border-border/40 rounded-xl overflow-hidden flex items-center px-4">
                    <div
                      className={cn(
                        "absolute left-0 top-0 bottom-0 bg-gradient-to-r transition-all duration-500 rounded-r-lg",
                        stage.color,
                        "opacity-95 dark:opacity-85"
                      )}
                      style={{ width: `${Math.max(widthPct, 2)}%` }}
                    />
                    
                    {/* Value Badge inside the bar if wide enough */}
                    <span className="relative z-10 text-[10px] font-extrabold text-white mix-blend-difference">
                      {Math.round(widthPct)}% of total
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Right: Department breakdown */}
        <div className="lg:col-span-4 w-full glass-panel p-6 rounded-2xl border border-border flex flex-col gap-4">
          <div>
            <h3 className="font-bold text-base">Department Distribution</h3>
            <p className="text-xs text-muted-foreground mt-0.5">Job postings and applications by business unit.</p>
          </div>

          <div className="flex flex-col gap-3 mt-2">
            {data.departments.map((dept) => {
              const totalDeptActive = dept.jobsCount;
              const totalDeptApps = dept.applicationsCount;

              return (
                <div
                  key={dept.department}
                  className="p-3.5 rounded-xl border border-border bg-muted/10 flex items-center justify-between gap-4"
                >
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="w-8 h-8 rounded-lg bg-primary/10 text-primary flex items-center justify-center shrink-0">
                      <Building2 className="w-4 h-4" />
                    </div>
                    <div className="min-w-0">
                      <h4 className="text-xs font-bold text-foreground truncate">{dept.department}</h4>
                      <p className="text-[10px] text-muted-foreground mt-0.5">
                        {totalDeptActive} open role{totalDeptActive !== 1 && 's'}
                      </p>
                    </div>
                  </div>

                  <div className="text-right shrink-0">
                    <span className="text-xs font-extrabold text-foreground">{totalDeptApps} applications</span>
                    <p className="text-[9px] text-muted-foreground mt-0.5">
                      Avg: {Math.round(totalDeptApps / Math.max(totalDeptActive, 1))} per job
                    </p>
                  </div>
                </div>
              );
            })}

            {data.departments.length === 0 && (
              <p className="text-xs text-center text-muted-foreground py-6">No department statistics found.</p>
            )}
          </div>
        </div>
      </div>

      {/* Jobs Performance Leaderboard */}
      <div className="glass-panel p-6 rounded-2xl border border-border w-full flex flex-col gap-4">
        <div>
          <h3 className="font-bold text-base">Job Opening Performance</h3>
          <p className="text-xs text-muted-foreground mt-0.5">Scoring alignment and response breakdown across jobs.</p>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead>
              <tr className="border-b border-border text-[10px] text-muted-foreground uppercase tracking-wider font-bold">
                <th className="pb-3 pt-1 font-bold">Job Role</th>
                <th className="pb-3 pt-1 font-bold">Department</th>
                <th className="pb-3 pt-1 font-bold text-center">Applicants</th>
                <th className="pb-3 pt-1 font-bold text-center">Avg Match Score</th>
                <th className="pb-3 pt-1 font-bold text-center">Shortlisted</th>
                <th className="pb-3 pt-1 font-bold text-center">Status Breakdown</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border/60 text-xs">
              {data.jobsBreakdown.map((job) => {
                const shortlisted = job.statusCounts['Shortlisted'] || 0;
                const rejected = job.statusCounts['Rejected'] || 0;
                const totalScored = job.applicationCount - (job.statusCounts['Queued'] || 0) - (job.statusCounts['Processing'] || 0) - (job.statusCounts['Failed'] || 0);

                return (
                  <tr key={job.jobId} className="hover:bg-muted/5 transition-colors">
                    <td className="py-3.5 font-bold text-foreground max-w-[200px] truncate">{job.title}</td>
                    <td className="py-3.5 text-muted-foreground">{job.department}</td>
                    <td className="py-3.5 text-center font-semibold text-foreground">{job.applicationCount}</td>
                    <td className="py-3.5 text-center">
                      <div className="inline-flex items-center gap-1">
                        <span className={cn(
                          "px-2 py-0.5 rounded-md font-extrabold text-[10px] border",
                          job.averageScore >= 75
                            ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20"
                            : job.averageScore >= 50
                            ? "bg-amber-500/10 text-amber-600 dark:text-amber-400 border-amber-500/20"
                            : "bg-rose-500/10 text-rose-600 dark:text-rose-400 border-rose-500/20"
                        )}>
                          {job.averageScore}%
                        </span>
                      </div>
                    </td>
                    <td className="py-3.5 text-center font-bold text-emerald-600 dark:text-emerald-400">
                      {shortlisted}
                    </td>
                    <td className="py-3.5 text-center">
                      <div className="flex justify-center items-center gap-1 flex-wrap">
                        {/* Custom Micro Chips */}
                        <span className="text-[9px] bg-slate-500/10 text-slate-600 dark:text-slate-400 px-1.5 py-0.5 rounded border border-slate-500/20 font-bold" title="Ingested">
                          {job.applicationCount} Ingest
                        </span>
                        {shortlisted > 0 && (
                          <span className="text-[9px] bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 px-1.5 py-0.5 rounded border border-emerald-500/20 font-bold" title="Shortlisted">
                            {shortlisted} Short
                          </span>
                        )}
                        {rejected > 0 && (
                          <span className="text-[9px] bg-rose-500/10 text-rose-600 dark:text-rose-400 px-1.5 py-0.5 rounded border border-rose-500/20 font-bold" title="Rejected">
                            {rejected} Rej
                          </span>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}

              {data.jobsBreakdown.length === 0 && (
                <tr>
                  <td colSpan={6} className="text-center py-8 text-muted-foreground">
                    No active job opening statistics available.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
