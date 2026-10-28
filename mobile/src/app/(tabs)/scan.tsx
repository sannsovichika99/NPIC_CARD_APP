import Ionicons from '@expo/vector-icons/Ionicons';
import * as Haptics from 'expo-haptics';
import { router, useIsFocused } from 'expo-router';
import { useState } from 'react';
import { ActivityIndicator, KeyboardAvoidingView, Platform, StyleSheet, Text, TextInput, View } from 'react-native';
import { QrScanner } from '@/components/QrScanner';
import { Badge, Button, StudentPhoto } from '@/components/ui';
import { api, ApiError, LookupResult } from '@/lib/api';
import { usePairing } from '@/lib/connection';
import { formatDateTime, khmerText, parseCardDate, useTheme } from '@/lib/theme';

/**
 * Check a printed card: scan the QR code on the card (or type the ID) and see whose card it is, whether it
 * was printed, its RFID number and whether it is still valid.
 */
export default function CheckCardScreen() {
  const t = useTheme();
  const pairing = usePairing();
  const focused = useIsFocused(); // the camera only runs while this tab is shown
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<LookupResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [typed, setTyped] = useState('');

  async function lookup(code: string) {
    if (!code.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const r = await api.lookup(pairing, code.trim());
      setResult(r);
      void Haptics.notificationAsync(r.found ? Haptics.NotificationFeedbackType.Success : Haptics.NotificationFeedbackType.Warning);
    } catch (e) {
      setResult(null);
      setError((e as ApiError).message);
    } finally {
      setBusy(false);
    }
  }

  const showing = busy || !!result || !!error;

  return (
    <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
      <View style={{ flex: 1 }}>
        {focused ? (
          <QrScanner onScan={lookup} paused={showing} hint="Point at the QR code on the student card" />
        ) : (
          <View style={{ flex: 1, backgroundColor: '#000' }} />
        )}

        {!showing && (
          <View style={[styles.typeBar, { backgroundColor: t.surface, borderColor: t.border }]}>
            <TextInput
              value={typed}
              onChangeText={setTyped}
              placeholder="…or type a StudentId"
              placeholderTextColor={t.textMuted}
              autoCapitalize="characters"
              autoCorrect={false}
              returnKeyType="search"
              onSubmitEditing={() => lookup(typed)}
              style={[styles.typeInput, { color: t.text }]}
            />
            <Button title="Check" onPress={() => lookup(typed)} style={{ minHeight: 40 }} />
          </View>
        )}

        {showing && (
          <View style={[styles.sheet, { backgroundColor: t.surface }]}>
            {busy ? (
              <View style={styles.inline}>
                <ActivityIndicator color={t.accent} />
                <Text style={{ color: t.text, fontSize: 16 }}>Checking…</Text>
              </View>
            ) : error ? (
              <Text style={{ color: t.danger, fontSize: 15, lineHeight: 21 }}>{error}</Text>
            ) : result ? (
              <ResultView result={result} />
            ) : null}
            {!busy && (
              <Button
                title="Scan next card"
                icon="scan-outline"
                kind="secondary"
                onPress={() => {
                  setResult(null);
                  setError(null);
                  setTyped('');
                }}
              />
            )}
          </View>
        )}
      </View>
    </KeyboardAvoidingView>
  );
}

function ResultView({ result }: { result: LookupResult }) {
  const t = useTheme();
  const s = result.student;

  if (s) {
    const valid = parseCardDate(s.validUntil);
    const expired = valid ? valid.getTime() < Date.now() : false;
    return (
      <View style={{ gap: 12 }}>
        <View style={styles.inline}>
          <StudentPhoto student={s} width={72} />
          <View style={{ flex: 1, gap: 2 }}>
            <Text style={[styles.name, khmerText, { color: t.text }]}>{s.nameKh}</Text>
            <Text style={{ color: t.textSoft }}>{s.nameEn}</Text>
            <Text style={{ color: t.accent, fontWeight: '700' }}>{s.studentId}</Text>
            <Text style={{ color: t.textSoft }} numberOfLines={1}>
              {[s.faculty, s.level].filter(Boolean).join(' · ')}
            </Text>
          </View>
        </View>
        <View style={styles.badges}>
          {expired ? (
            <Badge label={`Expired ${s.validUntil}`} tone="danger" icon="close-circle-outline" />
          ) : valid ? (
            <Badge label={`Valid until ${s.validUntil}`} tone="success" icon="checkmark-circle-outline" />
          ) : null}
          {s.printedAt ? (
            <Badge label={`Printed ${formatDateTime(s.printedAt)}`} tone="success" icon="print-outline" />
          ) : (
            <Badge label="No print in the history" tone="warning" icon="print-outline" />
          )}
          {s.rfidUid ? <Badge label={`RFID ${s.rfidUid}`} tone="accent" icon="radio-outline" /> : <Badge label="No RFID card" />}
        </View>
        <Button
          title="Open student"
          icon="person-outline"
          onPress={() => router.push({ pathname: '/student/[id]', params: { id: s.studentId } })}
        />
      </View>
    );
  }

  const h = result.history;
  return (
    <View style={{ gap: 8 }}>
      <View style={styles.inline}>
        <Ionicons name={h ? 'information-circle-outline' : 'help-circle-outline'} size={28} color={h ? t.warning : t.danger} />
        <Text style={{ color: t.text, fontSize: 17, fontWeight: '700', flex: 1 }}>
          {h ? 'Not in the list loaded on the PC' : 'Unknown card'}
        </Text>
      </View>
      {h ? (
        <Text style={{ color: t.textSoft, fontSize: 15, lineHeight: 22 }}>
          <Text style={[khmerText, { color: t.text }]}>{h.nameKh}</Text> ({h.nameEn}, {h.studentId}) was printed{' '}
          {formatDateTime(h.printedAt)}
          {h.rfidUid ? ` and has RFID card ${h.rfidUid}` : ''}. The student is no longer in the loaded list (for example after the RFID
          card was saved).
        </Text>
      ) : (
        <Text style={{ color: t.textSoft, fontSize: 15, lineHeight: 22 }}>
          “{result.code}” does not match any student loaded in Card Studio or in its print history.
        </Text>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  typeBar: {
    position: 'absolute',
    left: 12,
    right: 12,
    bottom: 16,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    borderRadius: 26,
    borderWidth: 1,
    paddingLeft: 16,
    padding: 4,
  },
  typeInput: { flex: 1, fontSize: 16, paddingVertical: 10 },
  sheet: { position: 'absolute', left: 12, right: 12, bottom: 16, borderRadius: 20, padding: 16, gap: 14 },
  inline: { flexDirection: 'row', gap: 12, alignItems: 'center' },
  name: { fontSize: 19, fontWeight: '700' },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
});
