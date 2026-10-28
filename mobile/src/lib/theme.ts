import { useColorScheme } from 'react-native';
import { useConnection } from './connection';

/** Card Studio's default violet, used until the PC tells us its Theme Color. */
export const DEFAULT_ACCENT = '#6C63F5';

export type Theme = ReturnType<typeof makeTheme>;

function makeTheme(dark: boolean, accent: string) {
  return {
    dark,
    accent,
    accentSoft: withAlpha(accent, dark ? 0.22 : 0.12),
    onAccent: '#FFFFFF',
    // Same light / dark tokens as the desktop app (UiKit.Theme), so both feel like one product.
    background: dark ? '#11121B' : '#F5F6FA',
    surface: dark ? '#1C1D2A' : '#FFFFFF',
    surfaceAlt: dark ? '#212231' : '#FAFAFD',
    border: dark ? '#37394E' : '#E4E5EE',
    text: dark ? '#E8E9F5' : '#2B2D42',
    textSoft: dark ? '#AAADC4' : '#686B82',
    textMuted: dark ? '#80839E' : '#9699B0',
    success: dark ? '#48C784' : '#22A064',
    successSoft: dark ? 'rgba(72,199,132,0.18)' : 'rgba(34,160,100,0.12)',
    warning: dark ? '#F0A546' : '#C46E00',
    warningSoft: dark ? 'rgba(240,165,70,0.18)' : '#FFF3E2',
    danger: dark ? '#FF7080' : '#D63048',
    dangerSoft: dark ? 'rgba(255,112,128,0.18)' : '#FDE9EC',
  };
}

export function useTheme(): Theme {
  const dark = useColorScheme() === 'dark';
  const { info } = useConnection();
  const accent = isHex(info?.accentColor) ? info!.accentColor : DEFAULT_ACCENT;
  return makeTheme(dark, accent);
}

function isHex(value?: string | null): value is string {
  return !!value && /^#[0-9a-f]{6}$/i.test(value);
}

function withAlpha(hex: string, alpha: number) {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha})`;
}

/** Khmer and Latin text: Khmer glyphs need a little more line height to not clip. */
export const khmerText = { lineHeight: 26 };

/** "2026-10-28T09:41:00" → "28 Oct 2026, 09:41" */
export function formatDateTime(iso: string | null | undefined) {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getDate()} ${months[d.getMonth()]} ${d.getFullYear()}, ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** "30/09/2028" (the card's ValidUntil) → Date, or null. */
export function parseCardDate(ddMMyyyy: string | null | undefined): Date | null {
  const m = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec((ddMMyyyy || '').trim());
  if (!m) return null;
  const d = new Date(Number(m[3]), Number(m[2]) - 1, Number(m[1]), 23, 59, 59);
  return isNaN(d.getTime()) ? null : d;
}
