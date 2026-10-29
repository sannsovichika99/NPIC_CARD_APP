import Ionicons from '@expo/vector-icons/Ionicons';
import { Href, router, useFocusEffect } from 'expo-router';
import { useCallback, useState } from 'react';
import { Pressable, RefreshControl, ScrollView, StyleSheet, Text, View } from 'react-native';
import { Badge, Card, ConnectionBanner, EmptyState, IconName } from '@/components/ui';
import { api, HistoryPage, StudentFilter } from '@/lib/api';
import { useConnection, usePairing } from '@/lib/connection';
import { useT } from '@/lib/i18n';
import { formatDateTime, khmerText, Theme, useTheme } from '@/lib/theme';

const RECENT = 4;

/**
 * Home: which PC the phone works with, how far the cards are (photos, printed, RFID), what is left to do
 * (each line opens the student list already filtered), shortcuts to the other tabs and the latest prints.
 */
export default function HomeScreen() {
  const t = useTheme();
  const tr = useT();
  const pairing = usePairing();
  const { info, error, refresh } = useConnection();
  const [history, setHistory] = useState<HistoryPage | null>(null);
  const [hour, setHour] = useState(() => new Date().getHours());
  const [refreshing, setRefreshing] = useState(false);

  const loadHistory = useCallback(async () => {
    try {
      setHistory(await api.history(pairing, RECENT));
    } catch {
      // the connection banner already says what is wrong; keep the last list shown
    }
  }, [pairing]);

  // Back on Home (after a photo, a print…) → fresh counts and prints.
  useFocusEffect(
    useCallback(() => {
      setHour(new Date().getHours());
      void refresh();
      void loadHistory();
    }, [refresh, loadHistory]),
  );

  const onRefresh = async () => {
    setRefreshing(true);
    await Promise.all([refresh(), loadHistory()]);
    setRefreshing(false);
  };

  const openStudents = (filter: StudentFilter) =>
    router.navigate({ pathname: '/students', params: { filter, at: stamp() } });

  const total = info?.students ?? 0;
  const todo = info
    ? ([
        { filter: 'nophoto', left: total - info.withPhoto, icon: 'camera-outline', text: tr.home.noPhoto(total - info.withPhoto) },
        { filter: 'notprinted', left: total - info.printed, icon: 'print-outline', text: tr.home.notPrinted(total - info.printed) },
        { filter: 'nocard', left: total - info.withCard, icon: 'radio-outline', text: tr.home.noCard(total - info.withCard) },
      ] as { filter: StudentFilter; left: number; icon: IconName; text: string }[]).filter((x) => x.left > 0)
    : [];

  return (
    <ScrollView
      contentContainerStyle={styles.page}
      refreshControl={<RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={t.accent} />}>
      <ConnectionBanner />

      {/* Who and where */}
      <View style={styles.hero}>
        <Text style={[styles.greeting, { color: t.textSoft }]}>{tr.home.greeting(hour)}</Text>
        <Text style={[styles.title, { color: t.text }]} numberOfLines={2}>
          {tr.home.onPc(info?.computer || pairing.pcName)}
        </Text>
        <View style={styles.heroLine}>
          <View style={[styles.dot, { backgroundColor: error ? t.warning : t.success }]} />
          <Text style={{ color: t.textSoft, flex: 1 }} numberOfLines={1}>
            {error ? tr.pc.notAnswering : info?.source ? tr.home.list(info.source) : tr.pc.noList}
          </Text>
        </View>
      </View>

      {info && !info.photoFolderOk && (
        <View style={[styles.notice, { backgroundColor: t.warningSoft }]}>
          <Ionicons name="folder-open-outline" size={18} color={t.warning} />
          <Text style={{ color: t.warning, flex: 1 }}>{tr.pc.photoFolderMissing}</Text>
        </View>
      )}

      {info && total === 0 && (
        <Card>
          <EmptyState icon="document-outline" title={tr.students.noneLoaded} text={tr.students.noneLoadedText} />
        </Card>
      )}

      {/* Progress */}
      {info && total > 0 && (
        <Card style={{ gap: 14 }}>
          <View style={styles.sectionHead}>
            <Text style={[styles.section, { color: t.text }]}>{tr.home.progress}</Text>
            <Text style={{ color: t.textSoft }}>{tr.home.ofStudents(total)}</Text>
          </View>
          <Progress t={t} label={tr.home.photos} icon="camera-outline" value={info.withPhoto} total={total} />
          <Progress t={t} label={tr.home.printed} icon="print-outline" value={info.printed} total={total} />
          <Progress t={t} label={tr.home.rfid} icon="radio-outline" value={info.withCard} total={total} />
        </Card>
      )}

      {/* To do: each line opens the filtered list */}
      {info && total > 0 && (
        <Card style={{ paddingVertical: 6 }}>
          <Text style={[styles.section, { color: t.text, paddingTop: 10, paddingBottom: 4 }]}>{tr.home.todo}</Text>
          {todo.length === 0 ? (
            <View style={styles.todoRow}>
              <View style={[styles.todoIcon, { backgroundColor: t.successSoft }]}>
                <Ionicons name="checkmark-done" size={18} color={t.success} />
              </View>
              <View style={{ flex: 1 }}>
                <Text style={{ color: t.text, fontWeight: '600' }}>{tr.home.allDone}</Text>
                <Text style={{ color: t.textSoft }}>{tr.home.allDoneText}</Text>
              </View>
            </View>
          ) : (
            todo.map((item, i) => (
              <Pressable
                key={item.filter}
                accessibilityRole="button"
                onPress={() => openStudents(item.filter)}
                style={({ pressed }) => [
                  styles.todoRow,
                  i > 0 && { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: t.border },
                  { opacity: pressed ? 0.6 : 1 },
                ]}>
                <View style={[styles.todoIcon, { backgroundColor: t.warningSoft }]}>
                  <Ionicons name={item.icon} size={18} color={t.warning} />
                </View>
                <Text style={{ color: t.text, flex: 1, fontSize: 15 }}>{item.text}</Text>
                <Ionicons name="chevron-forward" size={18} color={t.textMuted} />
              </Pressable>
            ))
          )}
        </Card>
      )}

      {/* Shortcuts */}
      <Text style={[styles.section, styles.outside, { color: t.text }]}>{tr.home.actions}</Text>
      <View style={styles.grid}>
        <Action t={t} icon="search-outline" title={tr.home.findStudent} text={tr.home.findStudentText} href="/students" />
        <Action t={t} icon="scan-outline" title={tr.home.checkCard} text={tr.home.checkCardText} href="/scan" />
        <Action t={t} icon="time-outline" title={tr.home.history} text={tr.home.historyText} href="/history" />
        <Action t={t} icon="desktop-outline" title={tr.home.pc} text={tr.home.pcText} href="/pc" />
      </View>

      {/* Latest prints */}
      {history && (
        <Card style={{ paddingVertical: 6 }}>
          <View style={[styles.sectionHead, { paddingTop: 10, paddingBottom: 4 }]}>
            <Text style={[styles.section, { color: t.text }]}>{tr.home.recent}</Text>
            {history.printedToday > 0 && <Badge label={tr.home.today(history.printedToday)} tone="success" />}
            <View style={{ flex: 1 }} />
            {history.total > 0 && (
              <Pressable onPress={() => router.navigate('/history')} hitSlop={10} accessibilityRole="link">
                <Text style={{ color: t.accent, fontWeight: '600' }}>{tr.home.seeAll}</Text>
              </Pressable>
            )}
          </View>
          {history.items.length === 0 ? (
            <Text style={{ color: t.textSoft, paddingVertical: 10 }}>{tr.home.nothingPrinted}</Text>
          ) : (
            history.items.map((item, i) => (
              <Pressable
                key={`${item.printedAt}-${item.studentId}-${item.side}-${i}`}
                onPress={() => router.push({ pathname: '/student/[id]', params: { id: item.studentId } })}
                style={({ pressed }) => [
                  styles.recentRow,
                  i > 0 && { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: t.border },
                  { opacity: pressed ? 0.6 : 1 },
                ]}>
                <View style={{ flex: 1 }}>
                  <Text style={[{ color: t.text, fontSize: 15, fontWeight: '600' }, khmerText]} numberOfLines={1}>
                    {item.nameKh || item.nameEn}
                  </Text>
                  <Text style={{ color: t.textMuted, fontSize: 13 }} numberOfLines={1}>
                    {item.studentId} · {formatDateTime(item.printedAt, tr.months)}
                  </Text>
                </View>
                <Badge
                  label={item.side === 'BACK' ? tr.common.back_side : tr.common.front}
                  tone={item.side === 'BACK' ? 'neutral' : 'accent'}
                />
              </Pressable>
            ))
          )}
        </Card>
      )}
    </ScrollView>
  );
}

