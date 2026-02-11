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
import useKeycloak from '../hooks/useKeycloak';
import { useJobEvents } from '../hooks/useJobEvents';

interface UploadedFile {
  uploadId: string;
  fileId: string;
  orignalFileName: string;
  uploadedAt: string;
  virusDetected: boolean;
}

interface JobEvent {
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
}

const FileList: React.FC = () => {
  const { token } = useKeycloak();
  const [files, setFiles] = useState<UploadedFile[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [jobStatuses, setJobStatuses] = useState<Map<string, JobEvent>>(new Map());

  const handleJobEvent = useCallback((jobEvent: JobEvent) => {
    // Update job status map
    setJobStatuses((prev) => {
      const updated = new Map(prev);
      updated.set(jobEvent.jobId, jobEvent);
      return updated;
    });
  }, []);

  const { isConnected: sseConnected } = useJobEvents(handleJobEvent);

  useEffect(() => {
    const fetchFiles = async () => {
      try {
        setIsLoading(true);
        setError(null);

        const response = await fetch('/api/files-list', {
          headers: {
            'Authorization': `Bearer ${token}`,
          },
        });

        if (!response.ok) {
          throw new Error(`Failed to fetch files: ${response.statusText}`);
        }

        const data = await response.json();
        setFiles(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'An error occurred');
      } finally {
        setIsLoading(false);
      }
    };

    if (token) {
      fetchFiles();
    }
  }, [token]);

  const formatDate = (dateString: string): string => {
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

  if (files.length === 0) {
    return (
      <Alert severity="info">No files uploaded yet.</Alert>
    );
  }

  return (
    <Box>
      <Box display="flex" justifyContent="space-between" alignItems="center" gap={1} marginBottom={2}>
        <Typography variant="h6" gutterBottom sx={{ margin: 0 }}>
          Uploaded Files
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
              <TableCell sx={{ fontWeight: 'bold' }}>File Name</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Uploaded</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Virus Check</TableCell>
              <TableCell sx={{ fontWeight: 'bold' }}>Job Status</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {files.map((file) => {
              const jobStatus = jobStatuses.get(file.fileId);
              return (
                <TableRow key={file.uploadId} hover>
                  <TableCell>{file.orignalFileName}</TableCell>
                  <TableCell>{formatDate(file.uploadedAt)}</TableCell>
                  <TableCell>
                    <Chip
                      label={file.virusDetected ? 'Virus Detected' : 'Safe'}
                      color={file.virusDetected ? 'error' : 'success'}
                      variant="outlined"
                      size="small"
                    />
                  </TableCell>
                  <TableCell>
                    {jobStatus ? (
                      <Box>
                        <Chip
                          label={jobStatus.status}
                          color={
                            jobStatus.status === 'completed'
                              ? 'success'
                              : jobStatus.status === 'failed'
                                ? 'error'
                                : 'default'
                          }
                          size="small"
                        />
                        <Typography variant="caption" display="block" sx={{ mt: 0.5 }}>
                          {jobStatus.attempts}/{jobStatus.maxAttempts}
                        </Typography>
                      </Box>
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

export default FileList;
