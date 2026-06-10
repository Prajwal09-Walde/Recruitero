import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import apiClient from '@/lib/apiClient';
import { queryKeys } from '@/lib/queryKeys';
import { LeaderboardResult, Job } from '@/types';
import { toast } from '@/components/ui/Toaster';

interface UseJobLeaderboardFilters {
  status?: string;
  page: number;
  pageSize: number;
}

export function useJobDetails(jobId: string) {
  return useQuery({
    queryKey: queryKeys.jobs.detail(jobId),
    queryFn: async () => {
      if (!jobId) return null;
      const response = await apiClient.get<Job>(`/api/jobs/${jobId}`);
      return response.data;
    },
    enabled: !!jobId,
    staleTime: 10 * 60 * 1000, // job metadata rarely changes mid-session
  });
}

export function useJobList() {
  return useQuery({
    queryKey: queryKeys.jobs.lists(),
    queryFn: async () => {
      const response = await apiClient.get<Job[]>('/api/jobs');
      return response.data;
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function useJobLeaderboard(jobId: string, filters: UseJobLeaderboardFilters) {
  return useQuery({
    queryKey: queryKeys.leaderboard.job(jobId, filters),
    queryFn: async () => {
      if (!jobId) return null;
      const response = await apiClient.get<LeaderboardResult>(
        `/api/jobs/${jobId}/leaderboard`,
        {
          params: {
            status: filters.status,
            page: filters.page,
            pageSize: filters.pageSize,
          },
        }
      );
      return response.data;
    },
    enabled: !!jobId,
    staleTime: 30 * 1000,       // 30s — real-time updates arrive via SignalR
    placeholderData: keepPreviousData, // no blank flash on page/filter change
  });
}

export function useUpdateApplicationStatus(jobId: string, filters: UseJobLeaderboardFilters) {
  const queryClient = useQueryClient();
  const activeKey = queryKeys.leaderboard.job(jobId, filters);

  return useMutation({
    mutationFn: async ({ applicationId, status }: { applicationId: string; status: string }) => {
      const response = await apiClient.patch(`/api/applications/${applicationId}/status`, {
        status,
      });
      return response.data;
    },
    // Perform optimistic updates
    onMutate: async (variables) => {
      // Cancel any outgoing refetches so they don't overwrite our optimistic update
      await queryClient.cancelQueries({ queryKey: activeKey });

      // Snapshot the previous value
      const previousData = queryClient.getQueryData<LeaderboardResult>(activeKey);

      // Optimistically update the list
      if (previousData) {
        const updatedCandidates = previousData.candidates.map((cand) => {
          if (cand.applicationId === variables.applicationId) {
            return { ...cand, status: variables.status };
          }
          return cand;
        });

        queryClient.setQueryData<LeaderboardResult>(activeKey, {
          ...previousData,
          candidates: updatedCandidates,
        });
      }

      // Return context with snapshotted value
      return { previousData };
    },
    onError: (err, variables, context) => {
      // Rollback on error
      if (context?.previousData) {
        queryClient.setQueryData(activeKey, context.previousData);
      }
      toast('Failed to update status', {
        description: 'Status update was rolled back.',
        type: 'error',
      });
    },
    onSettled: () => {
      // Refetch after completion or failure
      queryClient.invalidateQueries({ queryKey: activeKey });
    },
  });
}
