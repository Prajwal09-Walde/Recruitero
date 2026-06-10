export const queryKeys = {
  jobs: {
    all: ['jobs'] as const,
    lists: () => [...queryKeys.jobs.all, 'list'] as const,
    detail: (id: string) => [...queryKeys.jobs.all, 'detail', id] as const,
  },
  leaderboard: {
    all: ['leaderboard'] as const,
    job: (jobId: string, filters: { status?: string; page: number; pageSize: number }) => 
      [...queryKeys.leaderboard.all, jobId, filters] as const,
  },
  applications: {
    all: ['applications'] as const,
    detail: (id: string) => [...queryKeys.applications.all, 'detail', id] as const,
    kit: (appId: string) => [...queryKeys.applications.all, 'kit', appId] as const,
  },
  skills: {
    preview: (text: string) => ['skills', 'preview', text] as const,
  }
} as const;
