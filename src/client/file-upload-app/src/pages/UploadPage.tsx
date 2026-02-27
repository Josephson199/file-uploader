import React from 'react';
import { Grid, Box, Typography } from '@mui/material';
import AuthenticationMessage from '../components/AuthenticationMessage';
import { useContext, useEffect } from 'react';
import Uploader from '../components/Uploader';
import FileList from '../components/FileList';

const Upload: React.FC = () => {

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Uploads
      </Typography>
      <Typography variant="body1" gutterBottom>
        Hello, authenticated user!
      </Typography>

      <Grid container spacing={3} sx={{ marginTop: 2 }}>
        <Grid item xs={12} md={6}>
          <Typography variant="h6" gutterBottom>
            Upload Files
          </Typography>
          <Uploader />
        </Grid>
        <Grid item xs={12} md={6}>
          <FileList />
        </Grid>
      </Grid>
    </Box>
  );
};

export default Upload;