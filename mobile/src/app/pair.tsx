import { router, useLocalSearchParams } from 'expo-router';
import { useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { Button } from '@/components/ui';
import { ApiError, parsePairingLink } from '@/lib/api';
import { useConnection } from '@/lib/connection';
import { useTheme } from '@/lib/theme';

/**
 * Opened by the link npiccard://pair?h=…&p=…&t=…&n=… — e.g. when the PC's QR code is scanned with the
 * phone's own camera app instead of inside Card Studio.
 */
export default function PairLinkScreen() {
  const t = useTheme();
  const { pair } = useConnection();
  const params = useLocalSearchParams<{ h?: string; p?: string; t?: string; n?: string }>();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const q = (k: string, v?: string) => `${k}=${encodeURIComponent(v ?? '')}`;
    const link = `npiccard://pair?${q('h', params.h)}&${q('p', params.p)}&${q('t', params.t)}&${q('n', params.n)}`;
    const p = parsePairingLink(link);
    if (!p) {
      setError('This link is not a complete Card Studio pairing code.');
      return;
    }
    pair(p)
      .then(() => router.replace('/'))
      .catch((e: ApiError) => setError(e.message));
  }, [params.h, params.p, params.t, params.n, pair]);

  return (
    <View style={[styles.page, { backgroundColor: t.background }]}>
      {error ? (
        <>
          <Text style={{ color: t.danger, fontSize: 16, textAlign: 'center', lineHeight: 22 }}>{error}</Text>
          <Button title="Back" kind="secondary" onPress={() => router.replace('/')} />
        </>
      ) : (
        <>
          <ActivityIndicator color={t.accent} />
          <Text style={{ color: t.text, fontSize: 16 }}>Connecting to {params.n || 'the PC'}…</Text>
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  page: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 16, padding: 24 },
});
