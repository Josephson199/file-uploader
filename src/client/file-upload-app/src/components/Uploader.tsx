import Uppy from '@uppy/core'
import { UppyContextProvider, Dashboard } from '@uppy/react'
import Tus from '@uppy/tus'
import '@uppy/core/dist/style.css'
import '@uppy/dashboard/dist/style.css'
import '@uppy/status-bar/dist/style.css'
import { useState, useEffect } from 'react'
import StatusBar from '@uppy/status-bar'
import useKeycloak from '../hooks/useKeycloak'
import { useConfig } from '../hooks/useConfig'

const Uploader = () => {
  const { token } = useKeycloak();
  const { config, isLoading } = useConfig();
  const [uppy, setUppy] = useState<Uppy | null>(null);

  useEffect(() => {
    if (isLoading || !config?.upload) return;

    // Create Uppy instance with config restrictions
    const uppyInstance = new Uppy({
      autoProceed: false,
      restrictions: {
        maxFileSize: config.upload.maxFileSize,
        allowedFileTypes: config.upload.allowedMimeTypes,
      },
    })
    .use(Tus, {
        endpoint: '/api/files',
        chunkSize: 5242880 * 2, // 10mb
        retryDelays: [0, 1000, 3000, 5000],
        headers: {
          'Authorization': `Bearer ${token}`,
          'X-Custom-Header': '1234',
        }
    })
    .use(StatusBar, {
        showProgressDetails: true,
        hideUploadButton: false,
        hidePauseResumeButton: false,
        hideCancelButton: false,
    });

    // Validate filename and extension
    uppyInstance.on('file-added', (file) => {
      // Check filename length
      if (file.name && file.name.length > config.upload.maxFileNameLength) {
        uppyInstance.removeFile(file.id);
        uppyInstance.info(`File name "${file.name}" exceeds maximum length of ${config.upload.maxFileNameLength} characters`, 'error', 5000);
        return;
      }

      // Check file extension
      if (file.name) {
        const fileExtension = file.name.split('.').pop()?.toLowerCase();
        console.log('File extension:', fileExtension);
        console.log('Allowed extensions:', config.upload.allowedExtensions);
        const fileExtensionWithDot = fileExtension ? `.${fileExtension}` : '';
        if (fileExtensionWithDot && !config.upload.allowedExtensions.includes(fileExtensionWithDot)) {
          uppyInstance.removeFile(file.id);
          uppyInstance.info(`File extension .${fileExtension} is not allowed. Allowed extensions: ${config.upload.allowedExtensions.join(', ')}`, 'error', 5000);
        }
      }
    });

    setUppy(uppyInstance);

    return () => {
      uppyInstance.destroy();
    };
  }, [config?.upload, token, isLoading]);

  if (isLoading) {
    return <div>Loading uploader configuration...</div>;
  }

  if (!config?.upload || !uppy) {
    return <div>Upload configuration not available</div>;
  }

  return (
    <UppyContextProvider uppy={uppy}>
      <Dashboard uppy={uppy} proudlyDisplayPoweredByUppy={false}/>
    </UppyContextProvider>
  )
}
export default Uploader;