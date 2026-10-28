import Ionicons from '@expo/vector-icons/Ionicons';
import { Image } from 'expo-image';
import * as Haptics from 'expo-haptics';
import { Stack, useLocalSearchParams } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, Alert, Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Badge, Button, Card, EmptyState, Row, StudentPhoto } from '@/components/ui';
import { api, ApiError, cardSource, StudentDetail } from '@/lib/api';
import { useConnection, usePairing } from '@/lib/connection';
import { PhotoSource, takeStudentPhoto } from '@/lib/photo';
import { formatDateTime, khmerText, parseCardDate, useTheme } from '@/lib/theme';

/** Columns already shown in the details; any other column of the imported list is listed under them. */
const KNOWN = new Set(
  ['StudentId', 'NameKh', 'NameEn', 'Gender', 'DateOfBirth', 'Faculty', 'Level', 'Generation', 'ValidUntil', 'QrData'].map((k) =>
    k.toLowerCase(),
  ),
);

export default function StudentScreen() {
  const t = useTheme();
  const pairing = usePairing();
  const { refresh: refreshInfo } = useConnection();
  const { id } = useLocalSearchParams<{ id: string }>();
  const [student, setStudent] = useState<StudentDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [side, setSide] = useState<'front' | 'back'>('front');
  const [upload, setUpload] = useState<{ progress: number } | null>(null);
  const [cardStamp, setCardStamp] = useState(() => String(Date.now()));

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
      Alert.alert('Photo', (e as Error).message);
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
        'Photo saved on the PC',
        result.replaced
          ? 'The card now uses the new photo. The old photo was kept in the photo folder under _replaced.'
          : 'The card now uses this photo.',
      );
    } catch (e) {
      void Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      Alert.alert('Photo not saved', (e as ApiError).message);
    } finally {
      setUpload(null);
    }
  }

  function confirmPhoto(source: PhotoSource) {
    if (!student?.hasPhoto) return void newPhoto(source);
    Alert.alert('Replace the photo?', `${student.nameEn || student.studentId} already has a photo. The old one is kept on the PC.`, [
      { text: 'Cancel', style: 'cancel' },
      { text: 'Replace', onPress: () => void newPhoto(source) },
    ]);
  }

  if (error && !student) {
    return (
      <>
        <Stack.Screen options={{ title: id }} />
        <EmptyState icon="alert-circle-outline" title="Cannot open this student" text={error} action={<Button title="Try again" onPress={load} />} />
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
              {!student.hasPhoto && <Badge label="No photo" tone="warning" icon="camera-outline" />}
              {student.printedAt ? <Badge label="Printed" tone="success" icon="checkmark" /> : <Badge label="Not printed" />}
              {student.rfidUid && <Badge label="RFID" tone="accent" icon="radio-outline" />}
              {expired && <Badge label="Expired" tone="danger" icon="time-outline" />}
            </View>
          </View>
        </Card>

        <View style={styles.actions}>
          <Button
            title={student.hasPhoto ? 'Retake photo' : 'Take photo'}
            icon="camera"
            onPress={() => confirmPhoto('camera')}
            busy={!!upload}
            style={{ flex: 1 }}
          />
          <Button title="Gallery" icon="images-outline" kind="secondary" onPress={() => confirmPhoto('library')} disabled={!!upload} />
        </View>
        <Text style={[styles.tip, { color: t.textMuted }]}>
          Plain light wall behind, face in the middle, eyes about a third from the top. Card Studio saves it as {student.studentId}.jpg in its
          photo folder.
        </Text>

        {/* ---- Card preview, drawn by Card Studio with the design in use ---- */}
        <Card style={{ gap: 12 }}>
          <View style={styles.cardHead}>
            <Text style={[styles.section, { color: t.text }]}>Card</Text>
            <View style={[styles.segment, { backgroundColor: t.surfaceAlt, borderColor: t.border }]}>
              {(['front', 'back'] as const).map((s) => (
                <Pressable
                  key={s}
                  onPress={() => setSide(s)}
                  accessibilityRole="button"
                  accessibilityState={{ selected: side === s }}
                  style={[styles.segmentItem, side === s && { backgroundColor: t.accent }]}>
                  <Text style={{ color: side === s ? t.onAccent : t.textSoft, fontWeight: '600' }}>{s === 'front' ? 'Front' : 'Back'}</Text>
                </Pressable>
              ))}
            </View>
          </View>
          <Image
            source={cardSource(pairing, student.studentId, side, cardStamp)}
            style={[styles.card, { borderColor: t.border, backgroundColor: t.surfaceAlt }]}
            contentFit="contain"
            transition={150}
            accessibilityLabel={`${side} of the card`}
          />
        </Card>

        {/* ---- Status ---- */}
        <Card>
          <Text style={[styles.section, { color: t.text, marginBottom: 4 }]}>Status</Text>
          <StatusLine
            ok={!!student.printedAt}
            icon="print-outline"
            text={student.printedAt ? `Printed ${formatDateTime(student.printedAt)}` : 'Not printed yet'}
          />
          <StatusLine
            ok={!!student.rfidUid}
            icon="radio-outline"
            text={student.rfidUid ? `RFID card ${student.rfidUid}` : 'No RFID card saved'}
          />
          {valid && (
            <StatusLine ok={!expired} icon="calendar-outline" text={`${expired ? 'Expired' : 'Valid until'} ${student.validUntil}`} />
          )}
        </Card>

        {/* ---- Details ---- */}
        <Card>
          <Text style={[styles.section, { color: t.text, marginBottom: 4 }]}>Details</Text>
          <Row label="Name (Khmer)" value={student.nameKh} />
          <Row label="Name (Latin)" value={student.nameEn} />
          <Row label="Gender" value={student.gender} />
          <Row label="Date of birth" value={student.dateOfBirthKh ? `${student.dateOfBirth}  ·  ${student.dateOfBirthKh}` : student.dateOfBirth} />
          <Row label="Faculty" value={student.faculty} />
          <Row label="Level" value={student.level} />
          <Row label="Generation" value={student.generation} />
          <Row label="Valid until" value={student.validUntil} />
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
