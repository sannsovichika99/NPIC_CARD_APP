import Ionicons from '@expo/vector-icons/Ionicons';
import Constants from 'expo-constants';
import { useState } from 'react';
import { Alert, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Button, Card, ConnectionBanner, Row } from '@/components/ui';
import { useConnection, usePairing } from '@/lib/connection';
import { useTheme } from '@/lib/theme';

/** The paired PC: what Card Studio has loaded, and Forget this PC. */
export default function PcScreen() {
  const t = useTheme();
  const pairing = usePairing();
  const { info, error, refresh, forget } = useConnection();
  const [refreshing, setRefreshing] = useState(false);

  const percent = (n: number) => (info && info.students > 0 ? ` (${Math.round((n / info.students) * 100)}%)` : '');

  return (
    <ScrollView
      contentContainerStyle={styles.page}
      refreshControl={
        <RefreshControl
          refreshing={refreshing}
          onRefresh={async () => {
            setRefreshing(true);
            await refresh();
            setRefreshing(false);
          }}
          tintColor={t.accent}
        />
      }>
      <ConnectionBanner />
      <Card style={styles.pc}>
        <View style={[styles.pcIcon, { backgroundColor: t.accentSoft }]}>
          <Ionicons name="desktop-outline" size={28} color={t.accent} />
        </View>
        <View style={{ flex: 1 }}>
          <Text style={{ color: t.text, fontSize: 18, fontWeight: '700' }}>{info?.computer || pairing.pcName}</Text>
          <Text style={{ color: error ? t.warning : t.success }}>
            {error ? 'Not answering' : `Connected · ${pairing.host}:${pairing.port}`}
          </Text>
        </View>
      </Card>

      {info && (
        <Card>
          <Text style={[styles.section, { color: t.text }]}>Card Studio</Text>
          <Row label="Version" value={info.version} />
          <Row label="Student list" value={info.source || 'No list loaded'} />
          <Row label="Students" value={String(info.students)} />
          <Row label="With photo" value={`${info.withPhoto}${percent(info.withPhoto)}`} />
          <Row label="Printed" value={`${info.printed}${percent(info.printed)}`} />
          <Row label="RFID cards" value={`${info.withCard}${percent(info.withCard)}`} />
          <Row label="Card design" value={info.template} />
          {!info.photoFolderOk && (
            <Text style={{ color: t.warning, marginTop: 8 }}>
              The photo folder on the PC is missing: photos cannot be saved until it is chosen again (Photo Folder… on the PC).
            </Text>
          )}
        </Card>
      )}

      <Button
        title="Forget this PC"
        icon="log-out-outline"
        kind="danger"
        onPress={() =>
          Alert.alert('Forget this PC?', 'To connect again, scan the QR code on the PC (Card Studio → Mobile App…).', [
            { text: 'Cancel', style: 'cancel' },
            { text: 'Forget', style: 'destructive', onPress: () => void forget() },
          ])
        }
      />
      <Text style={[styles.footer, { color: t.textMuted }]}>Card Studio mobile {Constants.expoConfig?.version ?? ''}</Text>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  page: { padding: 16, gap: 12, paddingBottom: 40 },
  pc: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  pcIcon: { width: 52, height: 52, borderRadius: 14, alignItems: 'center', justifyContent: 'center' },
  section: { fontSize: 17, fontWeight: '700', marginBottom: 4 },
  footer: { textAlign: 'center', fontSize: 12 },
});
