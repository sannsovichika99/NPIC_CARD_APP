import Ionicons from '@expo/vector-icons/Ionicons';
import { router, useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ActivityIndicator, FlatList, Pressable, RefreshControl, ScrollView, StyleSheet, Text, TextInput, View } from 'react-native';
import { Badge, ConnectionBanner, EmptyState, StudentPhoto } from '@/components/ui';
import { api, ApiError, StudentFilter, StudentSummary } from '@/lib/api';
import { useConnection, usePairing } from '@/lib/connection';
import { khmerText, useTheme } from '@/lib/theme';

const PAGE = 50;

const FILTERS: { key: StudentFilter; label: string }[] = [
  { key: '', label: 'All' },
  { key: 'nophoto', label: 'No photo' },
  { key: 'notprinted', label: 'Not printed' },
  { key: 'nocard', label: 'No RFID card' },
];

/** The student list loaded in Card Studio on the PC: search, filter, open a student. */
export default function StudentsScreen() {
  const t = useTheme();
  const pairing = usePairing();
  const { info, refresh: refreshInfo } = useConnection();
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<StudentFilter>('');
  const [items, setItems] = useState<StudentSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const requestNo = useRef(0); // drops answers to older searches that arrive late

  const load = useCallback(
    async (offset: number) => {
      const no = ++requestNo.current;
      try {
        const page = await api.students(pairing, query.trim(), filter, offset, PAGE);
        if (no !== requestNo.current) return;
        setItems((old) => (offset === 0 ? page.items : [...old, ...page.items]));
        setTotal(page.total);
        setError(null);
      } catch (e) {
        if (no === requestNo.current) setError((e as ApiError).message);
      } finally {
        if (no === requestNo.current) {
          setLoading(false);
          setRefreshing(false);
        }
      }
    },
    [pairing, query, filter],
  );

  // New search / filter → first page (typing waits 250 ms).
  useEffect(() => {
    setLoading(true);
    const timer = setTimeout(() => load(0), query ? 250 : 0);
    return () => clearTimeout(timer);
  }, [load, query]);

  // Back on this tab (e.g. after taking a photo) → fresh photos and badges. Only on focus, not per keystroke.
  const loadRef = useRef(load);
  loadRef.current = load;
  const firstFocus = useRef(true);
  useFocusEffect(
    useCallback(() => {
      if (firstFocus.current) firstFocus.current = false; // the effect above loads the first page
      else void loadRef.current(0);
    }, []),
  );

  const onRefresh = () => {
    setRefreshing(true);
    void refreshInfo();
    void load(0);
  };

  const header = (
    <View style={{ gap: 12, paddingBottom: 8 }}>
      <ConnectionBanner />
      <View style={[styles.search, { backgroundColor: t.surface, borderColor: t.border }]}>
        <Ionicons name="search-outline" size={18} color={t.textMuted} />
        <TextInput
          value={query}
          onChangeText={setQuery}
          placeholder="ID, name (ខ្មែរ / Latin) or faculty"
          placeholderTextColor={t.textMuted}
          style={[styles.searchInput, { color: t.text }]}
          autoCorrect={false}
          clearButtonMode="while-editing"
          returnKeyType="search"
        />
      </View>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={{ gap: 8, paddingHorizontal: 16 }}>
        {FILTERS.map((f) => {
          const on = f.key === filter;
          return (
            <Pressable
              key={f.key}
              onPress={() => setFilter(f.key)}
              accessibilityRole="button"
              accessibilityState={{ selected: on }}
              style={[styles.chip, { backgroundColor: on ? t.accent : t.surface, borderColor: on ? t.accent : t.border }]}>
              <Text style={{ color: on ? t.onAccent : t.text, fontWeight: '600' }}>{f.label}</Text>
            </Pressable>
          );
        })}
      </ScrollView>
      {info && (
        <Text style={[styles.summary, { color: t.textSoft }]}>
          {loading ? 'Loading…' : `${total} of ${info.students} students`} · {info.withPhoto} with photo · {info.printed} printed
        </Text>
      )}
    </View>
  );

  return (
    <FlatList
      data={items}
      keyExtractor={(s) => s.studentId}
      ListHeaderComponent={header}
      contentContainerStyle={{ paddingBottom: 24 }}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={t.accent} />}
      onEndReachedThreshold={0.5}
      onEndReached={() => {
        if (!loading && items.length < total) void load(items.length);
      }}
      keyboardShouldPersistTaps="handled"
      renderItem={({ item }) => <StudentRow s={item} />}
      ItemSeparatorComponent={() => <View style={{ height: 8 }} />}
      ListEmptyComponent={
        loading ? (
          <ActivityIndicator color={t.accent} style={{ marginTop: 40 }} />
        ) : error ? (
          <EmptyState icon="cloud-offline-outline" title="Cannot load the students" text={error} />
        ) : info?.students === 0 ? (
          <EmptyState
            icon="document-outline"
            title="No students loaded on the PC"
            text="In Card Studio, use Import Students… (Ctrl+L) to load the list, then pull down here to refresh."
          />
        ) : (
          <EmptyState icon="search-outline" title="No student matches" text="Try another name or ID, or another filter." />
        )
      }
      ListFooterComponent={items.length > 0 && items.length < total ? <ActivityIndicator color={t.accent} style={{ margin: 16 }} /> : undefined}
    />
  );
}

function StudentRow({ s }: { s: StudentSummary }) {
  const t = useTheme();
  return (
    <Pressable
      onPress={() => router.push({ pathname: '/student/[id]', params: { id: s.studentId } })}
      style={({ pressed }) => [styles.row, { backgroundColor: pressed ? t.surfaceAlt : t.surface, borderColor: t.border }]}>
      <StudentPhoto student={s} width={52} radius={8} />
      <View style={{ flex: 1, gap: 2 }}>
        <Text style={[styles.name, khmerText, { color: t.text }]} numberOfLines={1}>
          {s.nameKh || s.nameEn}
        </Text>
        <Text style={{ color: t.textSoft }} numberOfLines={1}>
          {s.nameEn} · {s.studentId}
        </Text>
        <View style={styles.badges}>
          {!s.hasPhoto && <Badge label="No photo" tone="warning" icon="camera-outline" />}
          {s.printedAt ? <Badge label="Printed" tone="success" icon="checkmark" /> : <Badge label="Not printed" />}
          {s.rfidUid && <Badge label="RFID" tone="accent" icon="radio-outline" />}
        </View>
      </View>
      <Ionicons name="chevron-forward" size={18} color={t.textMuted} />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  search: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 8,
    marginHorizontal: 16,
    marginTop: 12,
    paddingHorizontal: 14,
    borderRadius: 14,
    borderWidth: 1,
  },
  searchInput: { flex: 1, paddingVertical: 12, fontSize: 16 },
  chip: { paddingHorizontal: 14, paddingVertical: 8, borderRadius: 18, borderWidth: 1 },
  summary: { marginHorizontal: 16, fontSize: 13 },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    marginHorizontal: 16,
    padding: 10,
    borderRadius: 14,
    borderWidth: StyleSheet.hairlineWidth,
  },
  name: { fontSize: 17, fontWeight: '600' },
  badges: { flexDirection: 'row', gap: 6, marginTop: 4, flexWrap: 'wrap' },
});
