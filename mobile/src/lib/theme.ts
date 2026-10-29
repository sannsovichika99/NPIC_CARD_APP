import { useColorScheme } from 'react-native';
import { useConnection } from './connection';
import { useSettings } from './settings';

/** Card Studio's default violet, used until the PC tells us its Theme Color. */
export const DEFAULT_ACCENT = '#6C63F5';

/**
 * Theme colors offered on the Settings screen. All keep white button text readable (≥ 4.3:1) and
 * stay visible as accent text on the dark surfaces (≥ 3.5:1), like the default violet.
 */
export const ACCENT_PRESETS = [
  { key: 'violet', hex: '#6C63F5' },
  { key: 'blue', hex: '#3B6FE0' },
  { key: 'teal', hex: '#0E7C86' },
  { key: 'green', hex: '#1F8A4C' },
  { key: 'orange', hex: '#D14D12' },
  { key: 'rose', hex: '#E11D48' },
  { key: 'magenta', hex: '#C026D3' },
] as const;

export type AccentPresetKey = (typeof ACCENT_PRESETS)[number]['key'];

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
  const system = useColorScheme();
  const { appearance, accent: choice } = useSettings();
  const pcAccent = usePcAccent();
  const dark = appearance === 'system' ? system === 'dark' : appearance === 'dark';
  const accent = choice !== 'pc' && isHex(choice) ? choice : pcAccent;
  return makeTheme(dark, accent);
}

/** The Theme Color set in Card Studio on the PC (the default violet when not paired yet). */
export function usePcAccent(): string {
  const { info } = useConnection();
  return isHex(info?.accentColor) ? info!.accentColor : DEFAULT_ACCENT;
}

export function isHex(value?: string | null): value is string {
  return !!value && /^#[0-9a-f]{6}$/i.test(value);
}

function withAlpha(hex: string, alpha: number) {
  const n = parseInt(hex.slice(1), 16);
  return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha})`;
}

/** Khmer and Latin text: Khmer glyphs need a little more line height to not clip. */
export const khmerText = { lineHeight: 26 };

const EN_MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "2026-10-28T09:41:00" → "28 Oct 2026, 09:41" (month names from the chosen language, see useT().months). */
export function formatDateTime(iso: string | null | undefined, months: readonly string[] = EN_MONTHS) {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
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
