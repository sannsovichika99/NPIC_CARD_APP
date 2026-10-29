/**
 * App preferences chosen on the Settings screen: language, light / dark mode and theme color.
 * Saved on the phone (same storage as the pairing), so they survive a restart and "Forget this PC".
 */
import * as SecureStore from 'expo-secure-store';
import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { Appearance, Platform } from 'react-native';

const STORE_KEY = 'card-studio.settings.v1';

export type Language = 'en' | 'km';
export type AppearanceMode = 'system' | 'light' | 'dark';
/** 'pc' = the Theme Color chosen in Card Studio on the PC; otherwise a #RRGGBB color. */
export type AccentChoice = 'pc' | string;

export type Settings = {
  language: Language;
  appearance: AppearanceMode;
  accent: AccentChoice;
};

const DEFAULTS: Settings = { language: 'en', appearance: 'system', accent: 'pc' };

type State = Settings & {
  /** True until the saved settings have been read. */
  loading: boolean;
  update: (patch: Partial<Settings>) => void;
};

const SettingsContext = createContext<State | null>(null);

// SecureStore has no web version: keep the settings in localStorage when testing in a browser.
const storage = {
  get: async () =>
    Platform.OS === 'web' ? globalThis.localStorage?.getItem(STORE_KEY) ?? null : SecureStore.getItemAsync(STORE_KEY),
  set: async (v: string) =>
    Platform.OS === 'web' ? globalThis.localStorage?.setItem(STORE_KEY, v) : SecureStore.setItemAsync(STORE_KEY, v),
};

/** Keeps only valid values, so an old or damaged save never breaks the app. */
function sanitize(raw: unknown): Settings {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Partial<Record<keyof Settings, unknown>>;
  return {
    language: r.language === 'km' ? 'km' : 'en',
    appearance: r.appearance === 'light' || r.appearance === 'dark' ? r.appearance : 'system',
    accent: typeof r.accent === 'string' && (r.accent === 'pc' || /^#[0-9a-f]{6}$/i.test(r.accent)) ? r.accent : 'pc',
  };
}

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [loading, setLoading] = useState(true);
  const [settings, setSettings] = useState<Settings>(DEFAULTS);

  useEffect(() => {
    (async () => {
      try {
        const saved = await storage.get();
        if (saved) setSettings(sanitize(JSON.parse(saved)));
      } catch {
        // unreadable save: keep the defaults
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  // Native parts (alerts, date pickers, keyboard) follow the chosen mode too, not only our own colors.
  useEffect(() => {
    if (Platform.OS === 'web') return;
    Appearance.setColorScheme(settings.appearance === 'system' ? 'unspecified' : settings.appearance);
  }, [settings.appearance]);

  const update = useCallback((patch: Partial<Settings>) => {
    setSettings((old) => {
      const next = sanitize({ ...old, ...patch });
      void storage.set(JSON.stringify(next)).catch(() => {});
      return next;
    });
  }, []);

  const value = useMemo<State>(() => ({ ...settings, loading, update }), [settings, loading, update]);
  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}

export function useSettings() {
  const ctx = useContext(SettingsContext);
  if (!ctx) throw new Error('useSettings must be used inside <SettingsProvider>');
  return ctx;
}
