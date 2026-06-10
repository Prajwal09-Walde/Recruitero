'use client';

import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { X, Sparkles, AlertCircle, RefreshCw, ChevronDown, Award } from 'lucide-react';
import apiClient from '@/lib/apiClient';
import { queryKeys } from '@/lib/queryKeys';
import { InterviewKitResult } from '@/types';
import { useAuthStore } from '@/stores/authStore';
import { toast } from '@/components/ui/Toaster';
import { cn } from '@/lib/utils';

interface InterviewKitDrawerProps {
  applicationId: string | null;
  isOpen: boolean;
  onClose: () => void;
}

export function InterviewKitDrawer({ applicationId, isOpen, onClose }: InterviewKitDrawerProps) {
  const queryClient = useQueryClient();
  const user = useAuthStore((s) => s.user);
  const [activeQuestionIdx, setActiveQuestionIdx] = useState<number | null>(null);

  // Fetch Interview Kit Query
  const {
    data: kit,
    isLoading,
    isError,
    error,
    refetch,
  } = useQuery<(InterviewKitResult & { notReady?: boolean }) | null>({
    queryKey: queryKeys.applications.kit(applicationId || ''),
    queryFn: async () => {
      if (!applicationId) return null;
      try {
        const response = await apiClient.get<InterviewKitResult>(
          `/api/applications/${applicationId}/interview-kit`
        );
        return response.data;
      } catch (err: any) {
        if (err.response?.status === 404) {
          // Kit is not ready yet
          return { notReady: true } as any;
        }
        throw err;
      }
    },
    enabled: isOpen && !!applicationId,
    refetchInterval: (query) => {
      // If the kit is marked as not ready, poll every 5 seconds until it is ready
      const data = query.state.data as any;
      if (data && data.notReady) {
        return 5000;
      }
      return false;
    },
  });

  // Regenerate Kit Mutation
  const regenerateMutation = useMutation({
    mutationFn: async () => {
      if (!applicationId) return;
      const response = await apiClient.post(
        `/api/applications/${applicationId}/interview-kit/regenerate`
      );
      return response.data;
    },
    onSuccess: () => {
      toast('Regeneration Queued!', {
        description: 'AI is recreating the interview questions. Please wait...',
        type: 'success',
      });
      // Invalidate query so it shifts back to a polling / notReady state
      queryClient.setQueryData(queryKeys.applications.kit(applicationId || ''), { notReady: true });
      queryClient.invalidateQueries({ queryKey: queryKeys.applications.kit(applicationId || '') });
    },
    onError: (err: any) => {
      toast('Regeneration Failed', {
        description: err.response?.data?.detail || 'An error occurred while queueing regeneration.',
        type: 'error',
      });
    },
  });

  const isHrAdmin = user?.role === 'HRAdmin';

  // Lock scroll on open
  useEffect(() => {
    if (isOpen) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = 'unset';
      setActiveQuestionIdx(null);
    }
    return () => {
      document.body.style.overflow = 'unset';
    };
  }, [isOpen]);

  if (!isOpen) return null;

  const handleRegenerate = () => {
    if (!isHrAdmin) {
      toast('Permission Denied', {
        description: 'Only HR Admins can regenerate interview kits.',
        type: 'error',
      });
      return;
    }
    regenerateMutation.mutate();
  };

  const isKitNotReady = kit && (kit as any).notReady;

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-black/60 backdrop-blur-sm transition-opacity"
        onClick={onClose}
      />

      {/* Slide-out container */}
      <div className="relative w-full max-w-2xl h-full bg-background border-l border-white/5 shadow-2xl flex flex-col z-10 animate-in slide-in-from-right duration-300">
        
        {/* Header */}
        <div className="p-6 border-b border-white/5 flex items-center justify-between gap-4">
          <div className="flex items-center gap-2">
            <Sparkles className="w-5 h-5 text-violet-400" />
            <h3 className="font-bold text-lg">AI Interview Kit</h3>
          </div>
          
          <button
            onClick={onClose}
            className="p-1.5 rounded-lg hover:bg-white/5 text-muted-foreground hover:text-foreground transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-6 custom-scrollbar">
          {isLoading ? (
            <div className="flex flex-col items-center justify-center h-64 gap-3">
              <div className="w-8 h-8 border-3 border-violet-500 border-t-transparent rounded-full animate-spin" />
              <p className="text-xs text-muted-foreground">Fetching interview questions...</p>
            </div>
          ) : isError ? (
            <div className="flex flex-col items-center justify-center p-6 border border-rose-500/20 bg-rose-500/5 rounded-xl gap-2 text-center text-rose-400">
              <AlertCircle className="w-8 h-8" />
              <p className="text-sm font-semibold">Failed to load interview kit</p>
              <p className="text-xs opacity-80 max-w-xs">{(error as any)?.message}</p>
            </div>
          ) : isKitNotReady ? (
            <div className="flex flex-col items-center justify-center p-8 border border-white/5 bg-white/5 rounded-2xl gap-3 text-center h-64">
              <div className="w-6 h-6 border-2 border-violet-500 border-t-transparent rounded-full animate-spin" />
              <h4 className="font-semibold text-sm">AI Interview Kit Generating</h4>
              <p className="text-xs text-muted-foreground max-w-xs leading-relaxed">
                The kit is currently being generated by GPT-4o. This page will update automatically when the kit is ready.
              </p>
            </div>
          ) : kit ? (
            <>
              {/* Profile Card */}
              <div className="flex items-start justify-between p-4 rounded-xl border border-white/5 bg-white/5 gap-4">
                <div className="min-w-0">
                  <h4 className="font-bold text-base text-foreground truncate">{kit.CandidateName}</h4>
                  <p className="text-xs text-muted-foreground truncate">{kit.JobTitle}</p>
                </div>
                <div className="flex items-center gap-1 bg-violet-500/15 border border-violet-500/25 px-2.5 py-1 rounded-lg shrink-0">
                  <Award className="w-3.5 h-3.5 text-violet-400" />
                  <span className="text-xs font-bold text-violet-300">{Math.round(kit.FitScore)}% Fit</span>
                </div>
              </div>

              {/* Accordion List */}
              <div className="space-y-3">
                <span className="text-[10px] font-bold text-muted-foreground uppercase tracking-wider">
                  Targeted Questions ({kit.Questions.length})
                </span>
                
                <div className="space-y-2.5">
                  {kit.Questions.map((q, idx) => {
                    const isActive = activeQuestionIdx === idx;
                    return (
                      <div
                        key={idx}
                        className={cn(
                          "rounded-xl border border-white/5 transition-all overflow-hidden",
                          isActive ? "bg-white/5 border-violet-500/20" : "bg-white/[0.01] hover:bg-white/[0.02]"
                        )}
                      >
                        {/* Header */}
                        <button
                          type="button"
                          onClick={() => setActiveQuestionIdx(isActive ? null : idx)}
                          className="w-full flex items-center justify-between p-4 text-left gap-4"
                        >
                          <div className="flex flex-col gap-1 min-w-0">
                            <div className="flex items-center gap-2 flex-wrap">
                              <span className="text-[10px] font-bold bg-violet-500/10 border border-violet-500/20 text-violet-400 px-2 py-0.5 rounded">
                                {q.Category}
                              </span>
                              <span className="text-[10px] font-bold bg-slate-500/10 border border-slate-500/20 text-slate-400 px-2 py-0.5 rounded">
                                {q.Difficulty}
                              </span>
                            </div>
                            <h5 className="font-semibold text-sm leading-snug text-foreground mt-1 truncate">
                              {q.Question}
                            </h5>
                          </div>
                          <ChevronDown
                            className={cn(
                              "w-4 h-4 text-muted-foreground transition-transform shrink-0",
                              isActive && "rotate-185"
                            )}
                          />
                        </button>

                        {/* Collapsible Content */}
                        {isActive && (
                          <div className="px-4 pb-4 pt-1 border-t border-white/5 bg-black/10 flex flex-col gap-3 animate-in fade-in duration-200">
                            <div className="space-y-1">
                              <span className="text-[10px] font-bold text-violet-400 uppercase tracking-wider">
                                Rationale & What to Listen For
                              </span>
                              <p className="text-xs text-muted-foreground leading-relaxed">
                                {q.Rationale}
                              </p>
                            </div>
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>
            </>
          ) : (
            <div className="text-center text-muted-foreground text-xs p-8">
              No interview kit data found.
            </div>
          )}
        </div>

        {/* Footer */}
        {kit && !isKitNotReady && (
          <div className="p-6 border-t border-white/5 flex items-center justify-between gap-4 bg-black/20">
            <div className="text-xs text-muted-foreground max-w-[280px]">
              {isHrAdmin ? (
                'Review and customize questions before candidate screening.'
              ) : (
                <span className="text-amber-400">Only HR Admins can regenerate kits.</span>
              )}
            </div>
            
            <button
              onClick={handleRegenerate}
              disabled={regenerateMutation.isPending || !isHrAdmin}
              className="inline-flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-bold bg-violet-600 hover:bg-violet-500 text-white disabled:opacity-40 transition-all shadow shadow-violet-600/15"
            >
              <RefreshCw className={cn("w-3.5 h-3.5", regenerateMutation.isPending && "animate-spin")} />
              Regenerate Kit
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
