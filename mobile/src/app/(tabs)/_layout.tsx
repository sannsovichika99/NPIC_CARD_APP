import * as Haptics from 'expo-haptics';
import { router, Tabs } from 'expo-router';
import { Platform } from 'react-native';
import { TabIcon, useReduceMotion } from '@/components/TabIcon';
import { IconName, SettingsButton } from '@/components/ui';
import { useT } from '@/lib/i18n';
import { useTheme } from '@/lib/theme';

/** Bottom menu icons: outline when unselected, filled when selected. */
const ICONS: Record<string, [IconName, IconName]> = {
  index: ['home-outline', 'home'],
  students: ['people-outline', 'people'],
  scan: ['scan-outline', 'scan'],
  history: ['print-outline', 'print'],
  pc: ['desktop-outline', 'desktop'],
};

export default function TabsLayout() {
  const t = useTheme();
  const tr = useT();
  const reduceMotion = useReduceMotion();
  return (
    <Tabs
      screenOptions={({ route, navigation }) => ({
        tabBarActiveTintColor: t.accent,
        tabBarInactiveTintColor: t.textMuted,
        tabBarStyle: { backgroundColor: t.surface, borderTopColor: t.border },
        tabBarIcon: ({ focused, color, size }) => {
          const [icon, activeIcon] = ICONS[route.name] ?? ['ellipse-outline', 'ellipse'];
          // The selected tab of this menu (not isFocused(): that also turns false under Settings and would replay the pop).
          const state = navigation.getState();
          const selected = state.routes[state.index]?.key === route.key;
          return (
            <TabIcon
              icon={icon}
              activeIcon={activeIcon}
              focused={focused}
              selected={selected}
              color={color}
              size={size}
            />
          );
        },
        // Screens slide a little toward the chosen tab.
        animation: reduceMotion ? 'none' : 'shift',
        headerStyle: { backgroundColor: t.surface },
        headerTitleStyle: { color: t.text },
        sceneStyle: { backgroundColor: t.background },
        headerRight: () => <SettingsButton onPress={() => router.push('/settings')} style={{ marginRight: 8 }} />,
      })}
      screenListeners={{
        tabPress: () => {
          if (Platform.OS !== 'web') void Haptics.selectionAsync();
        },
      }}>
      <Tabs.Screen name="index" options={{ title: tr.nav.home }} />
      <Tabs.Screen name="students" options={{ title: tr.nav.students }} />
      <Tabs.Screen name="scan" options={{ title: tr.nav.checkCard }} />
      <Tabs.Screen name="history" options={{ title: tr.nav.printed }} />
      <Tabs.Screen name="pc" options={{ title: tr.nav.pc }} />
    </Tabs>
  );
}
