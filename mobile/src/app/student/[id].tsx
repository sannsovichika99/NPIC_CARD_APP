import Ionicons from '@expo/vector-icons/Ionicons';
import { Image } from 'expo-image';
import * as Haptics from 'expo-haptics';
import { Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Badge, Button, Card, EmptyState, Row, StudentPhoto } from '@/components/ui';
import { api, ApiError, cardSource, StudentDetail } from '@/lib/api';
import { useConnection, usePairing } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { CameraDeniedError, PhotoSource, takeStudentPhoto } from '@/lib/photo';
import { formatDateTime, khmerText, parseCardDate, useTheme } from '@/lib/theme';

/** Columns already shown in the details; any other column of the imported list is listed under them. */
const KNOWN = new Set(
  ['StudentId', 'NameKh', 'NameEn', 'Gender', 'DateOfBirth', 'Faculty', 'Level', 'Generation', 'ValidUntil', 'QrData'].map((k) =>
    k.toLowerCase(),
  ),
);

export default function StudentScreen() {
  const t = useTheme();
  const tr = useT();
  const pairing = usePairing();
  const { refresh: refreshInfo } = useConnection();
  const { id } = useLocalSearchParams<{ id: string }>();
  const [student, setStudent] = useState<StudentDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [side, setSide] = useState<'front' | 'back'>('front');
  const [upload, setUpload] = useState<{ progress: number } | null>(null);
  const [cardStamp, setCardStamp] = useState(() => String(Date.now()));
  const [printing, setPrinting] = useState(false);

  const load = useCallback(async () => {
    try {
      setStudent(await api.student(pairing, id));
      setError(null);
    } catch (e) {
      setError((e as ApiError).message);
    } finally {
      setRefreshing(false);
    }
  }, [pairing, id]);

  useEffect(() => {
    void load();
  }, [load]);

  async function newPhoto(source: PhotoSource) {
    if (!student) return;
    let uri: string | null;
    try {
      uri = await takeStudentPhoto(source);
    } catch (e) {
      Alert.alert(tr.student.photo, e instanceof CameraDeniedError ? tr.camera.denied : (e as Error).message);
      return;
    }
    if (!uri) return;

    setUpload({ progress: 0 });
    try {
      const result = await api.uploadPhoto(pairing, student.studentId, uri, (progress) => setUpload({ progress }));
      setStudent({ ...student, ...result.student });
      setCardStamp(String(Date.now())); // the card now shows the new photo
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      void refreshInfo();
      Alert.alert(
        tr.student.saved,
        result.replaced ? tr.student.savedReplaced : tr.student.savedNew,
      );
    } catch (e) {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      Alert.alert(tr.student.notSaved, (e as ApiError).message);
    } finally {
      setUpload(null);
    }
  }

  function confirmPhoto(source: PhotoSource) {
    if (!student?.hasPhoto) return void newPhoto(source);
    Alert.alert(tr.student.replaceTitle, tr.student.replaceText(student.nameEn || student.studentId), [
      { text: tr.common.cancel, style: 'cancel' },
      { text: tr.student.replace, onPress: () => void newPhoto(source) },
    ]);
  }

  /** Print button: Card Studio selects this student and side and starts Print Selected on the PC. */
  function confirmPrint() {
    if (!student) return;
    const sideName = side === 'front' ? tr.common.front : tr.common.back_side;
    Alert.alert(tr.student.printTitle, tr.student.printText(sideName, student.nameEn || student.studentId), [
      { text: tr.common.cancel, style: 'cancel' },
      { text: tr.student.printConfirm, onPress: () => void sendPrint() },
    ]);
  }

  async function sendPrint() {
    if (!student) return;
    setPrinting(true);
    try {
      await api.print(pairing, student.studentId, side);
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      Alert.alert(tr.student.printSent, tr.student.printSentText);
    } catch (e) {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      Alert.alert(tr.student.notPrintedTitle, (e as ApiError).message);
    } finally {
      setPrinting(false);
    }
  }

  if (error && !student) {
    return (
      <>
        <Stack.Screen options={{ title: id }} />
        <EmptyState icon="alert-circle-outline" title={tr.student.cannotOpen} text={error} action={<Button title={tr.common.tryAgain} onPress={load} />} />
      </>
    );
  }
  if (!student) {
    return (
      <>
        <Stack.Screen options={{ title: id }} />
        <ActivityIndicator color={t.accent} style={{ marginTop: 40 }} />
      </>
    );
  }

  const valid = parseCardDate(student.validUntil);
  const expired = valid ? valid.getTime() < Date.now() : false;
  const extra = Object.entries(student.columns).filter(([k, v]) => v && !KNOWN.has(k.toLowerCase()));

  return (
    <>
      <Stack.Screen options={{ title: student.studentId }} />
      <ScrollView
        contentContainerStyle={styles.page}
        refreshControl={
          <RefreshControl
            refreshing={refreshing}
            onRefresh={() => {
              setRefreshing(true);
              setCardStamp(String(Date.now()));
              void load();
            }}
            tintColor={t.accent}
          />
        }>
        {/* ---- Photo and name ---- */}
        <Card style={styles.head}>
          <View>
            <StudentPhoto student={student} width={112} />
            {upload && (
              <View style={styles.uploading}>
                <ActivityIndicator color="#fff" />
                <Text style={{ color: '#fff', fontWeight: '600' }}>{Math.round(upload.progress * 100)}%</Text>
              </View>
            )}
          </View>
          <View style={{ flex: 1, gap: 4 }}>
            <Text style={[styles.nameKh, khmerText, { color: t.text }]}>{student.nameKh}</Text>
            <Text style={[styles.nameEn, { color: t.textSoft }]}>{student.nameEn}</Text>
            <Text style={{ color: t.accent, fontWeight: '700', marginTop: 2 }}>{student.studentId}</Text>
            <View style={styles.badges}>
              {!student.hasPhoto && <Badge label={tr.common.noPhoto} tone="warning" icon="camera-outline" />}
              {student.printedAt ? <Badge label={tr.common.printed} tone="success" icon="checkmark" /> : <Badge label={tr.common.notPrinted} />}
              {student.rfidUid && <Badge label={tr.common.rfid} tone="accent" icon="radio-outline" />}
              {expired && <Badge label={tr.common.expired} tone="danger" icon="time-outline" />}
            </View>
          </View>
        </Card>

        <View style={styles.actions}>
          <Button
            title={student.hasPhoto ? tr.student.retake : tr.student.take}
            icon="camera"
            onPress={() => confirmPhoto('camera')}
            busy={!!upload}
            style={{ flex: 1 }}
          />
          <Button title={tr.student.gallery} icon="images-outline" kind="secondary" onPress={() => confirmPhoto('library')} disabled={!!upload} />
        </View>
        <Text style={[styles.tip, { color: t.textMuted }]}>
          {tr.student.tip(student.studentId)}
        </Text>

        {/* ---- Card preview, drawn by Card Studio with the design in use ---- */}
        <Card style={{ gap: 12 }}>
          <View style={styles.cardHead}>
            <Text style={[styles.section, { color: t.text }]}>{tr.student.card}</Text>
            <View style={[styles.segment, { backgroundColor: t.surfaceAlt, borderColor: t.border }]}>
              {(['front', 'back'] as const).map((s) => (
                <Pressable
                  key={s}
                  onPress={() => setSide(s)}
                  accessibilityRole="button"
                  accessibilityState={{ selected: side === s }}
                  style={[styles.segmentItem, side === s && { backgroundColor: t.accent }]}>
                  <Text style={{ color: side === s ? t.onAccent : t.textSoft, fontWeight: '600' }}>{s === 'front' ? tr.common.front : tr.common.back_side}</Text>
                </Pressable>
              ))}
            </View>
          </View>
          <Image
            source={cardSource(pairing, student.studentId, side, cardStamp)}
            style={[styles.card, { borderColor: t.border, backgroundColor: t.surfaceAlt }]}
            contentFit="contain"
            transition={150}
            accessibilityLabel={tr.student.sideOfCard(side === 'front' ? tr.common.front : tr.common.back_side)}
          />
          <Button
            title={tr.student.print(side === 'front' ? tr.common.front : tr.common.back_side)}
            icon="print-outline"
            onPress={confirmPrint}
            busy={printing}
            disabled={!!upload}
          />
        </Card>

        {/* ---- Status ---- */}
        <Card>
          <Text style={[styles.section, { color: t.text, marginBottom: 4 }]}>{tr.student.status}</Text>
          <StatusLine
            ok={!!student.printedAt}
            icon="print-outline"
            text={student.printedAt ? tr.common.printedOn(formatDateTime(student.printedAt, tr.months)) : tr.student.notPrintedYet}
          />
          <StatusLine
            ok={!!student.rfidUid}
            icon="radio-outline"
            text={student.rfidUid ? tr.common.rfidCard(student.rfidUid) : tr.student.noRfidSaved}
          />
          {valid && (
            <StatusLine ok={!expired} icon="calendar-outline" text={expired ? tr.common.expiredOn(student.validUntil) : tr.common.validUntil(student.validUntil)} />
          )}
        </Card>

        {/* ---- Details ---- */}
        <Card>
          <Text style={[styles.section, { color: t.text, marginBottom: 4 }]}>{tr.student.details}</Text>
          <Row label={tr.student.nameKh} value={student.nameKh} />
          <Row label={tr.student.nameEn} value={student.nameEn} />
          <Row label={tr.student.gender} value={student.gender} />
          <Row label={tr.student.dateOfBirth} value={student.dateOfBirthKh ? `${student.dateOfBirth}  ·  ${student.dateOfBirthKh}` : student.dateOfBirth} />
          <Row label={tr.student.faculty} value={student.faculty} />
          <Row label={tr.student.level} value={student.level} />
          <Row label={tr.student.generation} value={student.generation} />
          <Row label={tr.student.validUntil} value={student.validUntil} />
          {extra.map(([k, v]) => (
            <Row key={k} label={k} value={v} />
          ))}
        </Card>
      </ScrollView>
    </>
  );
}

