import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuthStore } from '@/stores/authStore';
import { getHubUrl } from '@/lib/config';

interface HubCallbacks {
  onResumeUploaded?: (applicationId: string, candidateName: string, timestamp: string) => void;
  onProcessingStarted?: (applicationId: string, candidateName: string) => void;
  onFitScoreReady?: (applicationId: string, candidateName: string, fitScore: number, rankPosition: number) => void;
  onInterviewKitReady?: (applicationId: string) => void;
  onProcessingFailed?: (applicationId: string, candidateName: string, errorMessage: string) => void;
}

export function useRecruitmentHub(jobId: string | null, callbacks?: HubCallbacks) {
  const { token } = useAuthStore();
  const [isConnected, setIsConnected] = useState(false);
  const [isReconnecting, setIsReconnecting] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  // Keep callback refs stable
  const callbacksRef = useRef(callbacks);
  useEffect(() => {
    callbacksRef.current = callbacks;
  }, [callbacks]);

  useEffect(() => {
    if (!jobId || !token) return;

    const hubUrl = getHubUrl();

    // Configure connection with token resolver (enforces JWT bearer auth via query string)
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    // Set connection states
    connection.onclose(() => {
      setIsConnected(false);
      setIsReconnecting(false);
    });

    connection.onreconnecting(() => {
      setIsConnected(false);
      setIsReconnecting(true);
    });

    connection.onreconnected(() => {
      setIsConnected(true);
      setIsReconnecting(false);
      // Re-join room on reconnect
      connection.invoke('JoinJobRoom', jobId).catch(console.error);
    });

    // Event Bindings
    connection.on('ResumeUploaded', (appId: string, name: string, ts: string) => {
      callbacksRef.current?.onResumeUploaded?.(appId, name, ts);
    });

    connection.on('ProcessingStarted', (appId: string, name: string) => {
      callbacksRef.current?.onProcessingStarted?.(appId, name);
    });

    connection.on('FitScoreReady', (appId: string, name: string, score: number, rank: number) => {
      callbacksRef.current?.onFitScoreReady?.(appId, name, score, rank);
    });

    connection.on('InterviewKitReady', (appId: string) => {
      callbacksRef.current?.onInterviewKitReady?.(appId);
    });

    connection.on('ProcessingFailed', (appId: string, name: string, err: string) => {
      callbacksRef.current?.onProcessingFailed?.(appId, name, err);
    });

    // Start connection and Join room
    const startConnection = async () => {
      try {
        await connection.start();
        setIsConnected(true);
        setIsReconnecting(false);
        await connection.invoke('JoinJobRoom', jobId);
      } catch (err) {
        console.error('SignalR Hub Connection error: ', err);
        setIsConnected(false);
        // Retry connection after 5 seconds if initial start fails
        setTimeout(startConnection, 5000);
      }
    };

    startConnection();

    // Cleanup on unmount
    return () => {
      if (connectionRef.current) {
        const conn = connectionRef.current;
        if (conn.state === signalR.HubConnectionState.Connected) {
          conn.invoke('LeaveJobRoom', jobId)
            .then(() => conn.stop())
            .catch(console.error);
        } else {
          conn.stop().catch(console.error);
        }
      }
    };
  }, [jobId, token]);

  return {
    isConnected,
    isReconnecting,
  };
}
