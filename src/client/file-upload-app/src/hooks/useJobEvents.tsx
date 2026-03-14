import { useEffect, useState } from 'react';

export interface JobEvent {
  jobId: string;
  type: string;
  status: string;
  attempts: number;
  maxAttempts: number;
  lockedAt: string | null;
  lockedBy: string | null;
  createdAt: string;
  updatedAt: string;
  payload: Record<string, any>;
  userId?: number;
}
export const useJobEvents = (onEvent?: (event: JobEvent) => void) => {
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

 useEffect(() => {
  let eventSource: EventSource | null = null;

  const connect = () => {
    setError(null);
    eventSource = new EventSource("/api/events");

    eventSource.onopen = () => setIsConnected(true);

    eventSource.addEventListener("jobs", (event) => {
      try {
        const jobEvent: JobEvent = JSON.parse(event.data);
        onEvent?.(jobEvent);
      } catch (err) {
        console.error("Failed to parse event data:", err);
      }
    });

    eventSource.onerror = () => {
      setIsConnected(false);
      setError("Connection lost. Reconnecting...");
      eventSource?.close();
      setTimeout(connect, 3000);
    };
  };

  connect();

  return () => {
    eventSource?.close();
    setIsConnected(false);
  };
}, [onEvent]);


  return { isConnected, error };
};
