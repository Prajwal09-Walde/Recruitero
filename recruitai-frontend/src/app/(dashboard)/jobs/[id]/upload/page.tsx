'use client';

import React, { useCallback, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useDropzone } from 'react-dropzone';
import { useUpload } from '@/hooks/useUpload';
import { useRecruitmentHub } from '@/hooks/useRecruitmentHub';
import { CandidateStatusRow, CandidateProgress } from '@/components/recruitment/CandidateStatusRow';
import { toast } from '@/components/ui/Toaster';
import { FileUp, File, X, Sparkles, AlertCircle, ArrowLeft, RefreshCw } from 'lucide-react';
import { cn } from '@/lib/utils';

export default function ResumeUploadPage() {
  const params = useParams();
  const router = useRouter();
  const jobId = params.id as string;

  const [files, setFiles] = useState<File[]>([]);
  const [fileErrors, setFileErrors] = useState<{ name: string; error: string }[]>([]);

  // List of candidates currently processing
  const [candidates, setCandidates] = useState<CandidateProgress[]>([]);

  const { upload, progress, isUploading, isUploaded, reset: resetUpload } = useUpload();

  // Dropzone setup
  const onDrop = useCallback((acceptedFiles: File[], rejectedFiles: any[]) => {
    // PDF, DOCX, TXT, ZIP, max 20 files, 5MB each
    const errorsList: { name: string; error: string }[] = [];
    
    const validFiles = acceptedFiles.filter((file) => {
      const ext = file.name.split('.').pop()?.toLowerCase();
      const isValid = ['pdf', 'docx', 'txt', 'zip'].includes(ext || '');
      if (!isValid) {
        errorsList.push({ name: file.name, error: 'Only PDF, DOCX, TXT, and ZIP files are accepted' });
        return false;
      }
      if (file.size > 5 * 1024 * 1024) {
        errorsList.push({ name: file.name, error: 'File size exceeds 5MB limit' });
        return false;
      }
      return true;
    });

    rejectedFiles.forEach((rej) => {
      const isSize = rej.errors.some((e: any) => e.code === 'file-too-large');
      const isType = rej.errors.some((e: any) => e.code === 'file-invalid-type');
      errorsList.push({
        name: rej.file.name,
        error: isSize
          ? 'File size exceeds 5MB limit'
          : isType
          ? 'Only PDF, DOCX, TXT, and ZIP files are accepted'
          : 'Invalid file',
      });
    });

    setFileErrors(errorsList);

    setFiles((prev) => {
      const combined = [...prev, ...validFiles];
      if (combined.length > 20) {
        toast('Limit Exceeded', {
          description: 'You can upload a maximum of 20 files at once.',
          type: 'error',
        });
        return combined.slice(0, 20);
      }
      return combined;
    });
  }, []);

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: {
      'application/pdf': ['.pdf'],
      'application/vnd.openxmlformats-officedocument.wordprocessingml.document': ['.docx'],
      'text/plain': ['.txt'],
      'application/zip': ['.zip'],
      'application/x-zip-compressed': ['.zip'],
    },
    maxSize: 5 * 1024 * 1024,
    multiple: true,
  });

  const removeFile = (index: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== index));
  };

  const handleUploadSubmit = async () => {
    if (files.length === 0) return;

    try {
      const result = await upload(jobId, files);
      const applicationIds: string[] = result.applicationIds || [];

      // Map local files to returned application IDs in Queued state
      const initialProgress: CandidateProgress[] = files.map((file, idx) => {
        const candidateName = file.name
          .replace('.pdf', '')
          .replace(/_/g, ' ')
          .replace(/-/g, ' ');

        return {
          applicationId: applicationIds[idx] || `mock-${idx}`,
          name: candidateName,
          status: 'Queued',
        };
      });

      setCandidates(initialProgress);
      toast('Upload Complete!', {
        description: 'Successfully uploaded resumes. Initiating AI processing pipeline.',
        type: 'success',
      });
    } catch (err) {
      // Handled in upload hook
    }
  };

  // SignalR real-time processing listener
  const { isReconnecting } = useRecruitmentHub(isUploaded ? jobId : null, {
    onResumeUploaded: (appId, name) => {
      setCandidates((prev) => {
        if (prev.some((c) => c.applicationId === appId)) return prev;
        return [...prev, { applicationId: appId, name, status: 'Queued' }];
      });
    },
    onProcessingStarted: (appId, name) => {
      setCandidates((prev) => {
        if (prev.some((c) => c.applicationId === appId)) {
          return prev.map((c) => (c.applicationId === appId ? { ...c, status: 'Processing', name } : c));
        }
        return [...prev, { applicationId: appId, name, status: 'Processing' }];
      });
    },
    onFitScoreReady: (appId, name, score) => {
      setCandidates((prev) => {
        if (prev.some((c) => c.applicationId === appId)) {
          return prev.map((c) =>
            c.applicationId === appId ? { ...c, status: 'Scored', fitScore: score, name } : c
          );
        }
        return [...prev, { applicationId: appId, name, status: 'Scored', fitScore: score }];
      });
    },
    onInterviewKitReady: (appId) => {
      setCandidates((prev) =>
        prev.map((c) => (c.applicationId === appId ? { ...c, kitReady: true } : c))
      );
    },
    onProcessingFailed: (appId, name, err) => {
      setCandidates((prev) => {
        if (prev.some((c) => c.applicationId === appId)) {
          return prev.map((c) =>
            c.applicationId === appId ? { ...c, status: 'Failed', errorMessage: err, name } : c
          );
        }
        return [...prev, { applicationId: appId, name, status: 'Failed', errorMessage: err }];
      });
    },
  });

  // Sorting: Processing first, then Scored (score DESC), then Queued, Failed last
  const sortedCandidates = [...candidates].sort((a, b) => {
    const statusOrder = { Processing: 0, Scored: 1, Queued: 2, Failed: 3 };
    const orderA = statusOrder[a.status];
    const orderB = statusOrder[b.status];

    if (orderA !== orderB) {
      return orderA - orderB;
    }

    if (a.status === 'Scored' && b.status === 'Scored') {
      return (b.fitScore || 0) - (a.fitScore || 0);
    }

    return a.name.localeCompare(b.name);
  });

  const allProcessed = candidates.length > 0 && candidates.every((c) => c.status === 'Scored' || c.status === 'Failed');

  return (
    <div className="flex flex-col gap-6 w-full max-w-3xl mx-auto py-4">
      {/* Reconnect Banner */}
      {isReconnecting && (
        <div className="bg-amber-500/10 border border-amber-500/20 text-amber-400 p-3 rounded-xl flex items-center gap-2 text-xs leading-none">
          <RefreshCw className="w-4 h-4 animate-spin shrink-0" />
          <span>Connection interrupted. Reconnecting and syncing live processing statuses...</span>
        </div>
      )}

      {/* Header */}
      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <button
            onClick={() => router.push(`/jobs/${jobId}`)}
            className="p-2 border border-white/5 bg-white/5 rounded-xl hover:text-white transition-colors"
          >
            <ArrowLeft className="w-4 h-4" />
          </button>
          <div>
            <h1 className="text-2xl font-bold tracking-tight">Bulk Resume Upload</h1>
            <p className="text-xs text-muted-foreground mt-0.5">
              Upload PDF resumes to parse, index, and score candidates.
            </p>
          </div>
        </div>
      </div>

      {!isUploaded ? (
        /* ── UPLOAD STATE SCREEN ── */
        <div className="flex flex-col gap-6">
          {/* Dropzone container */}
          <div
            {...getRootProps()}
            className={cn(
              "border-2 border-dashed rounded-2xl p-8 text-center cursor-pointer transition-all flex flex-col items-center justify-center h-52 gap-3",
              isDragActive
                ? "border-violet-500 bg-violet-500/5"
                : "border-white/10 hover:border-violet-500/30 hover:bg-white/[0.01]"
            )}
          >
            <input {...getInputProps()} />
            <div className="w-10 h-10 rounded-full bg-violet-500/10 flex items-center justify-center">
              <FileUp className="w-5 h-5 text-violet-400" />
            </div>
            <div className="space-y-1">
              <p className="text-sm font-semibold">Drag & drop candidate resumes, or click to browse</p>
              <p className="text-xs text-muted-foreground">PDF only · Max 20 files · Up to 5MB each</p>
            </div>
          </div>

          {/* Inline file errors */}
          {fileErrors.length > 0 && (
            <div className="flex flex-col gap-2 p-4 rounded-xl border border-rose-500/10 bg-rose-500/5">
              <span className="text-xs font-semibold text-rose-400 flex items-center gap-1.5">
                <AlertCircle className="w-4 h-4" /> Some files could not be added:
              </span>
              <div className="flex flex-col gap-1 mt-1 max-h-[120px] overflow-y-auto custom-scrollbar">
                {fileErrors.map((err, idx) => (
                  <span key={idx} className="text-xs text-muted-foreground truncate">
                    <strong className="text-foreground">{err.name}</strong> — {err.error}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* Selected files list */}
          {files.length > 0 && (
            <div className="glass-panel p-5 rounded-2xl flex flex-col gap-3.5">
              <div className="flex items-center justify-between border-b border-white/5 pb-2">
                <span className="text-xs font-bold text-muted-foreground uppercase tracking-wider">
                  Files Selected ({files.length})
                </span>
                <button
                  type="button"
                  onClick={() => setFiles([])}
                  className="text-xs text-rose-400 hover:underline"
                >
                  Clear all
                </button>
              </div>

              <div className="flex flex-col gap-2 max-h-[240px] overflow-y-auto custom-scrollbar pr-1">
                {files.map((file, index) => (
                  <div
                    key={index}
                    className="flex items-center justify-between p-3 rounded-lg border border-white/5 bg-white/[0.01]"
                  >
                    <div className="flex items-center gap-2.5 min-w-0">
                      <File className="w-4 h-4 text-violet-400 shrink-0" />
                      <span className="text-xs font-semibold truncate text-foreground">{file.name}</span>
                      <span className="text-[10px] text-muted-foreground shrink-0">
                        ({(file.size / (1024 * 1024)).toFixed(2)} MB)
                      </span>
                    </div>
                    <button
                      type="button"
                      onClick={() => removeFile(index)}
                      className="text-muted-foreground hover:text-rose-400 p-0.5 rounded-md hover:bg-white/5 transition-all"
                    >
                      <X className="w-4 h-4" />
                    </button>
                  </div>
                ))}
              </div>

              {/* Progress bar and upload submit */}
              {isUploading ? (
                <div className="space-y-2 pt-2">
                  <div className="flex items-center justify-between text-xs font-bold">
                    <span>Uploading Resumes...</span>
                    <span className="tabular-nums">{progress}%</span>
                  </div>
                  <div className="w-full bg-white/5 rounded-full h-2 overflow-hidden border border-white/5">
                    <div
                      className="bg-primary h-full rounded-full transition-all duration-300"
                      style={{ width: `${progress}%` }}
                    />
                  </div>
                </div>
              ) : (
                <button
                  type="button"
                  onClick={handleUploadSubmit}
                  className="w-full bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm transition-all"
                >
                  Submit Resumes for Processing
                </button>
              )}
            </div>
          )}
        </div>
      ) : (
        /* ── PROCESSING REAL-TIME SCREEN ── */
        <div className="glass-panel p-6 rounded-2xl border border-white/5 flex flex-col gap-5">
          <div className="flex items-center justify-between gap-4 border-b border-white/5 pb-3.5">
            <div className="flex items-center gap-2">
              <Sparkles className="w-4 h-4 text-violet-400" />
              <h3 className="font-bold text-sm text-foreground uppercase tracking-wider">AI Processing Status</h3>
            </div>
            
            {allProcessed && (
              <button
                onClick={() => router.push(`/jobs/${jobId}`)}
                className="bg-violet-600 hover:bg-violet-500 text-white rounded-lg px-3.5 py-1.5 text-xs font-bold transition-all shadow shadow-violet-600/10"
              >
                Go to Dashboard
              </button>
            )}
          </div>

          <div className="flex flex-col gap-3">
            {sortedCandidates.map((c) => (
              <CandidateStatusRow key={c.applicationId} candidate={c} />
            ))}
          </div>

          {/* Simple footer indicator */}
          <div className="flex items-center justify-center p-4 border border-dashed border-white/5 rounded-xl text-center">
            <p className="text-xs text-muted-foreground leading-relaxed max-w-xs">
              {allProcessed
                ? 'AI analysis finished for all resumes. You can now view recommendations and kits.'
                : 'Please keep this tab open. Candidates are being queued and analyzed in real-time.'}
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
