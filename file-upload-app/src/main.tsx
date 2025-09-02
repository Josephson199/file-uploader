import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import Uploader from './components/uploader.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
    <Uploader />
  </StrictMode>,
)
