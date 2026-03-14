// This file has been replaced by JobList.tsx. All logic and exports have moved.

import React, { useState, useEffect, useCallback } from 'react';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Paper,
  CircularProgress,
  Alert,
  Box,
  Typography,
  Chip,
} from '@mui/material';
import { useJobEvents, type JobEvent } from '../hooks/useJobEvents';

export interface Job {
  jobId: number;
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

const JobList: React.FC = () => {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Map jobId (number) to latest event info
  const [jobEvents, setJobEvents] = useState<Map<number, JobEvent>>(new Map());

  // Map the SSE event to our jobEvents map using jobId as number
  const handleJobEvent = useCallback((jobEvent: JobEvent) => {
    setJobEvents((prev) => {
      const updated = new Map(prev);
      const jobIdNum = Number(jobEvent.jobId);
      if (!isNaN(jobIdNum)) {
        updated.set(jobIdNum, jobEvent);
      }
      return updated;
    });
  }, []);

  const { isConnected: sseConnected } = useJobEvents(handleJobEvent);

  useEffect(() => {
    const fetchJobs = async () => {
      try {
        setIsLoading(true);
        setError(null);
        const response = await fetch('/api/jobs-list');
        if (!response.ok) {
          throw new Error(`Failed to fetch jobs: ${response.statusText}`);
        }
        const data = await response.json();
        setJobs(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'An error occurred');
      } finally {
        setIsLoading(false);
      }
    };
    fetchJobs();
  }, [jobEvents]);

  const formatDate = (dateString: string | null): string => {
    if (!dateString) return '—';
    const date = new Date(dateString);
    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
  };

  if (isLoading) {
    return (
      <Box display="flex" justifyContent="center" alignItems="center" minHeight={300}>
        <CircularProgress />
      </Box>
    );
  }

  if (error) {
    return <Alert severity="error">Error: {error}</Alert>;
  }

  if (jobs.length === 0) {
    return <Alert severity="info">No jobs found.</Alert>;
  }

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" gap={1} marginBottom={2}>
        <Typography variant="h6" gutterBottom sx={{ margin: 0 }}>
          Job List
        </Typography>
        <Chip
          label={sseConnected ? 'Live' : 'Disconnected'}
          color={sseConnected ? 'success' : 'error'}
          size="small"
        />
      </Box>
      <TableContainer component={Paper}>
        <Table>
          <TableHead>
            <TableRow sx={{ backgroundColor: '#f5f5f5' }}>
              <TableCell sx={{ fontWeight: 'bold' }}>Job ID</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Type</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Status</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Attempts</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Created</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Updated</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Locked</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {jobs.map((job) => {
              const event = jobEvents.get(job.jobId);
              const status = event ? event.status : job.status;
              const attempts = event ? event.attempts : job.attempts;
              const maxAttempts = event ? event.maxAttempts : job.maxAttempts;
              const updatedAt = event ? event.updatedAt : job.updatedAt;
              const lockedAt = event ? event.lockedAt : job.lockedAt;
              const lockedBy = event ? event.lockedBy : job.lockedBy;
              return (
                <TableRow key={job.jobId} hover>
                  <TableCell>{job.jobId}</TableCell>
                  <TableCell>{job.type}</TableCell>
                  <TableCell>
                    <Chip
                      label={status}
                      color={
                        status === 'completed'
                          ? 'success'
                          : status === 'failed'
                            ? 'error'
                            : 'default'
                      }
                      size="small"
                    />
                  </TableCell>
                  <TableCell>
                    <Typography variant="caption">
                      {attempts}/{maxAttempts}
                    </Typography>
                  </TableCell>
                  <TableCell>{formatDate(job.createdAt)}</TableCell>
                  <TableCell>{formatDate(updatedAt)}</TableCell>
                  <TableCell>
                    {lockedAt ? (
                      <Typography variant="caption">
                        {formatDate(lockedAt)}
                        {lockedBy ? ` by ${lockedBy}` : ''}
                      </Typography>
                    ) : (
                      <Typography variant="caption" color="textSecondary">
                        —
                      </Typography>
                    )}
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
};

export default JobList;
