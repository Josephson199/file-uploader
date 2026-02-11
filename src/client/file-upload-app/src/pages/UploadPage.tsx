import React from 'react';
import { Grid, Box, Typography } from '@mui/material';
import AuthenticationMessage from '../components/AuthenticationMessage';
import useKeycloak from '../hooks/useKeycloak';
import Uploader from '../components/Uploader';
import FileList from '../components/FileList';

const Upload: React.FC = () => {
  const { keycloak, authenticated } = useKeycloak();

  if (!authenticated || !keycloak) {
    return <AuthenticationMessage />;
  }

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Uploads
      </Typography>
      <Typography variant="body1" gutterBottom>
        Hello, {keycloak?.idTokenParsed?.preferred_username}!
      </Typography>
      <Typography variant="body2" gutterBottom>
        Email: {keycloak?.idTokenParsed?.email}
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