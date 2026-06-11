'use client';

import React, { useState, useEffect } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useQueryClient } from '@tanstack/react-query';
import { useJobDetails, useJobLeaderboard, useUpdateApplicationStatus } from '@/hooks/useJobLeaderboard';
import { useRecruitmentHub } from '@/hooks/useRecruitmentHub';
import { LeaderboardTable } from '@/components/recruitment/LeaderboardTable';
import { KanbanBoard } from '@/components/recruitment/KanbanBoard';
import { InterviewKitDrawer } from '@/components/recruitment/InterviewKitDrawer';
import { toast } from '@/components/ui/Toaster';
import { queryKeys } from '@/lib/queryKeys';
import { Upload, Calendar, Building2, AlertTriangle, RefreshCw, CheckCircle, XCircle, FileUp, Sparkles, FileText, ArrowLeft } from 'lucide-react';
import { cn } from '@/lib/utils';
import { useAuthStore } from '@/stores/authStore';
import { useUpload } from '@/hooks/useUpload';
import { useDropzone } from 'react-dropzone';

export default function JobDetailPage() {
  const params = useParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const jobId = params.id as string;

  // User role details
  const { user } = useAuthStore();
  const isViewer = user?.role === 'Viewer';
  const isRecruiter = user?.role === 'Recruiter';
  const isHrAdmin = user?.role === 'HRAdmin';

  // Apply state (for Viewer)
  const [files, setFiles] = useState<File[]>([]);
  const { upload, progress, isUploading, isUploaded } = useUpload();

  const onDrop = React.useCallback((acceptedFiles: File[]) => {
    const pdfs = acceptedFiles.filter(f => f.type === 'application/pdf');
    if (pdfs.length > 0) {
      setFiles(pdfs.slice(0, 1));
    } else {
      toast('Invalid file type', {
        description: 'Only PDF files are accepted.',
        type: 'error',
      });
    }
  }, []);

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: { 'application/pdf': ['.pdf'] },
    maxFiles: 1,
    disabled: isUploading || isUploaded,
  });

  const handleApply = async () => {
    if (files.length === 0) return;
    try {
      await upload(jobId, files);
      toast('Application Submitted!', {
        description: 'Your resume has been uploaded successfully.',
        type: 'success',
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard.all });
    } catch (err) {
      // Handled in hook
    }
  };

  // Pagination and status filter states
  const [page, setPage] = useState(1);
  const [pageSize] = useState(10);
  const [statusFilter, setStatusFilter] = useState('All');
  
  // Real-time counter of candidates being processed
  const [processingCount, setProcessingCount] = useState(0);

  // Active candidate for Interview Kit Drawer
  const [selectedAppId, setSelectedAppId] = useState<string | null>(null);
  const [isDrawerOpen, setIsDrawerOpen] = useState(false);

  // Fetch job details and leaderboard data
  const { data: job, isLoading: isJobLoading } = useJobDetails(jobId);
  const { data: leaderboard, isLoading: isLeaderboardLoading } = useJobLeaderboard(jobId, {
    status: statusFilter,
    page,
    pageSize,
  });

  const activeFilters = { status: statusFilter, page, pageSize };
  const updateStatusMutation = useUpdateApplicationStatus(jobId, activeFilters);

  // SignalR real-time event hook
  const { isReconnecting } = useRecruitmentHub(jobId, {
    onResumeUploaded: (appId, name) => {
      toast('Resume Uploaded', {
        description: `${name} has been added to the queue.`,
        type: 'info',
      });
      // Invalidate queries to fetch new counts / candidates
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard.all });
    },
    onProcessingStarted: (appId, name) => {
      setProcessingCount((c) => c + 1);
      toast('Processing Started', {
        description: `AI started parsing and scoring ${name}'s resume.`,
        type: 'info',
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard.all });
    },
    onFitScoreReady: (appId, name, score) => {
      setProcessingCount((c) => Math.max(0, c - 1));
      toast('Fit Score Computed', {
        description: `AI computed ${Math.round(score)}% match for ${name}.`,
        type: 'success',
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard.all });
    },
    onInterviewKitReady: (appId) => {
      // Invalidate interview kit cache
      queryClient.invalidateQueries({ queryKey: queryKeys.applications.kit(appId) });
    },
    onProcessingFailed: (appId, name, err) => {
      setProcessingCount((c) => Math.max(0, c - 1));
      toast('Processing Failed', {
        description: `${name} failed parsing: ${err}`,
        type: 'error',
      });
      queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard.all });
    },
  });

  const handleStatusChange = (applicationId: string, nextStatus: string) => {
    updateStatusMutation.mutate({ applicationId, status: nextStatus });
  };

  const handleSendToRecruiter = (applicationId: string) => {
    updateStatusMutation.mutate({ applicationId, status: 'SentToRecruiter' });
    toast('Sent to Recruiter', {
      description: 'Candidate has been sent to the recruiter successfully.',
      type: 'success',
    });
  };

  const handleOpenKitDrawer = (applicationId: string) => {
    setSelectedAppId(applicationId);
    setIsDrawerOpen(true);
  };

  if (isJobLoading) {
    return (
      <div className="flex flex-col gap-6 items-center justify-center min-h-[400px]">
        <div className="w-8 h-8 border-3 border-violet-500 border-t-transparent rounded-full animate-spin" />
        <p className="text-xs text-muted-foreground">Loading job details dashboard...</p>
      </div>
    );
  }

  if (!job) {
    return (
      <div className="flex flex-col items-center justify-center p-8 border border-border bg-card/60 rounded-2xl gap-3 text-center min-h-[300px]">
        <AlertTriangle className="w-8 h-8 text-amber-500" />
        <h4 className="font-semibold text-sm">Job Posting Not Found</h4>
        <p className="text-xs text-muted-foreground">The job opening may have been deleted or archived.</p>
        <button
          onClick={() => router.push('/jobs/new')}
          className="bg-violet-600 hover:bg-violet-500 text-white rounded-lg px-4 py-2 text-xs font-semibold"
        >
          Create Job Posting
        </button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6 w-full animate-in fade-in duration-300">
      
      {/* Reconnecting banner */}
      {isReconnecting && (
        <div className="bg-amber-500/10 border border-amber-500/20 text-amber-600 dark:text-amber-400 p-3 rounded-xl flex items-center gap-2 text-xs leading-none">
          <RefreshCw className="w-4 h-4 animate-spin shrink-0" />
          <span>Real-time connection interrupted. Reconnecting and syncing pipeline...</span>
        </div>
      )}

      {/* 1. Job Header Bar */}
      <div className="glass-panel p-6 rounded-2xl border border-border flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
        <div className="flex flex-col gap-1.5 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-2xl font-bold tracking-tight text-foreground truncate">
              {job.title}
            </h1>
            <span
              className={cn(
                "inline-flex items-center px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider",
                job.isActive ? "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20" : "bg-slate-500/10 text-slate-600 dark:text-slate-400 border border-slate-500/20"
              )}
            >
              {job.isActive ? 'Active' : 'Closed'}
            </span>
          </div>
          
          <div className="flex items-center gap-4 text-xs text-muted-foreground flex-wrap">
            <span className="flex items-center gap-1">
              <Building2 className="w-3.5 h-3.5" /> {job.department}
            </span>
            <span className="flex items-center gap-1">
              <Calendar className="w-3.5 h-3.5" /> Opened on {new Date(job.createdAt).toLocaleDateString()}
            </span>
          </div>
        </div>

        {/* Action Counters & CTA */}
        <div className="flex items-center gap-3 w-full sm:w-auto shrink-0 flex-wrap">
          {/* Live Processing counter */}
          {processingCount > 0 && (
            <div className="flex items-center gap-2 bg-violet-500/10 dark:bg-violet-500/15 border border-violet-500/20 dark:border-violet-500/25 px-3.5 py-2 rounded-xl text-xs font-semibold animate-pulse text-violet-600 dark:text-violet-300">
              <RefreshCw className="w-3.5 h-3.5 animate-spin" />
              <span>{processingCount} being processed</span>
            </div>
          )}

          {isHrAdmin && (
            <button
              onClick={() => router.push(`/jobs/${jobId}/upload`)}
              className="flex-1 sm:flex-initial inline-flex items-center justify-center gap-2 px-4 py-2.5 rounded-xl text-xs font-bold bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white shadow-md shadow-violet-600/10 transition-all"
            >
              <Upload className="w-4 h-4" />
              Upload Resumes
            </button>
          )}
        </div>
      </div>

      {/* Split Dashboard Content / Viewer View */}
      {isViewer ? (
        <div className="grid grid-cols-1 lg:grid-cols-10 gap-6 items-start">
          {/* Left: Job Description (60% equivalent) */}
          <div className="lg:col-span-6 w-full glass-panel p-6 rounded-2xl border border-border flex flex-col gap-4">
            <div>
              <h3 className="font-bold text-lg">Job Description</h3>
              <p className="text-xs text-muted-foreground mt-0.5">Please review the role details and requirements.</p>
            </div>
            
            <div className="text-sm text-muted-foreground leading-relaxed whitespace-pre-line mt-2">
              {job.description}
            </div>
          </div>

          {/* Right: Application Portal / Status (40% equivalent) */}
          <div className="lg:col-span-4 w-full">
            {leaderboard && leaderboard.candidates.length > 0 ? (
              /* ── STATUS CARD ── */
              (() => {
                const userApplication = leaderboard.candidates[0];
                return (
                  <div className="glass-panel p-6 rounded-2xl border border-border flex flex-col gap-4">
                    <h3 className="font-bold text-sm text-foreground uppercase tracking-wider">Application Status</h3>
                    
                    <div className="flex items-center gap-3 bg-muted/20 border border-border p-4 rounded-xl">
                      <div className="w-12 h-12 rounded-xl bg-violet-500/10 dark:bg-violet-600/10 flex items-center justify-center font-bold text-violet-600 dark:text-violet-400 text-lg">
                        {userApplication.fitScore ? `${Math.round(userApplication.fitScore)}%` : '--'}
                      </div>
                      <div className="flex-1 min-w-0">
                        <p className="text-xs text-muted-foreground uppercase tracking-wider font-semibold">Match Score</p>
                        <p className="text-sm font-bold text-foreground">
                          {userApplication.fitScore ? 'AI Semantic Match Computed' : 'Calculating match alignment...'}
                        </p>
                      </div>
                    </div>

                    {/* Progress Steps */}
                    <div className="flex flex-col gap-4 mt-2">
                      {/* Step 1: Resume Uploaded */}
                      <div className="flex gap-3">
                        <div className="flex flex-col items-center">
                          <div className="w-5 h-5 rounded-full bg-emerald-500 flex items-center justify-center text-[10px] font-bold text-black">✓</div>
                          <div className="w-0.5 h-10 bg-emerald-500/30" />
                        </div>
                        <div>
                          <h4 className="text-xs font-bold text-foreground">Resume Submitted</h4>
                          <p className="text-[11px] text-muted-foreground">Your application was received successfully.</p>
                        </div>
                      </div>

                      {/* Step 2: AI Parsing & Scoring */}
                      <div className="flex gap-3">
                        <div className="flex flex-col items-center">
                          <div className={cn(
                            "w-5 h-5 rounded-full flex items-center justify-center text-[10px] font-bold",
                            ['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status)
                              ? "bg-emerald-500 text-black"
                              : userApplication.status === 'Processing'
                              ? "bg-violet-500 text-white animate-pulse"
                              : "bg-muted text-muted-foreground"
                          )}>
                            {['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status) ? '✓' : '2'}
                          </div>
                          <div className={cn(
                            "w-0.5 h-10",
                            ['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status)
                              ? "bg-emerald-500/30"
                              : "bg-muted"
                          )} />
                        </div>
                        <div>
                          <h4 className="text-xs font-bold text-foreground">AI Match Recommendation</h4>
                          <p className="text-[11px] text-muted-foreground">
                            {userApplication.status === 'Queued' && 'In queue for processing...'}
                            {userApplication.status === 'Processing' && 'AI parsing and extracting skills...'}
                            {['Scored', 'SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status) && 'AI semantic scoring complete.'}
                            {userApplication.status === 'Failed' && 'AI parsing failed. Please retry.'}
                          </p>
                        </div>
                      </div>

                      {/* Step 3: Recruiter Review */}
                      <div className="flex gap-3">
                        <div className="flex flex-col items-center">
                          <div className={cn(
                            "w-5 h-5 rounded-full flex items-center justify-center text-[10px] font-bold",
                            ['SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status)
                              ? "bg-emerald-500 text-black"
                              : "bg-muted text-muted-foreground"
                          )}>
                            {['SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status) ? '✓' : '3'}
                          </div>
                          <div className={cn(
                            "w-0.5 h-10",
                            ['SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status)
                              ? "bg-emerald-500/30"
                              : "bg-muted"
                          )} />
                        </div>
                        <div>
                          <h4 className="text-xs font-bold text-foreground">Recruiter Review</h4>
                          <p className="text-[11px] text-muted-foreground">
                            {['SentToRecruiter'].includes(userApplication.status) && 'Sent to Recruiter. Under review...'}
                            {['Shortlisted', 'Rejected'].includes(userApplication.status) && 'Review complete.'}
                            {!['SentToRecruiter', 'Shortlisted', 'Rejected'].includes(userApplication.status) && 'Awaiting review dispatch.'}
                          </p>
                        </div>
                      </div>

                      {/* Step 4: Decision */}
                      <div className="flex gap-3">
                        <div className="flex items-center justify-center">
                          <div className={cn(
                            "w-5 h-5 rounded-full flex items-center justify-center text-[10px] font-bold",
                            userApplication.status === 'Shortlisted'
                              ? "bg-emerald-500 text-black"
                              : userApplication.status === 'Rejected'
                              ? "bg-rose-500 text-white"
                              : "bg-muted text-muted-foreground"
                          )}>
                            {userApplication.status === 'Shortlisted' ? '✓' : userApplication.status === 'Rejected' ? '✗' : '4'}
                          </div>
                        </div>
                        <div>
                          <h4 className="text-xs font-bold text-foreground">Application Outcome</h4>
                          <p className="text-[11px] text-muted-foreground">
                            {userApplication.status === 'Shortlisted' && 'Congratulations! You have been shortlisted.'}
                            {userApplication.status === 'Rejected' && 'Thank you for your interest. We are proceeding with other candidates.'}
                            {!['Shortlisted', 'Rejected'].includes(userApplication.status) && 'Decision pending.'}
                          </p>
                        </div>
                      </div>
                    </div>
                  </div>
                );
              })()
            ) : (
              /* ── APPLY PORTAL ── */
              <div className="glass-panel p-6 rounded-2xl border border-border flex flex-col gap-4">
                <div>
                  <h3 className="font-bold text-sm text-foreground uppercase tracking-wider">Apply for this Role</h3>
                  <p className="text-xs text-muted-foreground mt-0.5">Submit your resume to align your skills.</p>
                </div>

                {!isUploaded ? (
                  <div className="flex flex-col gap-4">
                    <div
                      {...getRootProps()}
                      className={cn(
                        "border border-dashed rounded-xl p-6 text-center cursor-pointer transition-all flex flex-col items-center justify-center h-40 gap-2",
                        isDragActive
                          ? "border-violet-500 bg-violet-500/5"
                          : "border-border hover:border-violet-500/30 hover:bg-muted/10"
                      )}
                    >
                      <input {...getInputProps()} />
                      <div className="w-8 h-8 rounded-full bg-violet-500/10 flex items-center justify-center">
                        <FileUp className="w-4 h-4 text-violet-600 dark:text-violet-400" />
                      </div>
                      <div className="space-y-0.5">
                        <p className="text-xs font-bold">Drag & drop your resume, or browse</p>
                        <p className="text-[10px] text-muted-foreground">PDF format · Max 5MB</p>
                      </div>
                    </div>

                    {files.length > 0 && (
                      <div className="p-3 rounded-lg border border-border bg-muted/20 flex items-center justify-between">
                        <div className="flex items-center gap-2 min-w-0">
                          <FileText className="w-4 h-4 text-violet-600 dark:text-violet-400 shrink-0" />
                          <span className="text-xs font-semibold truncate text-foreground">{files[0].name}</span>
                        </div>
                        <button
                          type="button"
                          onClick={() => setFiles([])}
                          className="text-xs text-rose-600 dark:text-rose-400 hover:underline shrink-0"
                        >
                          Remove
                        </button>
                      </div>
                    )}

                    {isUploading ? (
                      <div className="space-y-2 pt-1">
                        <div className="flex items-center justify-between text-xs font-bold">
                          <span>Uploading Resume...</span>
                          <span className="tabular-nums">{progress}%</span>
                        </div>
                        <div className="w-full bg-muted rounded-full h-1.5 overflow-hidden border border-border">
                          <div
                            className="bg-primary h-full rounded-full transition-all duration-300"
                            style={{ width: `${progress}%` }}
                          />
                        </div>
                      </div>
                    ) : (
                      <button
                        type="button"
                        onClick={handleApply}
                        disabled={files.length === 0}
                        className="w-full bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-2.5 font-bold text-xs shadow-lg transition-all disabled:opacity-50"
                      >
                        Submit Application
                      </button>
                    )}
                  </div>
                ) : (
                  <div className="flex flex-col items-center justify-center p-4 border border-dashed border-border rounded-xl text-center min-h-[160px] gap-2">
                    <CheckCircle className="w-8 h-8 text-emerald-600 dark:text-emerald-400 animate-bounce" />
                    <span className="text-xs font-bold">Application Received</span>
                    <span className="text-[10px] text-muted-foreground max-w-[180px]">
                      Loading status card. AI engine is currently processing your fit score...
                    </span>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      ) : (
        /* ── ADMIN/RECRUITER DASHBOARD CONTENT ── */
        <div className="grid grid-cols-1 lg:grid-cols-10 gap-6 items-start">
          {/* Left: Leaderboard (60% equivalent) */}
          <div className="lg:col-span-6 w-full">
            <LeaderboardTable
              candidates={leaderboard?.candidates || []}
              isLoading={isLeaderboardLoading}
              page={page}
              pageSize={pageSize}
              totalApplicants={leaderboard?.totalApplicants || 0}
              onPageChange={setPage}
              onViewKit={handleOpenKitDrawer}
              statusFilter={statusFilter}
              onStatusFilterChange={(filter) => {
                setStatusFilter(filter);
                setPage(1); // Reset page on filter change
              }}
              userRole={user?.role}
              onSendToRecruiter={handleSendToRecruiter}
            />
          </div>

          {/* Right: Kanban Pipeline (40% equivalent) */}
          <div className="lg:col-span-4 w-full">
            <KanbanBoard
              candidates={leaderboard?.candidates || []}
              onStatusChange={handleStatusChange}
              userRole={user?.role}
            />
          </div>
        </div>
      )}

      {/* Slide-out Interview Kit Sheet */}
      <InterviewKitDrawer
        applicationId={selectedAppId}
        isOpen={isDrawerOpen}
        onClose={() => setIsDrawerOpen(false)}
      />
    </div>
  );
}
