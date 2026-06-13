'use client';

import React, { useState, useEffect } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as zod from 'zod';
import { useRouter } from 'next/navigation';
import { useMutation } from '@tanstack/react-query';
import apiClient from '@/lib/apiClient';
import { useSkillPreview } from '@/hooks/useSkillPreview';
import { SkillPillCloud } from '@/components/recruitment/SkillPillCloud';
import { toast } from '@/components/ui/Toaster';
import { Briefcase, Calendar, MapPin, Sparkles, Building2, AlertCircle } from 'lucide-react';
import { SkillGraph } from '@/types';

// Zod validation schema
const jobSchema = zod.object({
  title: zod.string()
    .min(5, 'Title must be at least 5 characters')
    .max(120, 'Title cannot exceed 120 characters'),
  description: zod.string()
    .min(100, 'Description must be at least 100 characters'),
  department: zod.enum(['Engineering', 'Design', 'Product', 'Data', 'QA', 'Management']),
  experienceLevel: zod.enum(['Junior', 'Mid', 'Senior', 'Lead', 'Director']),
  location: zod.string().min(1, 'Location is required'),
  isRemote: zod.boolean().default(false),
  deadline: zod.string().refine((val) => {
    if (!val) return true;
    return new Date(val) > new Date();
  }, {
    message: 'Deadline must be in the future',
  }),
});

type JobFormValues = zod.infer<typeof jobSchema>;

