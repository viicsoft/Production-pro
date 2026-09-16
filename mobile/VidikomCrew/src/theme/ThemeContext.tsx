import React, { createContext, useContext, useMemo, useState, useCallback, useEffect } from 'react';
import { ThemeTokens, DarkTheme, OledTheme } from './tokens';
import { SettingsContext } from '../context/SettingsContext';

export interface ThemeContextType {
  theme: ThemeTokens;
  isOled: boolean;
  toggleOled: () => void;
  setOled: (value: boolean) => void;
}

export const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export interface ThemeProviderProps {
  children: React.ReactNode;
  initialOled?: boolean;
}

export const ThemeProvider: React.FC<ThemeProviderProps> = ({
  children,
  initialOled = false,
}) => {
  // Gracefully detect if wrapped within SettingsContext
  const settingsContext = useContext(SettingsContext);
  const [localOled, setLocalOled] = useState(initialOled);

  // Synchronize with SettingsContext if available, otherwise local state
  const isOled = settingsContext ? settingsContext.settings.oledMode : localOled;

  // Keep local state in sync if initialOled prop changes (useful for unit tests)
  useEffect(() => {
    setLocalOled(initialOled);
  }, [initialOled]);

  const toggleOled = useCallback(() => {
    if (settingsContext) {
      settingsContext.updateSettings({ oledMode: !settingsContext.settings.oledMode });
    } else {
      setLocalOled(prev => !prev);
    }
  }, [settingsContext]);

  const setOled = useCallback((value: boolean) => {
    if (settingsContext) {
      settingsContext.updateSettings({ oledMode: value });
    } else {
      setLocalOled(value);
    }
  }, [settingsContext]);

  const theme = useMemo(() => {
    return isOled ? OledTheme : DarkTheme;
  }, [isOled]);

  const value = useMemo<ThemeContextType>(() => ({
    theme,
    isOled,
    toggleOled,
    setOled,
  }), [theme, isOled, toggleOled, setOled]);

  return (
    <ThemeContext.Provider value={value}>
      {children}
    </ThemeContext.Provider>
  );
};

export const useTheme = (): ThemeContextType => {
  const context = useContext(ThemeContext);
  if (!context) {
    return {
      theme: DarkTheme,
      isOled: false,
      toggleOled: () => {},
      setOled: () => {},
    };
  }
  return context;
};

export default ThemeProvider;
