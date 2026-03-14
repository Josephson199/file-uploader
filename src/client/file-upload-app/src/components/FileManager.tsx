import {
  Box,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography
} from "@mui/material";
import { useEffect, useState } from "react";

export interface FileRecord {
  uploadId: number;
  fileId: string;
  orignalFileName: string;
  createdAt: string;

  job?: {
    jobId: number;
    status: string;
    attempts: number;
    maxAttempts: number;
    createdAt: string;
  } | null;
}

export default function FileManager() {
  const [files, setFiles] = useState<FileRecord[]>([]);
  const [loading, setLoading] = useState(true);

  async function loadFiles() {
    setLoading(true);
    const res = await fetch("/api/files-list");
    const data = await res.json();
    setFiles(data);
    setLoading(false);
  }

  async function deleteFile(uploadId: number) {
    await fetch(`/api/files/${uploadId}`, { method: "DELETE" });
    loadFiles();
  }

  function downloadFile(fileId: string) {
    window.location.href = `/api/files/${fileId}/download`;
  }

  useEffect(() => {
    loadFiles();
  }, []);

  const renderStatus = (job: FileRecord["job"]) => {
    if (!job) return <Chip label="Pending" color="warning" size="small" />;
 
    switch (job.status) {
    case "Pending":
        return <Chip label="Pending" color="secondary" size="small" />;
      case "Completed":
        return <Chip label="Completed" color="success" size="small" />;
      case "Failed":
        return <Chip label="Failed" color="error" size="small" />;
      case "Processing":
      default:
        return <Chip label="Processing" color="warning" size="small" />;
    }
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" mt={4}>
        <CircularProgress />
      </Box>
    );
  }

  return (
    <TableContainer component={Paper} sx={{ mt: 3 }}>
      <Box display="flex" justifyContent="space-between" alignItems="center" p={2}>
        <Typography variant="h6">Your Files</Typography>
        <IconButton onClick={loadFiles}>
          <span style={{ marginLeft: 4 }}>Refresh</span>
        </IconButton>
      </Box>

      <Table>
        <TableHead>
          <TableRow>
            <TableCell>File Name</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Uploaded</TableCell>
            <TableCell align="right">Actions</TableCell>
          </TableRow>
        </TableHead>

        <TableBody>
          {files.map((f) => (
            <TableRow key={f.uploadId}>
              <TableCell>{f.orignalFileName}</TableCell>
              <TableCell>{renderStatus(f.job)}</TableCell>
              <TableCell>{new Date(f.createdAt).toLocaleString()}</TableCell>
              <TableCell align="right">
                <IconButton onClick={() => downloadFile(f.fileId)}>
                  <span>Download</span>
                </IconButton>
                <IconButton onClick={() => deleteFile(f.uploadId)}>
                  <span>Delete</span>
                </IconButton>
              </TableCell>
            </TableRow>
          ))}

          {files.length === 0 && (
            <TableRow>
              <TableCell colSpan={4} align="center" sx={{ py: 4 }}>
                No files uploaded yet
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
