/**
 * Client for the "Mobile App" server inside Card Studio on the PC
 * (NPIC_CARD_APP/Services/MobileLinkServer.cs). JSON over plain HTTP on the local Wi-Fi,
 * authorised with the pairing code from the QR code on the PC.
 */
import { Platform } from 'react-native';
import * as Device from 'expo-device';
import { File } from 'expo-file-system';

export type Pairing = {
  /** PC addresses from the QR code, best first. `host` is the one that answered last. */
  hosts: string[];
  host: string;
  port: number;
  token: string;
  /** Windows computer name, for display. */
  pcName: string;
};

export type Info = {
  app: string;
  version: string;
  computer: string;
  students: number;
  withPhoto: number;
  printed: number;
  withCard: number;
  source: string;
  template: string;
  accentColor: string;
  photoFolderOk: boolean;
};

export type StudentSummary = {
  studentId: string;
  nameKh: string;
  nameEn: string;
  gender: string;
  faculty: string;
  level: string;
  generation: string;
  hasPhoto: boolean;
  photoVersion: string;
  printedAt: string | null;
  rfidUid: string | null;
};

export type StudentDetail = StudentSummary & {
  dateOfBirth: string;
  dateOfBirthKh: string;
  validUntil: string;
  qrData: string;
  columns: Record<string, string>;
};

export type StudentPage = { total: number; offset: number; items: StudentSummary[] };

/** What is selected in Card Studio on the PC (Print tab). */
export type PcSelection = {
  /** Selected for Print Selected, in tray slot order (at most 2). */
  students: StudentSummary[];
  /** Side tab shown on the PC. */
  side: 'front' | 'back';
  /** Why the PC cannot print now (a window is open, a card is printing…), else null. */
  busy: string | null;
};

export type StudentFilter = '' | 'nophoto' | 'notprinted' | 'nocard';

export type LookupResult = {
  code: string;
  found: boolean;
  student?: StudentDetail;
  /** Set when the student is not in the loaded list but was printed before. */
  history?: {
    studentId: string;
    nameKh: string;
    nameEn: string;
    faculty: string;
    printedAt: string | null;
    rfidUid: string | null;
  };
};

export type PrintItem = {
  printedAt: string;
  studentId: string;
  nameKh: string;
  nameEn: string;
  faculty: string;
  side: 'FRONT' | 'BACK' | string;
  printer: string;
  user: string;
  computer: string;
};

export type HistoryPage = { total: number; printedToday: number; items: PrintItem[] };

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

export const DEFAULT_PORT = 8765;
const TIMEOUT_MS = 12000;

const deviceName = (Device.deviceName || Device.modelName || Platform.OS).slice(0, 40);

// ===================== Pairing link =====================

/**
 * Reads the pairing link from the PC's QR code (or Copy Link on the PC):
 * npiccard://pair?h=192.168.1.20,10.0.0.5&p=8765&t=CODE&n=PC-NAME
 */
export function parsePairingLink(text: string): Pairing | null {
  const trimmed = text.trim();
  const match = /^npiccard:\/\/pair\?(.+)$/i.exec(trimmed);
  if (!match) return null;
  const params: Record<string, string> = {};
  for (const part of match[1].split('&')) {
    const eq = part.indexOf('=');
    if (eq <= 0) continue;
    try {
      params[part.slice(0, eq)] = decodeURIComponent(part.slice(eq + 1).replace(/\+/g, ' '));
    } catch {
      return null;
    }
  }
  const hosts = (params.h || '').split(',').map((h) => h.trim()).filter(Boolean);
  const port = Number(params.p || DEFAULT_PORT);
  const token = (params.t || '').trim();
  if (hosts.length === 0 || !token || !Number.isInteger(port) || port < 1 || port > 65535) return null;
  return { hosts, host: hosts[0], port, token, pcName: params.n || hosts[0] };
}

/** Pairing typed by hand on the Connect screen. */
export function manualPairing(address: string, port: string, code: string): Pairing | null {
  const host = address.trim().replace(/^https?:\/\//i, '').replace(/[:/].*$/, '');
  const p = Number(port.trim() || DEFAULT_PORT);
  const token = code.replace(/\s+/g, '').toUpperCase();
  if (!host || !token || !Number.isInteger(p) || p < 1 || p > 65535) return null;
  return { hosts: [host], host, port: p, token, pcName: host };
}

// ===================== Requests =====================

export function baseUrl(p: Pairing, host = p.host) {
  return `http://${host}:${p.port}`;
}

export function authHeaders(p: Pairing): Record<string, string> {
  return { Authorization: `Bearer ${p.token}`, 'X-Device-Name': deviceName };
}

async function request<T>(p: Pairing, path: string, init: RequestInit = {}, host = p.host): Promise<T> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), TIMEOUT_MS);
  let response: Response;
  try {
    response = await fetch(baseUrl(p, host) + path, {
      ...init,
      headers: { Accept: 'application/json', ...authHeaders(p), ...(init.headers as Record<string, string>) },
      signal: controller.signal,
    });
  } catch (e) {
    throw new ApiError(
      controller.signal.aborted
        ? `The PC (${host}) did not answer. Check that Card Studio is open and the phone is on the same Wi-Fi.`
        : `Cannot reach the PC (${host}). Check the Wi-Fi, and that Mobile App is started in Card Studio.`,
      0,
    );
  } finally {
    clearTimeout(timer);
  }
  const text = await response.text();
  let body: any = null;
  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    /* not JSON */
  }
  if (!response.ok) throw new ApiError(body?.error || `The PC answered ${response.status}.`, response.status);
  return body as T;
}

