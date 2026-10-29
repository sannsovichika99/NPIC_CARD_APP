import Ionicons from '@expo/vector-icons/Ionicons';
import { router, Tabs } from 'expo-router';
import { SettingsButton } from '@/components/ui';
import { useT } from '@/lib/i18n';
import { useTheme } from '@/lib/theme';

export default function TabsLayout() {
  const t = useTheme();
  const tr = useT();
  return (
    <Tabs
      screenOptions={{
        tabBarActiveTintColor: t.accent,
        tabBarInactiveTintColor: t.textMuted,
        tabBarStyle: { backgroundColor: t.surface, borderTopColor: t.border },
        headerStyle: { backgroundColor: t.surface },
        headerTitleStyle: { color: t.text },
        sceneStyle: { backgroundColor: t.background },
        headerRight: () => <SettingsButton onPress={() => router.push('/settings')} style={{ marginRight: 8 }} />,
      }}>
      <Tabs.Screen
        name="index"
        options={{
          title: tr.nav.home,
          tabBarIcon: ({ color, size }) => <Ionicons name="home-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="students"
        options={{
          title: tr.nav.students,
          tabBarIcon: ({ color, size }) => <Ionicons name="people-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          title: tr.nav.checkCard,
          tabBarIcon: ({ color, size }) => <Ionicons name="scan-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="history"
        options={{
          title: tr.nav.printed,
          tabBarIcon: ({ color, size }) => <Ionicons name="print-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="pc"
        options={{
          title: tr.nav.pc,
          tabBarIcon: ({ color, size }) => <Ionicons name="desktop-outline" color={color as string} size={size} />,
        }}
      />
    </Tabs>
  );
}
