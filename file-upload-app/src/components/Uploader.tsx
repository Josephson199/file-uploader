import Uppy from '@uppy/core'
import { UppyContextProvider, Dashboard } from '@uppy/react'
import Tus from '@uppy/tus'
import '@uppy/core/dist/style.css'
import '@uppy/dashboard/dist/style.css'
import '@uppy/status-bar/dist/style.css'
import { useState } from 'react'
import StatusBar from '@uppy/status-bar'

const Uploader = () => {
  const [uppy] = useState(() =>
    new Uppy({
      autoProceed: false,
      restrictions: {
        maxFileSize: 100 * 1024 * 1024 * 10 * 2, // 2gb
        // allowedFileTypes: ['image/*', 'video/*', 'application/pdf'],
      },
    })
    .use(Tus, {
        endpoint: '/api/files',
        chunkSize: 5242880 * 2, // 10mb
        retryDelays: [0, 1000, 3000, 5000],
        // parallelUploads: 1,
        headers: {
          'Authorization': `Bearer 1234`,
          'X-Custom-Header': '1234',
        }

    })
    .use(StatusBar, {
        showProgressDetails: true,   // show "43 MB of 101 MB - 8 s left"
        hideUploadButton: false,     // keep the upload button
        hidePauseResumeButton: false,// allow pausing/resuming
        hideCancelButton: false,     // allow canceling
    })
  )

  return (
    <UppyContextProvider uppy={uppy}>
      <Dashboard uppy={uppy} proudlyDisplayPoweredByUppy={false}/>
    </UppyContextProvider>
  )
}
export default Uploader;