import Ionicons from '@expo/vector-icons/Ionicons';
import * as Haptics from 'expo-haptics';
import { ReactNode } from 'react';
import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Badge, Button, Card, IconName } from '@/components/ui';
import { useConnection } from '@/lib/connection';
import { LANGUAGES, useT } from '@/lib/i18n';
import { AppearanceMode, useSettings } from '@/lib/settings';
import { ACCENT_PRESETS, usePcAccent, useTheme } from '@/lib/theme';

/** Language, light / dark mode and theme color. Works paired or not. */
export default function SettingsScreen() {
  const t = useTheme();
  const tr = useT();
  const settings = useSettings();
  const { pairing } = useConnection();
  const pcAccent = usePcAccent();

  const pick = (patch: Parameters<typeof settings.update>[0]) => {
    void Haptics.selectionAsync();
    settings.update(patch);
  };

  const modes: { key: AppearanceMode; label: string; icon: IconName }[] = [
    { key: 'system', label: tr.settings.system, icon: 'phone-portrait-outline' },
    { key: 'light', label: tr.settings.light, icon: 'sunny-outline' },
    { key: 'dark', label: tr.settings.dark, icon: 'moon-outline' },
  ];

  return (
    <ScrollView contentContainerStyle={styles.page}>
      {/* ---- Language ---- */}
      <Section icon="language-outline" title={tr.settings.language}>
        {LANGUAGES.map((l, i) => {
          const on = settings.language === l.key;
          return (
            <Pressable
              key={l.key}
              onPress={() => pick({ language: l.key })}
              accessibilityRole="radio"
              accessibilityState={{ checked: on }}
              style={({ pressed }) => [
                styles.option,
                i > 0 && { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: t.border },
                pressed && { opacity: 0.7 },
              ]}>
              <View style={{ flex: 1 }}>
                <Text style={[styles.optionLabel, { color: t.text, lineHeight: 26 }]}>{l.label}</Text>
                {l.label !== l.sub && <Text style={{ color: t.textMuted, fontSize: 13 }}>{l.sub}</Text>}
              </View>
              <Radio on={on} />
            </Pressable>
          );
        })}
      </Section>

      {/* ---- Dark mode ---- */}
      <Section icon="contrast-outline" title={tr.settings.appearance}>
        <View style={[styles.segment, { backgroundColor: t.surfaceAlt, borderColor: t.border }]}>
          {modes.map((m) => {
            const on = settings.appearance === m.key;
            return (
              <Pressable
                key={m.key}
                onPress={() => pick({ appearance: m.key })}
                accessibilityRole="radio"
                accessibilityState={{ checked: on }}
                accessibilityLabel={m.label}
                style={[styles.segmentItem, on && { backgroundColor: t.accent }]}>
                <Ionicons name={m.icon} size={20} color={on ? t.onAccent : t.textSoft} />
                <Text style={{ color: on ? t.onAccent : t.textSoft, fontWeight: '600', lineHeight: 22 }} numberOfLines={1}>
                  {m.label}
                </Text>
              </Pressable>
            );
          })}
        </View>
        {settings.appearance === 'system' && <Text style={[styles.hint, { color: t.textMuted }]}>{tr.settings.systemHint}</Text>}
      </Section>

      {/* ---- Theme color ---- */}
      <Section icon="color-palette-outline" title={tr.settings.themeColor}>
        <Pressable
          onPress={() => pick({ accent: 'pc' })}
          accessibilityRole="radio"
          accessibilityState={{ checked: settings.accent === 'pc' }}
          style={({ pressed }) => [styles.option, pressed && { opacity: 0.7 }]}>
          <View style={[styles.pcSwatch, { backgroundColor: pcAccent }]}>
            <Ionicons name="desktop-outline" size={16} color="#FFFFFF" />
          </View>
          <View style={{ flex: 1 }}>
            <Text style={[styles.optionLabel, { color: t.text, lineHeight: 26 }]}>{tr.settings.matchPc}</Text>
            <Text style={{ color: t.textMuted, fontSize: 13, lineHeight: 20 }}>
              {pairing ? tr.settings.matchPcHint : tr.settings.matchPcUnpaired}
            </Text>
          </View>
          <Radio on={settings.accent === 'pc'} />
        </Pressable>

        <View style={[styles.swatches, { borderTopColor: t.border }]}>
          {ACCENT_PRESETS.map((p) => {
            const on = settings.accent.toLowerCase() === p.hex.toLowerCase();
            const name = tr.settings.colors[p.key];
            return (
              <Pressable
                key={p.key}
                onPress={() => pick({ accent: p.hex })}
                accessibilityRole="radio"
                accessibilityState={{ checked: on }}
                accessibilityLabel={name}
                style={styles.swatchCell}>
                <View style={[styles.swatchRing, { borderColor: on ? p.hex : 'transparent' }]}>
                  <View style={[styles.swatch, { backgroundColor: p.hex }]}>
                    {on && <Ionicons name="checkmark" size={20} color="#FFFFFF" />}
                  </View>
                </View>
                <Text style={{ color: on ? t.text : t.textSoft, fontSize: 12, lineHeight: 20 }} numberOfLines={1}>
                  {name}
                </Text>
              </Pressable>
            );
          })}
        </View>

        {/* Live preview with the current colors. */}
        <View style={[styles.preview, { backgroundColor: t.background, borderColor: t.border }]}>
          <Text style={{ color: t.textMuted, fontSize: 12, lineHeight: 20 }}>{tr.settings.preview}</Text>
          <View style={styles.previewRow}>
            <Button title={tr.settings.previewButton} icon="checkmark" onPress={() => {}} style={{ minHeight: 40, flex: 1 }} />
            <Badge label={tr.common.printed} tone="success" icon="checkmark" />
            <Badge label={tr.common.rfid} tone="accent" icon="radio-outline" />
          </View>
        </View>
      </Section>

      <Text style={[styles.footer, { color: t.textMuted }]}>{tr.settings.savedNote}</Text>
    </ScrollView>
  );
}

