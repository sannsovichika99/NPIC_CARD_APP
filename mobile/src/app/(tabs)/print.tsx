import Ionicons from '@expo/vector-icons/Ionicons';
import { Image } from 'expo-image';
import * as Haptics from 'expo-haptics';
import { useFocusEffect } from 'expo-router';
import { useCallback, useRef, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Badge, Button, Card, ConnectionBanner, EmptyState, StudentPhoto } from '@/components/ui';
import { api, ApiError, cardSource, PcSelection } from '@/lib/api';
import { usePairing } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { khmerText, useTheme } from '@/lib/theme';

/** How often the screen re-reads the PC's selection while it is shown. */
const POLL_MS = 3000;

type Side = 'front' | 'back';

/**
 * Print tab: the student(s) selected in Card Studio on the PC, with their card, and one button that starts
 * Print Selected there. Follows the PC while open, so picking another student on the PC updates this screen.
 */
export default function PrintScreen() {
  const t = useTheme();
  const tr = useT();
  const pairing = usePairing();
  const [sel, setSel] = useState<PcSelection | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [side, setSide] = useState<Side>('front');
  const [stamp, setStamp] = useState(() => String(Date.now()));
  const [refreshing, setRefreshing] = useState(false);
  const [printing, setPrinting] = useState(false);
  const shownKey = useRef<string | null>(null); // students shown last: a new selection takes the PC's side

  const load = useCallback(async () => {
    try {
      const next = await api.selected(pairing);
      const key = next.students.map((s) => `${s.studentId}:${s.photoVersion}`).join(',');
      if (key !== shownKey.current) {
        shownKey.current = key;
        setSide(next.side);
        setStamp(String(Date.now())); // fresh card pictures for the new students / photos
      }
      setSel(next);
      setError(null);
    } catch (e) {
      const err = e as ApiError;
      setError(err.status === 404 ? tr.printPc.updatePc : err.message);
    } finally {
      setRefreshing(false);
    }
  }, [pairing, tr]);

  // Shown → read now, then keep following the PC; hidden → stop.
  useFocusEffect(
    useCallback(() => {
      void load();
      const timer = setInterval(() => void load(), POLL_MS);
      return () => clearInterval(timer);
    }, [load]),
  );

  const students = sel?.students ?? [];
  const sideName = side === 'front' ? tr.common.front : tr.common.back_side;

  function confirmPrint() {
    if (students.length === 0) return;
    const names = students.map((s) => s.nameEn || s.studentId).join(', ');
    Alert.alert(tr.printPc.confirmTitle, tr.printPc.confirmText(sideName, names), [
      { text: tr.common.cancel, style: 'cancel' },
      { text: tr.student.printConfirm, onPress: () => void sendPrint() },
    ]);
  }

  async function sendPrint() {
    setPrinting(true);
    try {
      await api.printSelected(
        pairing,
        students.map((s) => s.studentId),
        side,
      );
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      Alert.alert(tr.student.printSent, tr.printPc.sentText);
    } catch (e) {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      const err = e as ApiError;
      Alert.alert(tr.student.notPrintedTitle, err.status === 404 ? tr.printPc.updatePc : err.message);
    } finally {
      setPrinting(false);
      void load(); // the selection or the busy state may have changed
    }
  }

  if (!sel && !error) return <ActivityIndicator color={t.accent} style={{ marginTop: 40 }} />;

  return (
    <ScrollView
      contentContainerStyle={styles.page}
      refreshControl={
        <RefreshControl
          refreshing={refreshing}
          onRefresh={() => {
            setRefreshing(true);
            setStamp(String(Date.now()));
            void load();
          }}
          tintColor={t.accent}
        />
      }>
      <ConnectionBanner />

      {error && !sel ? (
        <EmptyState icon="alert-circle-outline" title={tr.printPc.cannotLoad} text={error} action={<Button title={tr.common.tryAgain} onPress={load} />} />
      ) : students.length === 0 ? (
        <EmptyState icon="desktop-outline" title={tr.printPc.none} text={tr.printPc.noneText} />
      ) : (
        <>
          <View style={styles.titleRow}>
            <View style={[styles.liveDot, { backgroundColor: t.success }]} />
            <Text style={[styles.caption, { color: t.textSoft }]}>{tr.printPc.live}</Text>
          </View>

          {/* ---- Who is selected on the PC ---- */}
          <Card style={{ gap: 12 }}>
            <Text style={[styles.section, { color: t.text }]}>{tr.printPc.title}</Text>
            {students.map((s) => (
              <View key={s.studentId} style={styles.student}>
                <StudentPhoto student={s} width={52} radius={8} />
                <View style={{ flex: 1, gap: 2 }}>
                  <Text style={[styles.nameKh, khmerText, { color: t.text }]} numberOfLines={1}>
                    {s.nameKh || s.nameEn}
                  </Text>
                  <Text style={[styles.nameEn, { color: t.textSoft }]} numberOfLines={1}>
                    {s.nameEn}
                  </Text>
                  <Text style={{ color: t.accent, fontWeight: '700' }}>{s.studentId}</Text>
                </View>
                {s.printedAt ? <Badge label={tr.common.printed} tone="success" icon="checkmark" /> : <Badge label={tr.common.notPrinted} />}
              </View>
            ))}
            {students.length > 1 && <Text style={[styles.caption, { color: t.textMuted }]}>{tr.printPc.oneLoad}</Text>}
          </Card>

          {/* ---- Side and card preview ---- */}
          <Card style={{ gap: 12 }}>
            <View style={styles.cardHead}>
              <Text style={[styles.section, { color: t.text }]}>{tr.student.card}</Text>
              <SideSwitch side={side} onChange={setSide} />
            </View>
            {students.map((s) => (
              <Image
                key={s.studentId}
                source={cardSource(pairing, s.studentId, side, stamp)}
                style={[styles.card, { borderColor: t.border, backgroundColor: t.surfaceAlt }]}
                contentFit="contain"
                transition={150}
                accessibilityLabel={tr.student.sideOfCard(sideName)}
              />
            ))}
          </Card>

          {sel?.busy && (
            <View style={[styles.busy, { backgroundColor: t.warningSoft }]}>
              <Ionicons name="hourglass-outline" size={18} color={t.warning} />
              <Text style={{ flex: 1, color: t.warning }}>{sel.busy}</Text>
            </View>
          )}

          <Button
            title={tr.printPc.print(sideName, students.length)}
            icon="print"
            onPress={confirmPrint}
            busy={printing}
            disabled={!!sel?.busy}
          />
        </>
      )}
    </ScrollView>
  );
}

