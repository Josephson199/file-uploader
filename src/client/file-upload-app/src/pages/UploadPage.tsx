import React from 'react';
import AuthenticationMessage from '../components/AuthenticationMessage';
import useKeycloak from '../hooks/useKeycloak';
import Uploader from '../components/Uploader';
import { FilesList } from '@uppy/react';

const Upload: React.FC = () => {
  const { keycloak, authenticated } = useKeycloak();

  if (!authenticated || !keycloak) {
    return <AuthenticationMessage />;
  }

  return (
    <div>
      <h1>Uploads</h1>
      <p>Hello, {keycloak?.idTokenParsed?.preferred_username}!</p>
      <p>Email: {keycloak?.idTokenParsed?.email}</p>
      <Uploader/>
    </div>
  );
};

export default Upload;