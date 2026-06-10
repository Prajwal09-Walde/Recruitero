import { useState } from 'react';
import apiClient from '@/lib/apiClient';

export function useUpload() {
  const [progress, setProgress] = useState(0);
  const [isUploading, setIsUploading] = useState(false);
  const [isUploaded, setIsUploaded] = useState(false);
  const [applicationIds, setApplicationIds] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);

  const upload = async (jobId: string, files: File[]) => {
    setIsUploading(true);
    setIsUploaded(false);
    setProgress(0);
    setError(null);

    const formData = new FormData();
    files.forEach((file) => {
      formData.append('files', file);
    });

    try {
      const response = await apiClient.post(
        `/api/jobs/${jobId}/applications/bulk-upload`,
        formData,
        {
          headers: {
            'Content-Type': 'multipart/form-data',
          },
          onUploadProgress: (progressEvent) => {
            const total = progressEvent.total || 0;
            if (total > 0) {
              const current = Math.round((progressEvent.loaded * 100) / total);
              setProgress(current);
            }
          },
        }
      );

      setApplicationIds(response.data.applicationIds || []);
      setIsUploaded(true);
      return response.data;
    } catch (err: any) {
      const errMsg = err.response?.data?.detail || 'Failed to upload files.';
      setError(errMsg);
      throw err;
    } finally {
      setIsUploading(false);
    }
  };

  const reset = () => {
    setProgress(0);
    setIsUploading(false);
    setIsUploaded(false);
    setApplicationIds([]);
    setError(null);
  };

  return {
    upload,
    progress,
    isUploading,
    isUploaded,
    applicationIds,
    error,
    reset,
  };
}
