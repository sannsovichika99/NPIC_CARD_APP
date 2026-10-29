import Ionicons from '@expo/vector-icons/Ionicons';
import { Image } from 'expo-image';
import { ComponentProps, ReactNode } from 'react';
import { ActivityIndicator, Pressable, StyleProp, StyleSheet, Text, View, ViewStyle } from 'react-native';
import { photoSource, StudentSummary } from '@/lib/api';
import { useConnection, usePairing } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { useTheme } from '@/lib/theme';

export type IconName = ComponentProps<typeof Ionicons>['name'];

export function Card({ children, style }: { children: ReactNode; style?: StyleProp<ViewStyle> }) {
  const t = useTheme();
  return (
    <View style={[styles.card, { backgroundColor: t.surface, borderColor: t.border }, style]}>{children}</View>
  );
}

type ButtonKind = 'primary' | 'secondary' | 'ghost' | 'danger';

export function Button({
  title,
  icon,
  onPress,
  kind = 'primary',
  busy,
  disabled,
  style,
}: {
  title: string;
  icon?: IconName;
  onPress: () => void;
  kind?: ButtonKind;
  busy?: boolean;
  disabled?: boolean;
  style?: StyleProp<ViewStyle>;
}) {
  const t = useTheme();
  const bg = kind === 'primary' ? t.accent : kind === 'danger' ? t.dangerSoft : kind === 'secondary' ? t.surface : 'transparent';
  const fg = kind === 'primary' ? t.onAccent : kind === 'danger' ? t.danger : t.accent;
  const off = disabled || busy;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityState={{ disabled: !!off, busy: !!busy }}
      onPress={onPress}
      disabled={off}
      style={({ pressed }) => [
        styles.button,
        { backgroundColor: bg, borderColor: kind === 'secondary' ? t.border : 'transparent', opacity: off ? 0.55 : pressed ? 0.8 : 1 },
        style,
      ]}>
      {busy ? (
        <ActivityIndicator color={fg} />
      ) : (
        <>
          {icon && <Ionicons name={icon} size={19} color={fg} />}
          <Text style={[styles.buttonText, { color: fg }]}>{title}</Text>
        </>
      )}
    </Pressable>
  );
}

/** Gear icon that opens Settings (tab headers, Connect screen). 44 pt touch target. */
export function SettingsButton({ onPress, style }: { onPress: () => void; style?: StyleProp<ViewStyle> }) {
  const t = useTheme();
  const tr = useT();
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={tr.common.settings}
      hitSlop={6}
      style={({ pressed }) => [styles.iconButton, { opacity: pressed ? 0.6 : 1 }, style]}>
      <Ionicons name="settings-outline" size={22} color={t.accent} />
    </Pressable>
  );
}

type Tone = 'success' | 'warning' | 'danger' | 'neutral' | 'accent';

export function Badge({ label, tone = 'neutral', icon }: { label: string; tone?: Tone; icon?: IconName }) {
  const t = useTheme();
  const colors: Record<Tone, [string, string]> = {
    success: [t.successSoft, t.success],
    warning: [t.warningSoft, t.warning],
    danger: [t.dangerSoft, t.danger],
    accent: [t.accentSoft, t.accent],
    neutral: [t.surfaceAlt, t.textSoft],
  };
  const [bg, fg] = colors[tone];
  return (
    <View style={[styles.badge, { backgroundColor: bg }]}>
      {icon && <Ionicons name={icon} size={12} color={fg} />}
      <Text style={[styles.badgeText, { color: fg }]}>{label}</Text>
    </View>
  );
}

