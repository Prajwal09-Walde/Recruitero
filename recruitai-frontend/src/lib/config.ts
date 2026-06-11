/**
 * Dynamic configuration for API and Hub URLs depending on the environment.
 */

export const getApiUrl = (): string => {
  if (process.env.NEXT_PUBLIC_API_URL) {
    return process.env.NEXT_PUBLIC_API_URL;
  }
  if (typeof window !== 'undefined') {
    const hostname = window.location.hostname;
    if (hostname === 'localhost' || hostname === '127.0.0.1') {
      return 'http://localhost:5000';
    }
    return window.location.origin;
  }
  // Server-side default: detect Vercel serverless function env or default to localhost
  if (process.env.VERCEL === '1') {
    return 'https://recruitai.io';
  }
  return 'http://localhost:5000';
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
    return `${window.location.origin}/hubs/recruitment`;
  }
  // Server-side default: detect Vercel serverless function env or default to localhost
  if (process.env.VERCEL === '1') {
    return 'https://recruitai.io/hubs/recruitment';
  }
  return 'http://localhost:5000/hubs/recruitment';
};