/**
 * Tries every address from the QR code and returns the pairing with the one that answered
 * (a PC with Wi-Fi and a cable has two). Throws the most useful error when none works.
 */
export async function connect(p: Pairing): Promise<{ pairing: Pairing; info: Info }> {
  const order = [p.host, ...p.hosts.filter((h) => h !== p.host)];
  let lastError: ApiError | null = null;
  for (const host of order) {
    try {
      const info = await request<Info>(p, '/api/info', {}, host);
      return { pairing: { ...p, host }, info };
    } catch (e) {
      const err = e as ApiError;
      if (err.status === 401 || err.status === 429) throw err; // reached the PC: the code is the problem
      lastError = err;
    }
  }
  throw lastError ?? new ApiError('Cannot reach the PC.', 0);
}

export const api = {
  info: (p: Pairing) => request<Info>(p, '/api/info'),

  /** Tiny request that keeps this phone shown as connected in Card Studio's status bar on the PC. */
  heartbeat: (p: Pairing) => request<{ ok: boolean }>(p, '/api/heartbeat'),

  students: (p: Pairing, q: string, filter: StudentFilter, offset = 0, limit = 50) =>
    request<StudentPage>(
      p,
      `/api/students?q=${encodeURIComponent(q)}&filter=${filter}&offset=${offset}&limit=${limit}`,
    ),

  student: (p: Pairing, id: string) => request<StudentDetail>(p, `/api/students/${encodeURIComponent(id)}`),

  lookup: (p: Pairing, code: string) => request<LookupResult>(p, `/api/lookup?code=${encodeURIComponent(code)}`),

  /**
   * Starts Print Selected on the PC for one side of the student's card. Only says the print started: the tray load is
   * confirmed at the PC, and the print history shows when the card was really printed.
   */
  print: (p: Pairing, id: string, side: 'front' | 'back') =>
    request<{ started: boolean; studentId: string; side: string }>(
      p,
      `/api/students/${encodeURIComponent(id)}/print?side=${side}`,
      { method: 'POST' },
    ),

  /** The students selected in Card Studio right now. */
  selected: (p: Pairing) => request<PcSelection>(p, '/api/selected'),

  /**
   * Prints the PC's selection on one side. `ids` are the students shown on the phone: the PC refuses (409) when
   * its selection changed meanwhile, so a card never prints for a student the person did not see.
   */
  printSelected: (p: Pairing, ids: string[], side: 'front' | 'back') =>
    request<{ started: boolean; studentIds: string[]; side: string }>(
      p,
      `/api/selected/print?side=${side}&ids=${ids.map(encodeURIComponent).join(',')}`,
      { method: 'POST' },
    ),

  history: (p: Pairing, limit = 100) => request<HistoryPage>(p, `/api/history?limit=${limit}`),

  /** Sends a JPEG to the PC; it becomes {StudentId}.jpg in the photo folder (the old photo is kept in _replaced). */
  async uploadPhoto(p: Pairing, id: string, uri: string, onProgress?: (fraction: number) => void) {
    const url = `${baseUrl(p)}/api/students/${encodeURIComponent(id)}/photo`;
    const headers = { ...authHeaders(p), 'Content-Type': 'image/jpeg' };
    let status: number;
    let text: string;
    if (Platform.OS === 'web') {
      const blob = await (await fetch(uri)).blob();
      const r = await fetch(url, { method: 'PUT', headers, body: blob });
      status = r.status;
      text = await r.text();
    } else {
      const result = await new File(uri).upload(url, {
        httpMethod: 'PUT',
        headers,
        mimeType: 'image/jpeg',
        onProgress: ({ bytesSent, totalBytes }) => totalBytes > 0 && onProgress?.(bytesSent / totalBytes),
      });
      status = result.status;
      text = result.body;
    }
    let body: any = null;
    try {
      body = JSON.parse(text);
    } catch {
      /* not JSON */
    }
    if (status < 200 || status >= 300) throw new ApiError(body?.error || `The PC answered ${status}.`, status);
    return body as { saved: boolean; replaced: boolean; student: StudentSummary };
  },
};

// ===================== Images =====================

/** expo-image source for a student photo; the version makes a new photo show at once instead of a cached one. */
export function photoSource(p: Pairing, s: Pick<StudentSummary, 'studentId' | 'photoVersion'>) {
  return {
    uri: `${baseUrl(p)}/api/students/${encodeURIComponent(s.studentId)}/photo?v=${s.photoVersion}`,
    headers: authHeaders(p),
    cacheKey: `photo-${p.pcName}-${s.studentId}-${s.photoVersion}`,
  };
}

/** The card side drawn by Card Studio with the design in use. `stamp` forces a fresh render. */
export function cardSource(p: Pairing, id: string, side: 'front' | 'back', stamp: string) {
  return {
    uri: `${baseUrl(p)}/api/students/${encodeURIComponent(id)}/card?side=${side}&w=1100&s=${encodeURIComponent(stamp)}`,
    headers: authHeaders(p),
  };
}
