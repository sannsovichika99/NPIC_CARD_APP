import Ionicons from '@expo/vector-icons/Ionicons';
import * as Clipboard from 'expo-clipboard';
import { Image } from 'expo-image';
import { router } from 'expo-router';
import { useState } from 'react';
import { KeyboardAvoidingView, Platform, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { Button, Card, SettingsButton } from '@/components/ui';
import { ApiError, DEFAULT_PORT, manualPairing, Pairing, parsePairingLink } from '@/lib/api';
import { useConnection } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { useTheme } from '@/lib/theme';

/** First screen: pair this phone with Card Studio on a PC (scan its QR code, paste its link, or type it). */
export default function ConnectScreen() {
  const t = useTheme();
  const tr = useT();
  const { pair } = useConnection();
  const [manual, setManual] = useState(false);
  const [address, setAddress] = useState('');
  const [port, setPort] = useState(String(DEFAULT_PORT));
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function tryPair(p: Pairing | null, what: string) {
    if (!p) {
      setError(what);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await pair(p); // the router then shows the students
    } catch (e) {
      setError((e as ApiError).message);
    } finally {
      setBusy(false);
    }
  }

  async function pasteLink() {
    const text = await Clipboard.getStringAsync();
    await tryPair(parsePairingLink(text), tr.connect.noLinkInClipboard);
  }

  const input = [styles.input, { backgroundColor: t.surface, borderColor: t.border, color: t.text }];

  return (
    <SafeAreaView style={{ flex: 1, backgroundColor: t.background }}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.page} keyboardShouldPersistTaps="handled">
          <SettingsButton onPress={() => router.push('/settings')} style={styles.settings} />
          <Image source={require('../../assets/icon.png')} style={styles.logo} contentFit="contain" />
          <Text style={[styles.title, { color: t.text }]}>Card Studio</Text>
          <Text style={[styles.subtitle, { color: t.textSoft }]}>
            {tr.connect.subtitle}
          </Text>

          <Card style={{ gap: 14 }}>
            <Step n={1} text={tr.connect.step1} />
            <Step n={2} text={tr.connect.step2} />
            <Step n={3} text={tr.connect.step3} />
            <Button title={tr.connect.scanQr} icon="qr-code-outline" onPress={() => router.push('/scan-pair')} busy={busy} />
            <Button title={tr.connect.pasteLink} icon="clipboard-outline" kind="secondary" onPress={pasteLink} disabled={busy} />
          </Card>

          {error && (
            <View style={[styles.error, { backgroundColor: t.dangerSoft }]}>
              <Ionicons name="alert-circle-outline" size={20} color={t.danger} />
              <Text style={{ color: t.danger, flex: 1, lineHeight: 20 }}>{error}</Text>
            </View>
          )}

          {!manual ? (
            <Button title={tr.connect.typeAddress} kind="ghost" onPress={() => setManual(true)} />
          ) : (
            <Card style={{ gap: 10 }}>
              <Text style={[styles.label, { color: t.textSoft }]}>{tr.connect.address}</Text>
              <TextInput
                style={input}
                value={address}
                onChangeText={setAddress}
                placeholder="192.168.1.20"
                placeholderTextColor={t.textMuted}
                keyboardType="numbers-and-punctuation"
                autoCapitalize="none"
                autoCorrect={false}
              />
              <Text style={[styles.label, { color: t.textSoft }]}>{tr.connect.port}</Text>
              <TextInput style={input} value={port} onChangeText={setPort} keyboardType="number-pad" />
              <Text style={[styles.label, { color: t.textSoft }]}>{tr.connect.code}</Text>
              <TextInput
                style={[input, { fontFamily: 'monospace' }]}
                value={code}
                onChangeText={setCode}
                autoCapitalize="characters"
                autoCorrect={false}
                secureTextEntry
              />
              <Button
                title={tr.connect.connect}
                icon="link-outline"
                busy={busy}
                onPress={() =>
                  tryPair(manualPairing(address, port, code), tr.connect.fillIn)
                }
              />
            </Card>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function Step({ n, text }: { n: number; text: string }) {
  const t = useTheme();
  return (
    <View style={{ flexDirection: 'row', gap: 12, alignItems: 'flex-start' }}>
      <View style={[styles.stepNo, { backgroundColor: t.accentSoft }]}>
        <Text style={{ color: t.accent, fontWeight: '700' }}>{n}</Text>
      </View>
      <Text style={{ color: t.text, flex: 1, fontSize: 15, lineHeight: 22 }}>{text}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  page: { padding: 20, gap: 16, paddingBottom: 40 },
  settings: { position: 'absolute', top: 4, right: 4, zIndex: 1 },
  logo: { width: 96, height: 96, alignSelf: 'center', borderRadius: 22, marginTop: 12 },
  title: { fontSize: 28, fontWeight: '700', textAlign: 'center' },
  subtitle: { fontSize: 15, textAlign: 'center', lineHeight: 22, marginBottom: 4 },
  stepNo: { width: 28, height: 28, borderRadius: 14, alignItems: 'center', justifyContent: 'center' },
  error: { flexDirection: 'row', gap: 10, padding: 12, borderRadius: 12, alignItems: 'flex-start' },
  label: { fontSize: 13 },
  input: { borderWidth: 1, borderRadius: 12, paddingHorizontal: 14, paddingVertical: 12, fontSize: 16 },
});
