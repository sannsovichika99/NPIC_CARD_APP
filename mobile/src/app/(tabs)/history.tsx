import { router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, StyleSheet, Text, View } from 'react-native';
import { Badge, ConnectionBanner, EmptyState } from '@/components/ui';
import { api, ApiError, HistoryPage } from '@/lib/api';
import { usePairing } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { formatDateTime, khmerText, useTheme } from '@/lib/theme';

/** The latest card sides sent to the printer (Card Studio's Print History). */
export default function HistoryScreen() {
  const t = useTheme();
  const tr = useT();
  const pairing = usePairing();
  const [page, setPage] = useState<HistoryPage | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    try {
      setPage(await api.history(pairing, 200));
      setError(null);
    } catch (e) {
      setError((e as ApiError).message);
    } finally {
      setRefreshing(false);
    }
  }, [pairing]);

  useFocusEffect(
    useCallback(() => {
      void load();
    }, [load]),
  );

  if (!page && !error) return <ActivityIndicator color={t.accent} style={{ marginTop: 40 }} />;

  return (
    <FlatList
      data={page?.items ?? []}
      keyExtractor={(r, i) => `${r.printedAt}-${r.studentId}-${r.side}-${i}`}
      contentContainerStyle={{ paddingBottom: 24 }}
      refreshControl={
        <RefreshControl
          refreshing={refreshing}
          onRefresh={() => {
            setRefreshing(true);
            void load();
          }}
          tintColor={t.accent}
        />
      }
      ListHeaderComponent={
        <View style={{ gap: 12, paddingBottom: 8 }}>
          <ConnectionBanner />
          {page && (
            <View style={styles.stats}>
              <Stat label={tr.history.printedToday} value={page.printedToday} />
              <Stat label={tr.history.allTime} value={page.total} />
            </View>
          )}
        </View>
      }
      ItemSeparatorComponent={() => <View style={{ height: 8 }} />}
      ListEmptyComponent={
        error ? (
          <EmptyState icon="cloud-offline-outline" title={tr.history.cannotLoad} text={error} />
        ) : (
          <EmptyState icon="print-outline" title={tr.history.nothing} text={tr.history.nothingText} />
        )
      }
      renderItem={({ item }) => (
        <Pressable
          onPress={() => router.push({ pathname: '/student/[id]', params: { id: item.studentId } })}
          style={({ pressed }) => [styles.row, { backgroundColor: pressed ? t.surfaceAlt : t.surface, borderColor: t.border }]}>
          <View style={{ flex: 1, gap: 2 }}>
            <Text style={[styles.name, khmerText, { color: t.text }]} numberOfLines={1}>
              {item.nameKh || item.nameEn}
            </Text>
            <Text style={{ color: t.textSoft }} numberOfLines={1}>
              {item.nameEn} · {item.studentId}
            </Text>
            <Text style={{ color: t.textMuted, fontSize: 13 }} numberOfLines={1}>
              {formatDateTime(item.printedAt, tr.months)} · {item.printer || item.computer}
            </Text>
          </View>
          <Badge label={item.side === 'BACK' ? tr.common.back_side : tr.common.front} tone={item.side === 'BACK' ? 'neutral' : 'accent'} />
        </Pressable>
      )}
    />
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  const t = useTheme();
  return (
    <View style={[styles.stat, { backgroundColor: t.surface, borderColor: t.border }]}>
      <Text style={{ color: t.accent, fontSize: 26, fontWeight: '700' }}>{value.toLocaleString()}</Text>
      <Text style={{ color: t.textSoft }}>{label}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  stats: { flexDirection: 'row', gap: 10, marginHorizontal: 16, marginTop: 12 },
  stat: { flex: 1, padding: 14, borderRadius: 16, borderWidth: StyleSheet.hairlineWidth },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    marginHorizontal: 16,
    padding: 12,
    borderRadius: 14,
    borderWidth: StyleSheet.hairlineWidth,
  },
  name: { fontSize: 16, fontWeight: '600' },
});