/** Student photo from the PC, or a placeholder with the initials when there is none yet. */
export function StudentPhoto({
  student,
  width,
  radius = 10,
}: {
  student: Pick<StudentSummary, 'studentId' | 'photoVersion' | 'hasPhoto' | 'nameEn'>;
  width: number;
  radius?: number;
}) {
  const t = useTheme();
  const tr = useT();
  const pairing = usePairing();
  const height = Math.round((width * 22) / 18.7); // card photo box: 18.7 × 22 mm
  if (!student.hasPhoto) {
    const initials = (student.nameEn || student.studentId)
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0]?.toUpperCase())
      .join('');
    return (
      <View
        accessibilityLabel={tr.ui.noPhotoYet}
        style={{ width, height, borderRadius: radius, backgroundColor: t.surfaceAlt, borderColor: t.border, borderWidth: 1, alignItems: 'center', justifyContent: 'center' }}>
        <Ionicons name="person-outline" size={width * 0.38} color={t.textMuted} />
        {width > 70 && <Text style={{ color: t.textMuted, marginTop: 4, fontWeight: '600' }}>{initials}</Text>}
      </View>
    );
  }
  return (
    <Image
      accessibilityLabel={tr.ui.photoOf(student.nameEn || student.studentId)}
      source={photoSource(pairing, student)}
      style={{ width, height, borderRadius: radius, backgroundColor: t.surfaceAlt }}
      contentFit="cover"
      contentPosition="top"
      transition={150}
      recyclingKey={student.studentId}
    />
  );
}

/** Banner on the main screens when the PC does not answer, with a retry. */
export function ConnectionBanner() {
  const t = useTheme();
  const tr = useT();
  const { error, needsPairing, refresh, forget } = useConnection();
  if (!error) return null;
  return (
    <Pressable
      onPress={needsPairing ? forget : refresh}
      style={[styles.banner, { backgroundColor: needsPairing ? t.dangerSoft : t.warningSoft }]}>
      <Ionicons name={needsPairing ? 'key-outline' : 'cloud-offline-outline'} size={18} color={needsPairing ? t.danger : t.warning} />
      <Text style={{ flex: 1, color: needsPairing ? t.danger : t.warning }}>
        {error}
        {'\n'}
        <Text style={{ fontWeight: '700' }}>{needsPairing ? tr.ui.tapToPairAgain : tr.ui.tapToRetry}</Text>
      </Text>
    </Pressable>
  );
}

export function EmptyState({ icon, title, text, action }: { icon: IconName; title: string; text?: string; action?: ReactNode }) {
  const t = useTheme();
  return (
    <View style={styles.empty}>
      <Ionicons name={icon} size={44} color={t.textMuted} />
      <Text style={[styles.emptyTitle, { color: t.text }]}>{title}</Text>
      {text ? <Text style={[styles.emptyText, { color: t.textSoft }]}>{text}</Text> : null}
      {action}
    </View>
  );
}

export function Row({ label, value, mono }: { label: string; value?: string | null; mono?: boolean }) {
  const t = useTheme();
  if (!value) return null;
  return (
    <View style={[styles.row, { borderBottomColor: t.border }]}>
      <Text style={[styles.rowLabel, { color: t.textSoft }]}>{label}</Text>
      <Text selectable style={[styles.rowValue, { color: t.text, fontFamily: mono ? 'monospace' : undefined }]}>
        {value}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderRadius: 16, borderWidth: StyleSheet.hairlineWidth, padding: 16 },
  button: {
    minHeight: 48,
    borderRadius: 24,
    paddingHorizontal: 18,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'center',
    gap: 8,
    borderWidth: 1,
  },
  buttonText: { fontSize: 16, fontWeight: '600' },
  iconButton: { width: 44, height: 44, alignItems: 'center', justifyContent: 'center', borderRadius: 22 },
  badge: { flexDirection: 'row', alignItems: 'center', gap: 4, borderRadius: 10, paddingHorizontal: 8, paddingVertical: 3 },
  badgeText: { fontSize: 12, fontWeight: '600' },
  banner: { flexDirection: 'row', gap: 10, padding: 12, marginHorizontal: 16, marginTop: 8, borderRadius: 12, alignItems: 'center' },
  empty: { alignItems: 'center', padding: 32, gap: 10 },
  emptyTitle: { fontSize: 17, fontWeight: '600', textAlign: 'center' },
  emptyText: { fontSize: 14, textAlign: 'center', lineHeight: 20 },
  row: { flexDirection: 'row', paddingVertical: 10, borderBottomWidth: StyleSheet.hairlineWidth, gap: 12 },
  rowLabel: { width: 110, fontSize: 14 },
  rowValue: { flex: 1, fontSize: 15, lineHeight: 24 },
});
