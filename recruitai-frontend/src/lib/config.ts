/**
 * Dynamic configuration for API and Hub URLs depending on the environment.
 */

export const getApiUrl = (): string => {
  if (typeof window !== 'undefined') {
    const hostname = window.location.hostname;
    if (hostname === 'localhost' || hostname === '127.0.0.1') {
      return 'http://localhost:5000';
    }
    // In production browser, always use relative path so Vercel rewrites handles it.
    // This avoids CORS preflight requests and enforces same-origin.
    return '';
  }
  // Server-side: check environment variable or default to recruitai.io
  return process.env.NEXT_PUBLIC_API_URL || 'https://recruitai.io';
};

export const getHubUrl = (): string => {
  if (process.env.NEXT_PUBLIC_HUB_URL) {
    return process.env.NEXT_PUBLIC_HUB_URL;
  }
  if (typeof window !== 'undefined') {
    const hostname = window.location.hostname;
    if (hostname === 'localhost' || hostname === '127.0.0.1') {
      return 'http://localhost:5000/hubs/recruitment';
    }
    // Return relative path. If WebSockets are supported, it connects via wss relative.
    // If not, long-polling fallback routes correctly through Vercel rewrites.
    return '/hubs/recruitment';
  }
  // Server-side default
  if (process.env.NEXT_PUBLIC_API_URL) {
    return `${process.env.NEXT_PUBLIC_API_URL}/hubs/recruitment`;
  }
  return 'https://recruitai.io/hubs/recruitment';
};

