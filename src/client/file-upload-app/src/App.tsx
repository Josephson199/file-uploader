import './App.css'
import HomePage from './pages/HomePage'
import { KeycloakProvider } from './contexts/KeycloakContext'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import Layout from './components/Layout'
import Upload from './pages/UploadPage'

function App() {
  return (
    <KeycloakProvider>
      <BrowserRouter>
        <Routes>
          <Route
            path="/"
            element={
              <Layout>
                <HomePage />
              </Layout>
            }
          />
          <Route path="/upload" element={<Layout><Upload /></Layout>} />
        </Routes>
      </BrowserRouter>
    </KeycloakProvider>
  )
}

export default App