function Section({ icon, title, children }: { icon: IconName; title: string; children: ReactNode }) {
  const t = useTheme();
  return (
    <Card style={{ gap: 8 }}>
      <View style={styles.sectionHead}>
        <Ionicons name={icon} size={18} color={t.accent} />
        <Text style={[styles.section, { color: t.text }]}>{title}</Text>
      </View>
      {children}
    </Card>
  );
}

function Radio({ on }: { on: boolean }) {
  const t = useTheme();
  return (
    <View style={[styles.radio, { borderColor: on ? t.accent : t.border }]}>
      {on && <View style={[styles.radioDot, { backgroundColor: t.accent }]} />}
    </View>
  );
}

const styles = StyleSheet.create({
  page: { padding: 16, gap: 12, paddingBottom: 40 },
  sectionHead: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  section: { fontSize: 17, fontWeight: '700', lineHeight: 26 },
  option: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 52, paddingVertical: 6 },
  optionLabel: { fontSize: 16, fontWeight: '600' },
  radio: { width: 22, height: 22, borderRadius: 11, borderWidth: 2, alignItems: 'center', justifyContent: 'center' },
  radioDot: { width: 11, height: 11, borderRadius: 6 },
  segment: { flexDirection: 'row', borderRadius: 14, borderWidth: 1, padding: 3, marginTop: 4 },
  segmentItem: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 2, paddingVertical: 8, borderRadius: 11, minHeight: 56 },
  hint: { fontSize: 13, lineHeight: 20 },
  pcSwatch: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
  swatches: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    rowGap: 10,
    paddingTop: 12,
    borderTopWidth: StyleSheet.hairlineWidth,
  },
  swatchCell: { width: '25%', alignItems: 'center', gap: 4 },
  swatchRing: { padding: 3, borderRadius: 26, borderWidth: 2 },
  swatch: { width: 40, height: 40, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  preview: { borderRadius: 12, borderWidth: StyleSheet.hairlineWidth, padding: 12, gap: 8, marginTop: 4 },
  previewRow: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  footer: { textAlign: 'center', fontSize: 12, lineHeight: 20, marginHorizontal: 12 },
});
