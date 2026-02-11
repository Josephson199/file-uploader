import React, {
  createContext,
  useEffect,
  useState,
  useRef,
} from 'react'
import Keycloak, { type KeycloakConfig } from 'keycloak-js'
import { useConfig } from '../hooks/useConfig'

interface KeycloakContextProps {
  keycloak: Keycloak | null
  authenticated: boolean
  token?: string | null
  login?: () => void
  logout?: () => void
}

const KeycloakContext = createContext<KeycloakContextProps | undefined>(
  undefined,
)

interface KeycloakProviderProps {
  children: React.ReactNode
}

const KeycloakProvider: React.FC<KeycloakProviderProps> = ({ children }) => {
  const { config, isLoading } = useConfig()
  const isRun = useRef<boolean>(false)
  const refreshTimer = useRef<number | null>(null)
  const [keycloak, setKeycloak] = useState<Keycloak | null>(null)
  const [authenticated, setAuthenticated] = useState<boolean>(false)
  const [token, setToken] = useState<string | null>(null)

  useEffect(() => {
    if (isRun.current || isLoading || !config) return
    isRun.current = true

    const initKeycloak = async () => {
      const keycloackConfig: KeycloakConfig  = {
        url: config.keycloak.baseUrl,
        realm: config.keycloak.realm,
        clientId: 'spa-client',
      }
      const keycloakInstance: Keycloak = new Keycloak(keycloackConfig)

      try {
        await keycloakInstance.init({
          onLoad: 'check-sso',
          // required for check-sso to obtain tokens silently on page reload
          silentCheckSsoRedirectUri: `${window.location.origin}/silent-check-sso.html`,
          // PKCE + code flow (Keycloak handles details)
          pkceMethod: 'S256',
          // don't use login iframe in dev
          checkLoginIframe: false,
        })

        const isAuth = !!keycloakInstance.authenticated
        setAuthenticated(isAuth)
        setKeycloak(keycloakInstance)
        setToken(keycloakInstance.token ?? null)

        // periodic token refresh (every 60s attempt, updateToken will skip if not needed)
        refreshTimer.current = window.setInterval(() => {
          if (!keycloakInstance) return
          keycloakInstance
            .updateToken(70) // seconds
            .then(() => {
              // token refreshed or still valid
              setToken(keycloakInstance.token ?? null)
            })
            .catch(() => {
              // couldn't refresh — mark unauthenticated
              setAuthenticated(false)
              setToken(null)
            })
        }, 60_000)
      } catch (err) {
        console.error('Keycloak initialization failed:', err)
        setAuthenticated(false)
        setKeycloak(keycloakInstance)
        setToken(null)
      }
    }

    initKeycloak()

    return () => {
      if (refreshTimer.current) {
        clearInterval(refreshTimer.current)
      }
    }
  }, [config, isLoading])

  const login = () => keycloak?.login()
  const logout = () => keycloak?.logout()

  return (
    <KeycloakContext.Provider value={{ keycloak, authenticated, token, login, logout }}>
      {children}
    </KeycloakContext.Provider>
  )
}

export { KeycloakProvider, KeycloakContext }