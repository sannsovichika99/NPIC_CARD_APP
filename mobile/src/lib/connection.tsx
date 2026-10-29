/**
 * The paired PC: kept in the phone's secure storage, so the app reconnects by itself after a restart.
 * Screens read it with useConnection(); `info` is refreshed when the app comes back to the foreground.
 * While the app is open it sends a heartbeat every 30 s, so the PC shows "Phone connected" in its status bar.
 */
import * as SecureStore from 'expo-secure-store';
import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { AppState, Platform } from 'react-native';
import { api, ApiError, connect as connectToPc, Info, Pairing } from './api';

const STORE_KEY = 'card-studio.pairing.v1';
/** Must stay well under MobileLinkServer.PhoneTimeout on the PC (90 s). */
const HEARTBEAT_MS = 30_000;

type State = {
  /** True until the saved pairing has been read. */
  loading: boolean;
  pairing: Pairing | null;
  info: Info | null;
  /** Last connection problem (PC closed, other Wi-Fi…); null when the PC answered. */
  error: string | null;
  /** The PC refused the code: the phone must scan the QR code again. */
  needsPairing: boolean;
  pair: (p: Pairing) => Promise<void>;
  refresh: () => Promise<void>;
  forget: () => Promise<void>;
};

const ConnectionContext = createContext<State | null>(null);

// SecureStore has no web version: keep the pairing in localStorage when testing in a browser.
const storage = {
  get: async () =>
    Platform.OS === 'web' ? globalThis.localStorage?.getItem(STORE_KEY) ?? null : SecureStore.getItemAsync(STORE_KEY),
  set: async (v: string) =>
    Platform.OS === 'web' ? globalThis.localStorage?.setItem(STORE_KEY, v) : SecureStore.setItemAsync(STORE_KEY, v),
  clear: async () =>
    Platform.OS === 'web' ? globalThis.localStorage?.removeItem(STORE_KEY) : SecureStore.deleteItemAsync(STORE_KEY),
};

export function ConnectionProvider({ children }: { children: ReactNode }) {
  const [loading, setLoading] = useState(true);
  const [pairing, setPairing] = useState<Pairing | null>(null);
  const [info, setInfo] = useState<Info | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [needsPairing, setNeedsPairing] = useState(false);
  const pairingRef = useRef<Pairing | null>(null);
  pairingRef.current = pairing;

  const save = useCallback(async (p: Pairing) => {
    setPairing(p);
    await storage.set(JSON.stringify(p));
  }, []);

  /** Re-reads the PC info, trying the other addresses of the QR code when the last one stopped answering. */
  const refresh = useCallback(async () => {
    const p = pairingRef.current;
    if (!p) return;
    try {
      const result = await connectToPc(p);
      setInfo(result.info);
      setError(null);
      setNeedsPairing(false);
      if (result.pairing.host !== p.host) await save(result.pairing);
    } catch (e) {
      const err = e as ApiError;
      setError(err.message);
      setNeedsPairing(err.status === 401);
    }
  }, [save]);

  const pair = useCallback(
    async (p: Pairing) => {
      const result = await connectToPc(p); // throws with a message for the Connect screen
      setInfo(result.info);
      setError(null);
      setNeedsPairing(false);
      await save({ ...result.pairing, pcName: result.info.computer || result.pairing.pcName });
    },
    [save],
  );

  const forget = useCallback(async () => {
    await storage.clear();
    setPairing(null);
    setInfo(null);
    setError(null);
    setNeedsPairing(false);
  }, []);

  // Saved pairing → connect at start.
  useEffect(() => {
    (async () => {
      try {
        const saved = await storage.get();
        if (saved) {
          const p = JSON.parse(saved) as Pairing;
          pairingRef.current = p;
          setPairing(p);
          void refresh();
        }
      } catch {
        await storage.clear();
      } finally {
        setLoading(false);
      }
    })();
  }, [refresh]);

  // Back from the background (e.g. after the camera) → fresh counts.
  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') void refresh();
    });
    return () => sub.remove();
  }, [refresh]);

  // App in the foreground and paired → tell the PC we are still here. Failures are ignored: refresh() reports problems.
  useEffect(() => {
    if (!pairing) return;
    let timer: ReturnType<typeof setInterval> | null = null;
    const beat = () => {
      const p = pairingRef.current;
      if (p) api.heartbeat(p).catch(() => {});
    };
    const start = () => {
      if (timer === null) timer = setInterval(beat, HEARTBEAT_MS);
    };
    const stop = () => {
      if (timer !== null) clearInterval(timer);
      timer = null;
    };
    if (AppState.currentState === 'active') start();
    const sub = AppState.addEventListener('change', (state) => (state === 'active' ? start() : stop()));
    return () => {
      stop();
      sub.remove();
    };
  }, [pairing]);

  const value = useMemo<State>(
    () => ({ loading, pairing, info, error, needsPairing, pair, refresh, forget }),
    [loading, pairing, info, error, needsPairing, pair, refresh, forget],
  );
  return <ConnectionContext.Provider value={value}>{children}</ConnectionContext.Provider>;
}

export function useConnection() {
  const ctx = useContext(ConnectionContext);
  if (!ctx) throw new Error('useConnection must be used inside <ConnectionProvider>');
  return ctx;
}

/**
 * The pairing on screens that only show while paired. Keeps the last one for the render in which
 * "Forget this PC" clears it, before the router has unmounted the screen.
 */
export function usePairing(): Pairing {
  const { pairing } = useConnection();
  const last = useRef<Pairing | null>(pairing);
  if (pairing) last.current = pairing;
  if (!last.current) throw new Error('Not paired');
  return last.current;
}

export { api };
