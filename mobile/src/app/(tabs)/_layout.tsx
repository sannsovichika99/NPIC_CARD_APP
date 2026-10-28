import Ionicons from '@expo/vector-icons/Ionicons';
import { Tabs } from 'expo-router';
import { useTheme } from '@/lib/theme';

export default function TabsLayout() {
  const t = useTheme();
  return (
    <Tabs
      screenOptions={{
        tabBarActiveTintColor: t.accent,
        tabBarInactiveTintColor: t.textMuted,
        tabBarStyle: { backgroundColor: t.surface, borderTopColor: t.border },
        headerStyle: { backgroundColor: t.surface },
        headerTitleStyle: { color: t.text },
        sceneStyle: { backgroundColor: t.background },
      }}>
      <Tabs.Screen
        name="index"
        options={{
          title: 'Students',
          tabBarIcon: ({ color, size }) => <Ionicons name="people-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="scan"
        options={{
          title: 'Check Card',
          tabBarIcon: ({ color, size }) => <Ionicons name="scan-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="history"
        options={{
          title: 'Printed',
          tabBarIcon: ({ color, size }) => <Ionicons name="print-outline" color={color as string} size={size} />,
        }}
      />
      <Tabs.Screen
        name="pc"
        options={{
          title: 'PC',
          tabBarIcon: ({ color, size }) => <Ionicons name="desktop-outline" color={color as string} size={size} />,
        }}
      />
    </Tabs>
  );
}
