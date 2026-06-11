'use client';

import React from 'react';
import { RefreshCw, FileText, CheckCircle, XCircle, Clock } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useRouter } from 'next/navigation';

export interface CandidateProgress {
  applicationId: string;
  name: string;
  status: 'Queued' | 'Processing' | 'Scored' | 'Failed';
  fitScore?: number;
  errorMessage?: string;
  kitReady?: boolean;
}

interface CandidateStatusRowProps {
  candidate: CandidateProgress;
}

export function CandidateStatusRow({ candidate }: CandidateStatusRowProps) {
  const router = useRouter();

  const statusConfigs = {
    Queued: {
      icon: <Clock className="w-4 h-4 text-slate-600 dark:text-slate-400" />,
      chip: 'bg-slate-500/10 text-slate-600 dark:text-slate-400 border-slate-500/20',
      label: 'Queued',
    },
    Processing: {
      icon: <RefreshCw className="w-4 h-4 text-violet-600 dark:text-violet-400 animate-spin" />,
      chip: 'bg-violet-500/10 text-violet-600 dark:text-violet-400 border-violet-500/20 animate-pulse',
      label: 'Processing',
    },
    Scored: {
      icon: <CheckCircle className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />,
      chip: 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border-emerald-500/20',
      label: 'Scored',
    },
    Failed: {
      icon: <XCircle className="w-4 h-4 text-rose-600 dark:text-rose-400" />,
      chip: 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border-rose-500/20',
      label: 'Failed',
    },
  };

  const config = statusConfigs[candidate.status];

  const getScoreColor = (score: number) => {
    if (score >= 80) return 'text-emerald-600 dark:text-emerald-400 bg-emerald-500/10 border-emerald-500/20';
    if (score >= 60) return 'text-amber-600 dark:text-amber-400 bg-amber-500/10 border-amber-500/20';
    return 'text-rose-600 dark:text-rose-400 bg-rose-500/10 border-rose-500/20';
  };

  return (
    <div className="flex items-center justify-between p-4 rounded-xl border border-border bg-card/40 hover:bg-card/60 gap-4 transition-all animate-in fade-in duration-300">
      
      {/* Candidate Name & Info */}
      <div className="flex items-center gap-3 min-w-0">
        <div className="w-8 h-8 rounded-lg bg-muted flex items-center justify-center font-bold text-muted-foreground text-xs shrink-0">
          {candidate.name.charAt(0)}
        </div>
        
        <div className="flex flex-col min-w-0">
          <span className="text-sm font-semibold text-foreground truncate">{candidate.name}</span>
          {candidate.errorMessage && (
            <span className="text-[10px] text-rose-600 dark:text-rose-400 leading-snug truncate mt-0.5" title={candidate.errorMessage}>
              {candidate.errorMessage}
            </span>
          )}
        </div>
      </div>

      {/* Status & Scores */}
      <div className="flex items-center gap-3 shrink-0">
        
        {/* Fit Score (Scored state only) */}
        {candidate.status === 'Scored' && candidate.fitScore !== undefined && (
          <span className={cn("inline-flex items-center px-2 py-0.5 rounded-lg text-xs font-bold border", getScoreColor(candidate.fitScore))}>
            {Math.round(candidate.fitScore)}% Fit
          </span>
        )}

        {/* Status chip */}
        <span className={cn("inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider border", config.chip)}>
          {config.icon}
          {config.label}
        </span>

        {/* View kit CTA */}
        {candidate.kitReady && (
          <button
            onClick={() => router.push(`/applications/${candidate.applicationId}/kit`)}
            className="inline-flex items-center gap-1.5 px-3 py-1 bg-violet-600 hover:bg-violet-500 text-white rounded-lg text-xs font-bold transition-all shadow shadow-violet-600/10"
          >
            <FileText className="w-3.5 h-3.5" />
            View Kit
          </button>
        )}
      </div>
    </div>
  );
}
