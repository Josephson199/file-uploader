import { createContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'

export interface KeycloakConfig {
  baseUrl: string;
  realm: string;
}

export interface UploadConfig {
  maxFileSize: number;
  allowedExtensions: string[];
  allowedMimeTypes: string[];
  maxFileNameLength: number;
}

export interface AppConfig {
  keycloak: KeycloakConfig;
  upload: UploadConfig;
  [key: string]: unknown;
}

interface ConfigContextType {
  config: AppConfig | null;
  isLoading: boolean;
  error: string | null;
}

export const ConfigContext = createContext<ConfigContextType | undefined>(undefined);

export function ConfigProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<AppConfig | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const loadConfig = async () => {
      try {
        setIsLoading(true);
        const response = await fetch('/config');
        if (!response.ok) {
          throw new Error(`Failed to load config: ${response.statusText}`);
        }
        const data = await response.json();
        setConfig(data);
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load configuration');
        setConfig(null);
      } finally {
        setIsLoading(false);
      }
    };

    loadConfig();
  }, []);

  return (
    <ConfigContext.Provider value={{ config, isLoading, error }}>
      {children}
    </ConfigContext.Provider>
  );
}
