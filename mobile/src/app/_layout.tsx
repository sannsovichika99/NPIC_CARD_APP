import { Stack } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { ActivityIndicator, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { ConnectionProvider, useConnection } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { SettingsProvider, useSettings } from '@/lib/settings';
import { useTheme } from '@/lib/theme';

export default function RootLayout() {
  return (
    <SafeAreaProvider>
      <SettingsProvider>
        <ConnectionProvider>
          <RootStack />
        </ConnectionProvider>
      </SettingsProvider>
    </SafeAreaProvider>
  );
}

function RootStack() {
  const { loading, pairing } = useConnection();
  const { loading: settingsLoading } = useSettings();
  const t = useTheme();
  const tr = useT();

  if (loading || settingsLoading) {
    return (
      <View style={{ flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: t.background }}>
        <ActivityIndicator color={t.accent} />
      </View>
    );
  }

  const paired = !!pairing;
  return (
    <>
      <StatusBar style={t.dark ? 'light' : 'dark'} />
      <Stack
        screenOptions={{
          headerStyle: { backgroundColor: t.surface },
          headerTintColor: t.accent,
          headerTitleStyle: { color: t.text },
          contentStyle: { backgroundColor: t.background },
        }}>
        {/* Paired with a PC: the app itself. */}
        <Stack.Protected guard={paired}>
          <Stack.Screen name="(tabs)" options={{ headerShown: false }} />
          <Stack.Screen name="student/[id]" options={{ title: tr.nav.student }} />
        </Stack.Protected>

        {/* Not paired yet: connect to Card Studio on the PC. */}
        <Stack.Protected guard={!paired}>
          <Stack.Screen name="connect" options={{ headerShown: false }} />
          <Stack.Screen name="scan-pair" options={{ title: tr.nav.scanPcQr, presentation: 'modal' }} />
        </Stack.Protected>

        {/* npiccard://pair?… opened from the phone's own camera app: works paired or not (pairs with another PC). */}
        <Stack.Screen name="pair" options={{ title: tr.nav.connecting, presentation: 'modal' }} />

        {/* Language, dark mode and theme color: reachable paired (tab headers) or not (Connect screen). */}
        <Stack.Screen name="settings" options={{ title: tr.common.settings }} />
      </Stack>
    </>
  );
}
