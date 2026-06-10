import axios from 'axios';
import { useAuthStore } from '@/stores/authStore';

const apiClient = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000',
  timeout: 15000, // 15s — fail fast rather than hanging the UI
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.request.use(
  (config) => {
    const token = useAuthStore.getState().token;
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

let isRefreshing = false;
let failedQueue: Array<{
  resolve: (token: string) => void;
  reject: (err: any) => void;
}> = [];

const processQueue = (error: any, token: string | null = null) => {
  failedQueue.forEach((prom) => {
    if (token) {
      prom.resolve(token);
    } else {
      prom.reject(error);
    }
  });
  failedQueue = [];
};

apiClient.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    if (error.response?.status === 401 && !originalRequest._retry) {
      if (isRefreshing) {
        return new Promise<string>((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        })
          .then((token) => {
            originalRequest.headers.Authorization = `Bearer ${token}`;
            return apiClient(originalRequest);
          })
          .catch((err) => Promise.reject(err));
      }

      originalRequest._retry = true;
      isRefreshing = true;

      try {
        const { refreshToken, token: currentToken, setTokens, user, logout: storeLogout } = useAuthStore.getState();
        if (!refreshToken || !user?.email) {
          throw new Error('No refresh token available');
        }

        const response = await axios.post(
          `${process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000'}/api/auth/refresh`,
          {
            email: user.email,
            refreshToken: refreshToken,
          }
        );

        const { token: newToken, refreshToken: newRefreshToken } = response.data;
        setTokens(newToken, newRefreshToken);

        processQueue(null, newToken);
        originalRequest.headers.Authorization = `Bearer ${newToken}`;
        return apiClient(originalRequest);
      } catch (refreshError) {
        processQueue(refreshError, null);
        // Attempt server-side logout to revoke token before clearing local state
        try {
          const { token } = useAuthStore.getState();
          if (token) {
            await axios.post(
              `${process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000'}/api/auth/logout`,
              {},
              { headers: { Authorization: `Bearer ${token}` } }
            );
          }
        } catch { /* ignore logout errors */ }
        useAuthStore.getState().logout();
        return Promise.reject(refreshError);
      } finally {
        isRefreshing = false;
      }
    }

    return Promise.reject(error);
  }
);

export default apiClient;