function StatusLine({ ok, icon, text }: { ok: boolean; icon: React.ComponentProps<typeof Ionicons>['name']; text: string }) {
  const t = useTheme();
  return (
    <View style={styles.status}>
      <Ionicons name={icon} size={18} color={ok ? t.success : t.textMuted} />
      <Text style={{ color: ok ? t.text : t.textSoft, flex: 1, fontSize: 15 }}>{text}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  page: { padding: 16, gap: 12, paddingBottom: 40 },
  head: { flexDirection: 'row', gap: 16, alignItems: 'center' },
  nameKh: { fontSize: 21, fontWeight: '700' },
  nameEn: { fontSize: 15, textTransform: 'uppercase' },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6, marginTop: 6 },
  uploading: {
    position: 'absolute', top: 0, left: 0, right: 0, bottom: 0,
    backgroundColor: 'rgba(0,0,0,0.5)',
    borderRadius: 10,
    alignItems: 'center',
    justifyContent: 'center',
    gap: 6,
  },
  actions: { flexDirection: 'row', gap: 10 },
  tip: { fontSize: 13, lineHeight: 18, marginHorizontal: 4 },
  cardHead: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' },
  section: { fontSize: 17, fontWeight: '700' },
  segment: { flexDirection: 'row', borderRadius: 18, borderWidth: 1, padding: 3 },
  segmentItem: { paddingHorizontal: 14, paddingVertical: 6, borderRadius: 15 },
  card: { width: '100%', aspectRatio: 84.6 / 54, borderRadius: 12, borderWidth: StyleSheet.hairlineWidth },
  status: { flexDirection: 'row', gap: 10, alignItems: 'center', paddingVertical: 8 },
});
