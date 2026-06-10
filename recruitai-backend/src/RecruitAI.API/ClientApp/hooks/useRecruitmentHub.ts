"use client";

import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { useCallback, useEffect, useRef, useState } from "react";

// ── Types ────────────────────────────────────────────────────────────────────

export interface RankedCandidate {
  rank: number;
  candidateId: string;
  name: string;
  fitScore: number;
  topSkillMatches: string[];
  status: string;
  applicationId: string;
}

export interface RecruitmentHubEvents {
  onResumeUploaded?: (applicationId: string, candidateName: string, timestamp: string) => void;
  onProcessingStarted?: (applicationId: string, candidateName: string) => void;
  onFitScoreReady?: (applicationId: string, candidateName: string, fitScore: number, rankPosition: number) => void;
  onInterviewKitReady?: (applicationId: string) => void;
  onProcessingFailed?: (applicationId: string, candidateName: string, errorMessage: string) => void;
  onLeaderboardUpdated?: (jobId: string, candidates: RankedCandidate[]) => void;
}

export interface UseRecruitmentHubOptions {
  /** Full URL to the SignalR hub, e.g. "https://api.recruitai.io/hubs/recruitment" */
  hubUrl: string;
  /** JWT bearer token for authentication */
  accessToken: string;
  /** Job room to join on connect */
  jobId: string;
  /** Event handlers */
  events?: RecruitmentHubEvents;
}

export type ConnectionStatus = "disconnected" | "connecting" | "connected" | "reconnecting" | "error";

// ── Hook ─────────────────────────────────────────────────────────────────────

/**
 * useRecruitmentHub
 *
 * Establishes and manages a SignalR connection to the RecruitmentHub.
 * Automatically joins/leaves the job room, handles reconnection,
 * and registers all server→client event handlers.
 *
 * @example
 * const { status, joinRoom, leaveRoom } = useRecruitmentHub({
 *   hubUrl: process.env.NEXT_PUBLIC_HUB_URL!,
 *   accessToken: token,
 *   jobId: "job-uuid",
 *   events: {
 *     onFitScoreReady: (appId, name, score, rank) => console.log(`${name}: ${score}`),
 *     onLeaderboardUpdated: (jobId, candidates) => setLeaderboard(candidates),
 *   },
 * });
 */
export function useRecruitmentHub({
  hubUrl,
  accessToken,
  jobId,
  events = {},
}: UseRecruitmentHubOptions) {
  const connectionRef = useRef<HubConnection | null>(null);
  const [status, setStatus] = useState<ConnectionStatus>("disconnected");
  const [error, setError] = useState<string | null>(null);

  // Stable event ref to avoid re-subscribing on every render
  const eventsRef = useRef(events);
  useEffect(() => {
    eventsRef.current = events;
  }, [events]);

  const buildConnection = useCallback((): HubConnection => {
    return new HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => accessToken,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(
        process.env.NODE_ENV === "development" ? LogLevel.Information : LogLevel.Warning
      )
      .build();
  }, [hubUrl, accessToken]);

  const registerHandlers = useCallback((conn: HubConnection) => {
    conn.on("ResumeUploaded", (appId: string, name: string, ts: string) => {
      eventsRef.current.onResumeUploaded?.(appId, name, ts);
    });
    conn.on("ProcessingStarted", (appId: string, name: string) => {
      eventsRef.current.onProcessingStarted?.(appId, name);
    });
    conn.on("FitScoreReady", (appId: string, name: string, score: number, rank: number) => {
      eventsRef.current.onFitScoreReady?.(appId, name, score, rank);
    });
    conn.on("InterviewKitReady", (appId: string) => {
      eventsRef.current.onInterviewKitReady?.(appId);
    });
    conn.on("ProcessingFailed", (appId: string, name: string, err: string) => {
      eventsRef.current.onProcessingFailed?.(appId, name, err);
    });
    conn.on("LeaderboardUpdated", (jId: string, candidates: RankedCandidate[]) => {
      eventsRef.current.onLeaderboardUpdated?.(jId, candidates);
    });
  }, []);

  // ── Connect on mount / reconnect on jobId or token change ─────────────────
  useEffect(() => {
    let mounted = true;

    async function connect() {
      if (!accessToken || !jobId) return;

      setStatus("connecting");
      setError(null);

      const conn = buildConnection();
      connectionRef.current = conn;

      conn.onreconnecting(() => { if (mounted) setStatus("reconnecting"); });
      conn.onreconnected(async () => {
        if (!mounted) return;
        setStatus("connected");
        await conn.invoke("JoinJobRoom", jobId);
      });
      conn.onclose((err) => {
        if (!mounted) return;
        setStatus(err ? "error" : "disconnected");
        if (err) setError(err.message);
      });

      registerHandlers(conn);

      try {
        await conn.start();
        if (!mounted) { await conn.stop(); return; }
        setStatus("connected");
        await conn.invoke("JoinJobRoom", jobId);
      } catch (err: unknown) {
        if (mounted) {
          setStatus("error");
          setError(err instanceof Error ? err.message : "Connection failed");
        }
      }
    }

    connect();

    return () => {
      mounted = false;
      const conn = connectionRef.current;
      if (conn?.state === HubConnectionState.Connected) {
        conn.invoke("LeaveJobRoom", jobId).finally(() => conn.stop());
      } else {
        conn?.stop();
      }
    };
  }, [hubUrl, accessToken, jobId, buildConnection, registerHandlers]);

  // ── Manual helpers ────────────────────────────────────────────────────────
  const joinRoom = useCallback(async (id: string) => {
    if (connectionRef.current?.state === HubConnectionState.Connected)
      await connectionRef.current.invoke("JoinJobRoom", id);
  }, []);

  const leaveRoom = useCallback(async (id: string) => {
    if (connectionRef.current?.state === HubConnectionState.Connected)
      await connectionRef.current.invoke("LeaveJobRoom", id);
  }, []);

  return { status, error, joinRoom, leaveRoom };
}