function SideSwitch({ side, onChange }: { side: Side; onChange: (s: Side) => void }) {
  const t = useTheme();
  const tr = useT();
  return (
    <View style={[styles.segment, { backgroundColor: t.surfaceAlt, borderColor: t.border }]}>
      {(['front', 'back'] as const).map((s) => (
        <Pressable
          key={s}
          onPress={() => onChange(s)}
          accessibilityRole="button"
          accessibilityState={{ selected: side === s }}
          style={[styles.segmentItem, side === s && { backgroundColor: t.accent }]}>
          <Text style={{ color: side === s ? t.onAccent : t.textSoft, fontWeight: '600' }}>
            {s === 'front' ? tr.common.front : tr.common.back_side}
          </Text>
        </Pressable>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  page: { padding: 16, gap: 12, paddingBottom: 40, flexGrow: 1 },
  titleRow: { flexDirection: 'row', alignItems: 'center', gap: 8, marginHorizontal: 4 },
  liveDot: { width: 8, height: 8, borderRadius: 4 },
  caption: { fontSize: 13, lineHeight: 18 },
  section: { fontSize: 17, fontWeight: '700' },
  student: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  nameKh: { fontSize: 17, fontWeight: '700' },
  nameEn: { fontSize: 13, textTransform: 'uppercase' },
  cardHead: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  segment: { flexDirection: 'row', borderRadius: 18, borderWidth: 1, padding: 3 },
  segmentItem: { paddingHorizontal: 14, paddingVertical: 6, borderRadius: 15 },
  card: { width: '100%', aspectRatio: 84.6 / 54, borderRadius: 12, borderWidth: StyleSheet.hairlineWidth },
  busy: { flexDirection: 'row', gap: 10, alignItems: 'center', padding: 12, borderRadius: 12 },
});
