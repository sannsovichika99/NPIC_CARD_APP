/**
 * Bottom menu icon: when its tab becomes selected, a soft accent pill grows behind it and the icon pops
 * from outline to filled. Uses React Native's Animated (native driver), so no extra native module is needed.
 *
 * The tab bar always renders each icon twice (a "focused" and an "unfocused" copy) and only swaps their
 * opacity, so the `focused` flag it passes never changes on a mounted icon. The real selection comes in
 * through `selected` (navigation.isFocused() in the tab layout).
 */
import Ionicons from '@expo/vector-icons/Ionicons';
import { useEffect, useState } from 'react';
import { AccessibilityInfo, Animated, ColorValue, StyleSheet, View } from 'react-native';
import type { IconName } from '@/components/ui';
import { useTheme } from '@/lib/theme';

const PILL_WIDTH = 50;
const PILL_HEIGHT = 28;

type Props = {
  /** Icon of the unselected tab (outline). */
  icon: IconName;
  /** Icon of the selected tab (filled). */
  activeIcon: IconName;
  /** The copy the tab bar shows while this tab is selected. */
  focused: boolean;
  /** This tab is the selected one right now. */
  selected: boolean;
  color: ColorValue;
  size: number;
};

export function TabIcon({ icon, activeIcon, focused, selected, color, size }: Props) {
  const reduceMotion = useReduceMotion();
  // The unselected copy never shows while its tab is selected: no animation needed there.
  if (!focused) return <Ionicons name={icon} color={color as string} size={size} />;
  return <ActiveIcon name={activeIcon} selected={selected} color={color} size={size} reduceMotion={reduceMotion} />;
}

function ActiveIcon({
  name,
  selected,
  color,
  size,
  reduceMotion,
}: {
  name: IconName;
  selected: boolean;
  color: ColorValue;
  size: number;
  reduceMotion: boolean;
}) {
  const t = useTheme();
  const [progress] = useState(() => new Animated.Value(selected ? 1 : 0));

  useEffect(() => {
    if (!selected) {
      progress.setValue(0); // hidden by the tab bar already: start from 0 next time
      return;
    }
    if (reduceMotion) {
      progress.setValue(1);
      return;
    }
    const anim = Animated.spring(progress, { toValue: 1, friction: 5, tension: 140, useNativeDriver: true });
    anim.start();
    return () => anim.stop();
  }, [selected, reduceMotion, progress]);

  const pill = {
    opacity: progress.interpolate({ inputRange: [0, 0.5, 1], outputRange: [0, 1, 1], extrapolate: 'clamp' }),
    transform: [{ scaleX: progress.interpolate({ inputRange: [0, 1], outputRange: [0.3, 1] }) }],
  };
  const glyph = {
    transform: [
      { scale: progress.interpolate({ inputRange: [0, 1], outputRange: [0.7, 1] }) },
      { translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [3, 0] }) },
    ],
  };

  return (
    <View style={{ width: size, height: size, alignItems: 'center', justifyContent: 'center' }}>
      <Animated.View
        pointerEvents="none"
        style={[
          styles.pill,
          { backgroundColor: t.accentSoft, left: (size - PILL_WIDTH) / 2, top: (size - PILL_HEIGHT) / 2 },
          pill,
        ]}
      />
      <Animated.View style={glyph}>
        <Ionicons name={name} color={color as string} size={size} />
      </Animated.View>
    </View>
  );
}

/** Follows the phone's "Reduce motion" accessibility setting. */
export function useReduceMotion() {
  const [reduce, setReduce] = useState(false);
  useEffect(() => {
    let alive = true;
    AccessibilityInfo.isReduceMotionEnabled().then((on) => alive && setReduce(on)).catch(() => {});
    const sub = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduce);
    return () => {
      alive = false;
      sub.remove();
    };
  }, []);
  return reduce;
}

const styles = StyleSheet.create({
  pill: { position: 'absolute', width: PILL_WIDTH, height: PILL_HEIGHT, borderRadius: PILL_HEIGHT / 2 },
});
