import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import axios from 'axios';
import { queryKeys } from '@/lib/queryKeys';
import { SkillGraph } from '@/types';

export function useSkillPreview(text: string) {
  const [debouncedText, setDebouncedText] = useState(text);

  useEffect(() => {
    if (!text || text.length < 50) {
      setDebouncedText('');
      return;
    }

    const timer = setTimeout(() => {
      setDebouncedText(text);
    }, 800); // 800ms feels responsive while still debouncing API calls

    return () => clearTimeout(timer);
  }, [text]);

  const query = useQuery({
    queryKey: queryKeys.skills.preview(debouncedText),
    queryFn: async ({ signal }) => {
      if (!debouncedText) return null;
      const response = await axios.get<SkillGraph>(`/api/jobs/preview-skills`, {
        params: { text: debouncedText },
        signal,
      });
      return response.data;
    },
    enabled: debouncedText.length >= 100,
    staleTime: 15 * 60 * 1000, // same JD text = same skills; cache for 15 min
  });

  return {
    ...query,
    isDebouncing: text !== debouncedText && text.length >= 100,
  };
}
