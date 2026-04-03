// UNInjectProfiler 는 UNINJECT_PROFILING 스크립팅 심볼이 정의된 경우에만 활성화됨.
// Player Settings > Other Settings > Scripting Define Symbols 에 'UNINJECT_PROFILING' 을 추가하여 사용.
// 미활성 상태에서는 이 파일은 0바이트 오버헤드를 가짐 (전체가 조건부 컴파일 블록 안에 있음).

#if UNINJECT_PROFILING
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// UNInject 주입 비용을 타입별로 측정하고 리포트를 제공하는 진단 유틸리티.
///
/// 사용 방법:
///   1) Player Settings > Scripting Define Symbols 에 'UNINJECT_PROFILING' 추가.
///   2) 런타임 중 UNInjectProfiler.PrintReport() 호출 또는
///      Window > UNInject > Profiler Report 메뉴 사용 (에디터 전용).
///   3) UNInjectProfiler.Reset() 으로 누적 통계 초기화.
///
/// 주의:
///   - UNINJECT_PROFILING 미활성 시 이 클래스는 컴파일되지 않음.
///   - IL2CPP 빌드에서도 안전하게 동작하나, 프로덕션 빌드에는 포함하지 않도록 주의.
/// </summary>
public static class UNInjectProfiler
{
    // ─── 통계 구조체 ──────────────────────────────────────────────────────────

    public struct InjectionStat
    {
        /// <summary>총 주입 횟수</summary>
        public int Count;

        /// <summary>누적 주입 시간 (ms)</summary>
        public double TotalMs;

        /// <summary>최고 주입 시간 (ms)</summary>
        public double PeakMs;

        /// <summary>평균 주입 시간 (ms)</summary>
        public double AverageMs => Count > 0 ? TotalMs / Count : 0.0;
    }

    // ─── 내부 저장소 ──────────────────────────────────────────────────────────

    private static readonly Dictionary<Type, InjectionStat> _stats
        = new Dictionary<Type, InjectionStat>();

    private static readonly object _lock = new object();

    // ─── 기록 API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 한 타입에 대한 주입 소요 시간을 기록한다.
    /// ObjectInstaller.TryInjectTarget 의 #if UNINJECT_PROFILING 블록에서 호출됨.
    /// </summary>
    public static void RecordInjection(Type type, double elapsedMs)
    {
        if (type == null) return;

        lock (_lock)
        {
            if (!_stats.TryGetValue(type, out var stat))
                stat = default;

            stat.Count++;
            stat.TotalMs += elapsedMs;
            if (elapsedMs > stat.PeakMs)
                stat.PeakMs = elapsedMs;

            _stats[type] = stat;
        }
    }

    // ─── 조회 API ─────────────────────────────────────────────────────────────

    /// <summary>수집된 모든 타입의 통계를 읽기 전용으로 반환한다.</summary>
    public static IReadOnlyDictionary<Type, InjectionStat> GetStats()
    {
        lock (_lock)
            return new Dictionary<Type, InjectionStat>(_stats);
    }

    /// <summary>
    /// 누적 주입 비용이 높은 타입 순으로 정렬된 통계를 Debug.Log 로 출력한다.
    /// </summary>
    public static void PrintReport()
    {
        lock (_lock)
        {
            if (_stats.Count == 0)
            {
                Debug.Log("[UNInjectProfiler] No injection data recorded.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[UNInjectProfiler] ── Injection Cost Report ──────────────────────");
            sb.AppendLine($"{"Type",-40} {"Count",6} {"Total(ms)",12} {"Avg(ms)",10} {"Peak(ms)",10}");
            sb.AppendLine(new string('─', 82));

            foreach (var kvp in _stats.OrderByDescending(x => x.Value.TotalMs))
            {
                var s = kvp.Value;
                sb.AppendLine($"{kvp.Key.Name,-40} {s.Count,6} {s.TotalMs,12:F3} {s.AverageMs,10:F3} {s.PeakMs,10:F3}");
            }

            sb.AppendLine(new string('─', 82));
            Debug.Log(sb.ToString());
        }
    }

    /// <summary>누적 통계를 초기화한다.</summary>
    public static void Reset()
    {
        lock (_lock)
            _stats.Clear();

        Debug.Log("[UNInjectProfiler] Stats reset.");
    }

    // ─── RuntimeInitialize: 플레이 모드 진입 시 자동 초기화 ─────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration()
    {
        Reset();
    }
}
#endif
