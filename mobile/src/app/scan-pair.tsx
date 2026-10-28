import { useState } from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { QrScanner } from '@/components/QrScanner';
import { Button } from '@/components/ui';
import { ApiError, parsePairingLink } from '@/lib/api';
import { useConnection } from '@/lib/connection';
import { useTheme } from '@/lib/theme';

/** Scans the QR code on the PC's Mobile App page and pairs with it. */
export default function ScanPairScreen() {
  const t = useTheme();
  const { pair } = useConnection();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onScan(data: string) {
    const p = parsePairingLink(data);
    if (!p) {
      setError('This is not a Card Studio pairing code. Scan the QR code on the PC: Card Studio → Mobile App….');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await pair(p); // paired: the root layout's guard swaps this screen for the student list
    } catch (e) {
      setError((e as ApiError).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <View style={{ flex: 1, backgroundColor: '#000' }}>
      <QrScanner onScan={onScan} paused={busy} hint="Point at the QR code on the PC screen" />
      {(busy || error) && (
        <View style={[styles.sheet, { backgroundColor: t.surface }]}>
          {busy ? (
            <View style={styles.busy}>
              <ActivityIndicator color={t.accent} />
              <Text style={{ color: t.text, fontSize: 16 }}>Connecting to the PC…</Text>
            </View>
          ) : (
            <>
              <Text style={{ color: t.danger, fontSize: 15, lineHeight: 21 }}>{error}</Text>
              <Button title="Scan again" kind="secondary" icon="refresh-outline" onPress={() => setError(null)} />
            </>
          )}
        </View>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  sheet: { position: 'absolute', left: 12, right: 12, bottom: 24, borderRadius: 18, padding: 16, gap: 12 },
  busy: { flexDirection: 'row', gap: 12, alignItems: 'center' },
});
