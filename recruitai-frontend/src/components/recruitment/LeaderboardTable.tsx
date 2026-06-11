'use client';

import React, { useState } from 'react';
import { LeaderboardCandidateDto } from '@/types';
import { ChevronUp, ChevronDown, Award, Eye, AlertCircle, FileText } from 'lucide-react';
import { cn } from '@/lib/utils';

interface LeaderboardTableProps {
  candidates: LeaderboardCandidateDto[];
  isLoading: boolean;
  page: number;
  pageSize: number;
  totalApplicants: number;
  onPageChange: (newPage: number) => void;
  onViewKit: (applicationId: string) => void;
  statusFilter: string;
  onStatusFilterChange: (status: string) => void;
  userRole?: string;
  onSendToRecruiter?: (applicationId: string) => void;
}

type SortField = 'name' | 'fitScore';
type SortOrder = 'asc' | 'desc';

export function LeaderboardTable({
  candidates,
  isLoading,
  page,
  pageSize,
  totalApplicants,
  onPageChange,
  onViewKit,
  statusFilter,
  onStatusFilterChange,
  userRole,
  onSendToRecruiter,
}: LeaderboardTableProps) {
  const [sortField, setSortField] = useState<SortField>('fitScore');
  const [sortOrder, setSortOrder] = useState<SortOrder>('desc');

  const handleSort = (field: SortField) => {
    if (sortField === field) {
      setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc');
    } else {
      setSortField(field);
      setSortOrder('desc');
    }
  };

  // Sort candidate list locally
  const sortedCandidates = [...candidates].sort((a, b) => {
    if (sortField === 'name') {
      return sortOrder === 'asc'
        ? a.name.localeCompare(b.name)
        : b.name.localeCompare(a.name);
    } else {
      return sortOrder === 'asc' ? a.fitScore - b.fitScore : b.fitScore - a.fitScore;
    }
  });

  const getScoreColor = (score: number) => {
    if (score >= 80) return 'bg-emerald-500 text-emerald-600 dark:text-emerald-400';
    if (score >= 60) return 'bg-amber-500 text-amber-600 dark:text-amber-400';
    return 'bg-rose-500 text-rose-600 dark:text-rose-400';
  };

  const getRecommendation = (score: number) => {
    if (score >= 80) return { text: 'Strong Fit', className: 'text-emerald-600 dark:text-emerald-400 bg-emerald-500/10 border-emerald-500/20' };
    if (score >= 60) return { text: 'Potential Fit', className: 'text-amber-600 dark:text-amber-400 bg-amber-500/10 border-amber-500/20' };
    return { text: 'Unaligned', className: 'text-rose-600 dark:text-rose-400 bg-rose-500/10 border-rose-500/20' };
  };

  const totalPages = Math.ceil(totalApplicants / pageSize) || 1;

  return (
    <div className="flex flex-col gap-4 w-full glass-panel p-6 rounded-2xl border border-border">
      <div className="flex items-center justify-between gap-4 flex-wrap">
        <div>
          <h3 className="font-bold text-lg">Fit Score Leaderboard</h3>
          <p className="text-xs text-muted-foreground mt-0.5">Ranked by AI semantic alignment.</p>
        </div>

        {/* Filter Tabs */}
        <div className="flex bg-muted/40 border border-border p-1 rounded-xl">
          {(['All', 'Scored', 'Failed'] as const).map((status) => (
            <button
              key={status}
              type="button"
              onClick={() => onStatusFilterChange(status)}
              className={cn(
                "px-3.5 py-1.5 rounded-lg text-xs font-semibold transition-all",
                statusFilter === status
                  ? "bg-primary text-primary-foreground shadow"
                  : "text-muted-foreground hover:text-foreground"
              )}
            >
              {status}
            </button>
          ))}
        </div>
      </div>

      {/* Table grid */}
      <div className="overflow-x-auto custom-scrollbar">
        <table className="w-full text-left border-collapse min-w-[700px]">
          <thead>
            <tr className="border-b border-white/5 text-xs font-semibold text-muted-foreground uppercase tracking-wider">
              <th className="py-3 px-4 w-16">Rank</th>
              <th
                className="py-3 px-4 cursor-pointer hover:text-foreground select-none"
                onClick={() => handleSort('name')}
              >
                <div className="flex items-center gap-1">
                  Candidate
                  {sortField === 'name' && (
                    sortOrder === 'asc' ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />
                  )}
                </div>
              </th>
              <th
                className="py-3 px-4 cursor-pointer hover:text-foreground select-none"
                onClick={() => handleSort('fitScore')}
              >
                <div className="flex items-center gap-1">
                  Fit Score
                  {sortField === 'fitScore' && (
                    sortOrder === 'asc' ? <ChevronUp className="w-3.5 h-3.5" /> : <ChevronDown className="w-3.5 h-3.5" />
                  )}
                </div>
              </th>
              <th className="py-3 px-4">Recommendation</th>
              <th className="py-3 px-4">Status</th>
              <th className="py-3 px-4 text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-white/5 text-sm">
            {isLoading ? (
              // Loading skeletons
              Array.from({ length: 5 }).map((_, idx) => (
                <tr key={`skel-${idx}`} className="animate-pulse">
                  <td className="py-4 px-4"><div className="h-4 bg-white/5 rounded w-8" /></td>
                  <td className="py-4 px-4"><div className="h-4 bg-white/5 rounded w-32" /></td>
                  <td className="py-4 px-4">
                    <div className="flex items-center gap-2">
                      <div className="h-3 bg-white/5 rounded-full w-24" />
                      <div className="h-4 bg-white/5 rounded w-8" />
                    </div>
                  </td>
                  <td className="py-4 px-4"><div className="h-5 bg-white/5 rounded w-20" /></td>
                  <td className="py-4 px-4"><div className="h-5 bg-white/5 rounded w-16" /></td>
                  <td className="py-4 px-4 text-right"><div className="h-8 bg-white/5 rounded w-20 ml-auto" /></td>
                </tr>
              ))
            ) : sortedCandidates.length === 0 ? (
              <tr>
                <td colSpan={6} className="py-12 text-center text-muted-foreground">
                  <div className="flex flex-col items-center gap-2">
                    <AlertCircle className="w-8 h-8 opacity-20" />
                    <span>No candidates found matching criteria.</span>
                  </div>
                </td>
              </tr>
            ) : (
              sortedCandidates.map((cand) => {
                const rec = getRecommendation(cand.fitScore);
                const scoreColor = getScoreColor(cand.fitScore);

                return (
                  <tr key={cand.applicationId} className="group hover:bg-muted/30 transition-colors">
                    <td className="py-4 px-4 font-bold text-muted-foreground">
                      {cand.rank <= 3 ? (
                        <span className="flex items-center gap-1 text-amber-600 dark:text-amber-400 font-extrabold">
                          <Award className="w-4 h-4 shrink-0" />
                          {cand.rank}
                        </span>
                      ) : (
                        `#${cand.rank}`
                      )}
                    </td>
                    <td className="py-4 px-4">
                      <div className="font-semibold text-foreground">{cand.name}</div>
                    </td>
                    <td className="py-4 px-4">
                      <div className="flex items-center gap-2.5 min-w-[150px]">
                        <div className="w-full bg-muted rounded-full h-2 overflow-hidden border border-border">
                          <div
                            className={cn("h-full rounded-full transition-all duration-500", scoreColor.split(' ')[0])}
                            style={{ width: `${cand.fitScore}%` }}
                          />
                        </div>
                        <span className={cn("text-xs font-bold shrink-0 tabular-nums", scoreColor.split(' ').slice(1).join(' '))}>
                          {Math.round(cand.fitScore)}%
                        </span>
                      </div>
                    </td>
                    <td className="py-4 px-4">
                      <span className={cn("inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold border", rec.className)}>
                        {rec.text}
                      </span>
                    </td>
                     <td className="py-4 px-4">
                      <span
                        className={cn(
                          "inline-flex items-center px-2.5 py-0.5 rounded-md text-xs font-semibold uppercase tracking-wider",
                          cand.status === 'Scored' && 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20',
                          cand.status === 'SentToRecruiter' && 'bg-blue-500/10 text-blue-600 dark:text-blue-400 border border-blue-500/20',
                          cand.status === 'Shortlisted' && 'bg-teal-500/10 text-teal-600 dark:text-teal-400 border border-teal-500/20',
                          cand.status === 'Rejected' && 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/20',
                          cand.status === 'Failed' && 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/20',
                          cand.status === 'Processing' && 'bg-violet-500/10 text-violet-600 dark:text-violet-400 border border-violet-500/20 animate-pulse',
                          cand.status === 'Queued' && 'bg-slate-500/10 text-slate-600 dark:text-slate-400 border border-slate-500/20'
                        )}
                      >
                        {cand.status === 'SentToRecruiter' ? 'Sent to Recruiter' : cand.status}
                      </span>
                    </td>
                    <td className="py-4 px-4 text-right">
                      <div className="flex items-center justify-end gap-2">
                        {userRole === 'HRAdmin' && cand.status === 'Scored' && onSendToRecruiter && (
                          <button
                            type="button"
                            onClick={() => onSendToRecruiter(cand.applicationId)}
                            className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-bold border border-blue-500/20 bg-blue-500/5 hover:bg-blue-600 text-blue-600 dark:text-blue-300 hover:text-white transition-all shadow-sm"
                          >
                            Send to Recruiter
                          </button>
                        )}
                        {['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(cand.status) ? (
                          <button
                            type="button"
                            onClick={() => onViewKit(cand.applicationId)}
                            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-bold border border-violet-500/20 bg-violet-500/5 hover:bg-violet-600 text-violet-600 dark:text-violet-300 hover:text-white transition-all shadow-sm"
                          >
                            <Eye className="w-3.5 h-3.5" />
                            View Kit
                          </button>
                        ) : (
                          <span className="text-xs text-muted-foreground italic">Kit not ready</span>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination controls */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between border-t border-border pt-4">
          <span className="text-xs text-muted-foreground">
            Page {page} of {totalPages}
          </span>
          <div className="flex items-center gap-2">
            <button
              onClick={() => onPageChange(page - 1)}
              disabled={page === 1}
              className="px-3 py-1 text-xs font-semibold rounded-lg bg-muted/40 border border-border disabled:opacity-40 transition-colors"
            >
              Previous
            </button>
            <button
              onClick={() => onPageChange(page + 1)}
              disabled={page === totalPages}
              className="px-3 py-1 text-xs font-semibold rounded-lg bg-muted/40 border border-border disabled:opacity-40 transition-colors"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
