import './App.css'
import HomePage from './pages/HomePage'
import { BrowserRouter, Routes, Route } from 'react-router-dom'
import Layout from './components/Layout'
import Upload from './pages/UploadPage'
import { useConfig } from './hooks/useConfig'

function AppRoutes() {
  return (
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
          <Route path="/uploads" element={<Layout><Upload /></Layout>} />
        </Routes>
      </BrowserRouter>
  )
}

function App() {
  const { config, isLoading, error } = useConfig();

  if (isLoading) {
    return <div>Loading configuration...</div>;
  }

  if (error) {
    return <div>Error loading configuration: {error}</div>;
  }

  if (!config) {
    return <div>Configuration not available</div>;
  }

  return <AppRoutes />;
}

export default App