export default function CreateJobPage() {
  const router = useRouter();
  const [editedSkillGraph, setEditedSkillGraph] = useState<SkillGraph | null>(null);

  const {
    register,
    handleSubmit,
    control,
    formState: { errors },
  } = useForm<JobFormValues>({
    resolver: zodResolver(jobSchema),
    defaultValues: {
      title: '',
      description: '',
      department: 'Engineering',
      experienceLevel: 'Mid',
      location: '',
      isRemote: false,
      deadline: '',
    },
  });

  const description = useWatch({ control, name: 'description' });
  const title = useWatch({ control, name: 'title' });

  // Hook for debounced AI skill extraction
  const { data: skillGraphData, isLoading: isExtracting, isDebouncing } = useSkillPreview(description);

  // Sync extracted skills to local state for editing
  useEffect(() => {
    if (skillGraphData) {
      setEditedSkillGraph(skillGraphData);
    } else {
      setEditedSkillGraph(null);
    }
  }, [skillGraphData]);

  // Job creation mutation
  const createJobMutation = useMutation({
    mutationFn: async (values: JobFormValues) => {
      const payload = {
        ...values,
        deadline: values.deadline ? new Date(values.deadline).toISOString() : null,
        // Send the customized skillGraph if the user modified it
        skillGraph: editedSkillGraph,
      };
      const response = await apiClient.post('/api/jobs', payload);
      return response.data;
    },
    onSuccess: (data) => {
      toast('Job Posting Created!', {
        description: `Successfully posted ${data.title}. Triggers automated skill extraction.`,
        type: 'success',
      });
      router.push(`/jobs/${data.id}`);
    },
    onError: (err: any) => {
      toast('Failed to create job', {
        description: err.response?.data?.detail || 'An error occurred while creating the job posting.',
        type: 'error',
      });
    },
  });

  const onSubmit = (values: JobFormValues) => {
    createJobMutation.mutate(values);
  };

  const isDescriptionWarning = description && description.length >= 100 && description.length < 300;

  return (
    <div className="flex flex-col gap-6 w-full max-w-4xl mx-auto py-4">
      <div className="flex flex-col gap-1.5">
        <h1 className="text-3xl font-extrabold tracking-tight text-black dark:text-white">
          Create Job Posting
        </h1>
        <p className="text-sm text-muted-foreground">
          Define the role and allow the AI engine to analyze skill alignment automatically.
        </p>
      </div>

      <form onSubmit={handleSubmit(onSubmit)} className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        
        {/* Left Side: Form Controls (66%) */}
        <div className="lg:col-span-2 flex flex-col gap-5 glass-panel p-6 rounded-2xl">
          
          {/* Job Title */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold text-muted-foreground uppercase tracking-wider">
              Job Title
            </label>
            <div className="relative">
              <Briefcase className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
              <input
                type="text"
                placeholder="e.g. Senior Full-Stack Engineer"
                {...register('title')}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 pl-11 pr-4 text-sm focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
            </div>
            {errors.title && (
              <span className="text-xs text-rose-400 font-medium">{errors.title.message}</span>
            )}
          </div>

          {/* Department + Experience Level */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-bold text-muted-foreground uppercase tracking-wider flex items-center gap-1">
                <Building2 className="w-3.5 h-3.5" /> Department
              </label>
              <select
                {...register('department')}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 px-3 text-sm focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              >
                <option value="Engineering">Engineering</option>
                <option value="Design">Design</option>
                <option value="Product">Product</option>
                <option value="Data">Data</option>
                <option value="QA">QA</option>
                <option value="Management">Management</option>
              </select>
            </div>

            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-bold text-muted-foreground uppercase tracking-wider">
                Experience Level
              </label>
              <select
                {...register('experienceLevel')}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 px-3 text-sm focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              >
                <option value="Junior">Junior</option>
                <option value="Mid">Mid</option>
                <option value="Senior">Senior</option>
                <option value="Lead">Lead</option>
                <option value="Director">Director</option>
              </select>
            </div>
          </div>

          {/* Location + Remote */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 items-end">
            <div className="sm:col-span-2 flex flex-col gap-1.5">
              <label className="text-xs font-bold text-muted-foreground uppercase tracking-wider flex items-center gap-1">
                <MapPin className="w-3.5 h-3.5" /> Location
              </label>
              <input
                type="text"
                placeholder="e.g. San Francisco, CA"
                {...register('location')}
                className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 px-3 text-sm focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
              />
              {errors.location && (
                <span className="text-xs text-rose-400 font-medium">{errors.location.message}</span>
              )}
            </div>

            <div className="flex items-center justify-between p-3 bg-white/5 border border-white/10 rounded-xl h-[46px]">
              <span className="text-xs font-semibold">Remote Position</span>
              <input
                type="checkbox"
                {...register('isRemote')}
                className="w-4 h-4 accent-violet-600 rounded cursor-pointer"
              />
            </div>
          </div>

          {/* Deadline */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold text-muted-foreground uppercase tracking-wider flex items-center gap-1">
              <Calendar className="w-3.5 h-3.5" /> Application Deadline
            </label>
            <input
              type="date"
              {...register('deadline')}
              className="w-full bg-white/5 border border-white/10 rounded-xl py-2.5 px-3 text-sm focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all"
            />
            {errors.deadline && (
              <span className="text-xs text-rose-400 font-medium">{errors.deadline.message}</span>
            )}
          </div>

          {/* Job Description */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold text-muted-foreground uppercase tracking-wider">
              Job Description (JD)
            </label>
            <textarea
              rows={8}
              placeholder="Paste the full job description details here (Min 100 characters)..."
              {...register('description')}
              className="w-full bg-white/5 border border-white/10 rounded-xl p-3.5 text-sm focus:outline-none focus:border-violet-500 focus:ring-1 focus:ring-violet-500 transition-all resize-y"
            />
            {errors.description && (
              <span className="text-xs text-rose-400 font-medium">{errors.description.message}</span>
            )}
            
            {/* Warning if JD is < 300 characters */}
            {isDescriptionWarning && (
              <div className="flex items-start gap-2 text-xs text-amber-400 bg-amber-500/10 border border-amber-500/20 p-2.5 rounded-lg mt-1 leading-snug">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>Longer JDs (300+ characters) get much better AI match recommendations.</span>
              </div>
            )}
          </div>

          <button
            type="submit"
            disabled={createJobMutation.isPending}
            className="w-full mt-4 bg-gradient-to-r from-violet-600 to-fuchsia-600 hover:from-violet-500 hover:to-fuchsia-500 text-white rounded-xl py-3 font-semibold text-sm shadow-lg shadow-violet-600/20 hover:shadow-violet-600/35 transition-all flex items-center justify-center gap-2 disabled:opacity-50"
          >
            {createJobMutation.isPending ? (
              <div className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              'Post & Initialize AI Extractors'
            )}
          </button>
        </div>

        {/* Right Side: AI Skill Graph Extraction Panel (33%) */}
        <div className="flex flex-col gap-4">
          <div className="flex items-center gap-2">
            <Sparkles className="w-5 h-5 text-violet-400" />
            <h3 className="font-bold text-sm text-foreground uppercase tracking-wider">AI Skill Extractor</h3>
          </div>

          {isDebouncing && (
            <div className="glass-panel p-5 rounded-xl flex items-center justify-center h-32 border border-white/5">
              <span className="text-xs text-muted-foreground animate-pulse">Waiting for you to stop typing...</span>
            </div>
          )}

          {isExtracting && (
            <div className="glass-panel p-5 rounded-xl border border-white/5 flex flex-col gap-4">
              <div className="space-y-2">
                <div className="h-4 bg-white/5 rounded w-1/3 animate-pulse" />
                <div className="h-3 bg-white/5 rounded w-2/3 animate-pulse" />
              </div>
              <div className="flex flex-wrap gap-2">
                {Array.from({ length: 5 }).map((_, i) => (
                  <div key={i} className="h-6 bg-white/5 rounded-full w-16 animate-pulse" />
                ))}
              </div>
            </div>
          )}

          {!isExtracting && !isDebouncing && editedSkillGraph && (
            <SkillPillCloud
              skills={editedSkillGraph}
              onChange={(updated) => setEditedSkillGraph(updated)}
            />
          )}

          {!editedSkillGraph && !isExtracting && !isDebouncing && (
            <div className="glass-panel p-6 rounded-2xl border border-white/5 text-center flex flex-col items-center justify-center h-48">
              <Sparkles className="w-8 h-8 text-white/20 mb-2" />
              <p className="text-xs text-muted-foreground max-w-[200px] leading-relaxed">
                Type 100+ characters of the Job Description to view live AI skill graph extractions.
              </p>
            </div>
          )}
        </div>
      </form>
    </div>
  );
}
