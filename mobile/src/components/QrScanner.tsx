import Ionicons from '@expo/vector-icons/Ionicons';
import { BarcodeScanningResult, CameraView, useCameraPermissions } from 'expo-camera';
import * as Haptics from 'expo-haptics';
import { useRef } from 'react';
import { Linking, StyleSheet, Text, View } from 'react-native';
import { Button, EmptyState } from './ui';
import { useT } from '@/lib/i18n';
import { useTheme } from '@/lib/theme';

/**
 * Camera that reads QR codes. `onScan` gets each code once; while `paused` (e.g. a result is shown)
 * nothing new is reported, so one card is not looked up ten times.
 */
export function QrScanner({ onScan, paused, hint }: { onScan: (data: string) => void; paused?: boolean; hint: string }) {
  const t = useTheme();
  const tr = useT();
  const [permission, requestPermission] = useCameraPermissions();
  const last = useRef<{ data: string; at: number }>({ data: '', at: 0 });

  if (!permission) return <View style={{ flex: 1, backgroundColor: '#000' }} />;

  if (!permission.granted) {
    return (
      <EmptyState
        icon="camera-outline"
        title={tr.camera.needed}
        text={tr.camera.neededText}
        action={
          permission.canAskAgain ? (
            <Button title={tr.camera.allow} icon="camera-outline" onPress={requestPermission} />
          ) : (
            <Button title={tr.camera.openSettings} icon="settings-outline" onPress={() => Linking.openSettings()} />
          )
        }
      />
    );
  }

  function handle(result: BarcodeScanningResult) {
    if (paused || !result.data) return;
    const now = Date.now();
    // The same code stays in view for many frames: report it again only after 3 s.
    if (result.data === last.current.data && now - last.current.at < 3000) return;
    last.current = { data: result.data, at: now };
    void Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
    onScan(result.data);
  }

  return (
    <View style={{ flex: 1, backgroundColor: '#000' }}>
      <CameraView
        style={StyleSheet.absoluteFill}
        facing="back"
        barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
        onBarcodeScanned={paused ? undefined : handle}
      />
      <View pointerEvents="none" style={styles.overlay}>
        <View style={[styles.frame, { borderColor: paused ? 'rgba(255,255,255,0.35)' : t.onAccent }]} />
        <View style={styles.hint}>
          <Ionicons name="qr-code-outline" size={16} color="#fff" />
          <Text style={styles.hintText}>{hint}</Text>
        </View>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  overlay: { position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, alignItems: 'center', justifyContent: 'center', gap: 24 },
  frame: { width: 240, height: 240, borderWidth: 3, borderRadius: 24 },
  hint: {
    flexDirection: 'row',
    gap: 8,
    alignItems: 'center',
    backgroundColor: 'rgba(0,0,0,0.55)',
    paddingHorizontal: 14,
    paddingVertical: 8,
    borderRadius: 18,
    maxWidth: '85%',
  },
  hintText: { color: '#fff', fontSize: 14 },
});