/** Makes each tap on a To do line a new route, so the list applies the filter again after it was changed there. */
function stamp() {
  return String(Date.now());
}

function Progress({ t, label, icon, value, total }: { t: Theme; label: string; icon: IconName; value: number; total: number }) {
  const share = total > 0 ? Math.min(1, value / total) : 0;
  const done = value >= total;
  return (
    <View style={{ gap: 6 }} accessible accessibilityLabel={`${label}: ${value} / ${total}`}>
      <View style={styles.progressHead}>
        <Ionicons name={done ? 'checkmark-circle' : icon} size={17} color={done ? t.success : t.accent} />
        <Text style={{ color: t.text, flex: 1, fontSize: 15 }}>{label}</Text>
        <Text style={{ color: t.text, fontWeight: '700', fontVariant: ['tabular-nums'] }}>
          {value.toLocaleString()}
          <Text style={{ color: t.textMuted, fontWeight: '400' }}> / {total.toLocaleString()}</Text>
        </Text>
      </View>
      <View style={[styles.track, { backgroundColor: t.surfaceAlt, borderColor: t.border }]}>
        <View style={[styles.fill, { width: `${share * 100}%`, backgroundColor: done ? t.success : t.accent }]} />
      </View>
    </View>
  );
}

function Action({ t, icon, title, text, href }: { t: Theme; icon: IconName; title: string; text: string; href: Href }) {
  return (
    <Pressable
      accessibilityRole="button"
      onPress={() => router.navigate(href)}
      style={({ pressed }) => [
        styles.action,
        { backgroundColor: pressed ? t.surfaceAlt : t.surface, borderColor: t.border },
      ]}>
      <View style={[styles.actionIcon, { backgroundColor: t.accentSoft }]}>
        <Ionicons name={icon} size={22} color={t.accent} />
      </View>
      <Text style={{ color: t.text, fontSize: 15, fontWeight: '600' }} numberOfLines={1}>
        {title}
      </Text>
      <Text style={{ color: t.textSoft, fontSize: 13 }} numberOfLines={2}>
        {text}
      </Text>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  page: { padding: 16, gap: 12, paddingBottom: 32 },
  hero: { gap: 4, paddingHorizontal: 4, paddingTop: 4 },
  greeting: { fontSize: 15 },
  title: { fontSize: 24, fontWeight: '700' },
  heroLine: { flexDirection: 'row', alignItems: 'center', gap: 8, marginTop: 2 },
  dot: { width: 8, height: 8, borderRadius: 4 },
  notice: { flexDirection: 'row', gap: 10, padding: 12, borderRadius: 12, alignItems: 'center' },
  sectionHead: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  section: { fontSize: 17, fontWeight: '700' },
  outside: { marginTop: 4, paddingHorizontal: 4 },
  progressHead: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  track: { height: 8, borderRadius: 4, overflow: 'hidden', borderWidth: StyleSheet.hairlineWidth },
  fill: { height: '100%', borderRadius: 4 },
  todoRow: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 10, minHeight: 48 },
  todoIcon: { width: 34, height: 34, borderRadius: 10, alignItems: 'center', justifyContent: 'center' },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: 10 },
  action: {
    flexGrow: 1,
    flexBasis: '45%',
    padding: 14,
    gap: 6,
    borderRadius: 16,
    borderWidth: StyleSheet.hairlineWidth,
  },
  actionIcon: { width: 40, height: 40, borderRadius: 12, alignItems: 'center', justifyContent: 'center', marginBottom: 4 },
  recentRow: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingVertical: 10, minHeight: 48 },
});
