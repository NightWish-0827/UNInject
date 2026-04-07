import React, { useEffect, useRef } from 'react';
import { Chart, registerables } from 'chart.js';

Chart.register(...registerables);

const PerformGraph = () => {
  const chartRef = useRef(null);
  const chartInstance = useRef(null);

  const axisKo = ['런타임 성능', '에디터 툴링', 'IL2CPP 안전성', 'API 간결성', '스코프 관리', 'Unity 통합 깊이', '진단/빌드 검증', '런타임 유연성'];
  
  const dataList = [
    { label: 'UNInject v2.1', scores: [9, 10, 9, 9, 10, 10, 10, 10], color: '#534AB7', total: 96 },
    { label: 'VContainer',    scores: [9, 5, 8, 8, 8, 7, 5, 8],       color: '#0F6E56', total: 74 },
    { label: 'Zenject',       scores: [5, 7, 6, 6, 9, 8, 7, 9],       color: '#993C1D', total: 71 },
  ];

  const notes = [
    ['스냅샷 배열 Tick + Bake 기반 조회', 'IL emit 고속 주입', '리플렉션 의존, 느림'],
    ['GraphView + BakeValidator + FallbackGuard', '에디터 툴링 없음', '기본 수준'],
    ['Roslyn 1순위 → ExprTree → FieldInfo', 'IL emit, AOT 부분 제약', 'AOT 제약 많음'],
    ['어트리뷰트 단순 선언', 'Binding 간결', 'Binding DSL 복잡'],
    ['Named 바인딩 + parentScope + Tickable', 'LifetimeScope 계층', 'SubContainer 정밀'],
    ['IPoolInjectionTarget + SpawnInjected', 'PlayerLoop ITickable', 'Signals 내장'],
    ['Named 검증 + 생성자 폴백 감지', '없음', '기본 수준'],
    ['Create<T> + ITickable 위임', 'PlayerLoop ITickable', 'PlayerLoop + Signals'],
  ];

  const buildChart = () => {
    if (!chartRef.current) return;
    if (chartInstance.current) {
      chartInstance.current.destroy();
      chartInstance.current = null;
    }
    const ctx = chartRef.current.getContext('2d');
    chartInstance.current = new Chart(ctx, {
      type: 'radar',
      data: {
        labels: axisKo,
        datasets: dataList.map((d, i) => ({
          label: d.label,
          data: d.scores,
          borderColor: d.color,
          backgroundColor: d.color + (i === 0 ? '25' : '10'),
          pointBackgroundColor: d.color,
          borderWidth: i === 0 ? 3 : 1.5,
          pointRadius: i === 0 ? 4 : 2,
        })),
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        aspectRatio: 1.4,
        plugins: { legend: { display: false } },
        scales: {
          r: {
            min: 0,
            max: 10,
            ticks: { display: false },
            grid: { color: 'rgba(128,128,128,0.15)' },
            angleLines: { color: 'rgba(128,128,128,0.15)' },
            pointLabels: { font: { size: 11, weight: '600' }, color: '#888' },
          },
        },
      },
    });
  };

  useEffect(() => {
    buildChart();

    // Chart.js responsive:true 가 내부 ResizeObserver로 canvas 크기를 관리함.
    // 외부에서 추가로 resize/chart.resize()를 호출하면 두 경로가 경쟁하며
    // 레이아웃 재계산이 연속 트리거되어 미세한 떨림이 발생함.
    // → 외부 핸들러 없이 Chart.js 내장 ResizeObserver에만 위임.
    return () => {
      chartInstance.current?.destroy();
      chartInstance.current = null;
    };
  }, []);

  return (
    <div className="perf-diag-container">
      <style>{`
        .perf-diag-container {
          padding: 1rem 0;
          font-family: var(--ifm-font-family-base);
          color: var(--ifm-font-color-base);
          /* 컨테이너가 부모 너비를 100% 따르도록 */
          width: 100%;
          box-sizing: border-box;
        }

        /* ── 상단: 레이더 + 요약 ── */
        .diag-header {
          display: grid;
          /* 좁은 화면에서도 양쪽이 최소 0으로 수축 가능하도록 minmax 사용 */
          grid-template-columns: minmax(0, 1.2fr) minmax(0, 0.8fr);
          gap: 32px;
          margin-bottom: 4rem;
          align-items: center;
        }

        /* radar-box: Chart.js가 canvas 크기를 직접 관리하므로 CSS override 제거.
           aspect-ratio로 컨테이너 높이를 확보해 레이아웃 재계산 루프 방지. */
        .radar-box {
          position: relative;
          width: 100%;
          aspect-ratio: 1.4 / 1;
        }
        .radar-box canvas {
          display: block;
        }

        .score-summary { display: flex; flex-direction: column; gap: 15px; }
        .score-card {
          padding: 1.2rem 1.5rem;
          border-radius: 16px;
          background: var(--ifm-color-emphasis-100);
          display: flex;
          justify-content: space-between;
          align-items: center;
          box-shadow: inset 0 0 0 1px var(--ifm-color-emphasis-200);
        }
        .sc-name { font-size: 13px; font-weight: 700; opacity: 0.8; }
        .sc-val-box { text-align: right; }
        .sc-val { font-size: 32px; font-weight: 800; line-height: 1; }
        .sc-sub { font-size: 12px; opacity: 0.5; margin-left: 2px; }

        /* ── 섹션 타이틀 ── */
        .section-title {
          font-size: 13px;
          font-weight: 800;
          margin-bottom: 2rem;
          display: flex;
          align-items: center;
          gap: 12px;
          text-transform: uppercase;
          letter-spacing: 0.1em;
          color: var(--ifm-color-primary);
        }
        .section-title::after {
          content: "";
          flex: 1;
          height: 1px;
          background: linear-gradient(to right, var(--ifm-color-emphasis-300), transparent);
        }

        /* ── Breakdown ── */
        .breakdown-list { display: flex; flex-direction: column; gap: 24px; margin-bottom: 4rem; }
        .breakdown-row {
          background: var(--ifm-background-surface-color);
          border: 1px solid var(--ifm-color-emphasis-200);
          border-radius: 12px;
          overflow: hidden;
        }
        .row-header {
          background: var(--ifm-color-emphasis-100);
          padding: 10px 20px;
          font-weight: 700;
          font-size: 14px;
          border-bottom: 1px solid var(--ifm-color-emphasis-200);
          color: var(--ifm-color-primary-darker);
        }
        .row-content { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); }
        .lib-col {
          padding: 15px 20px;
          border-right: 1px solid var(--ifm-color-emphasis-200);
          min-width: 0; /* grid overflow 방지 */
        }
        .lib-col:last-child { border-right: none; }
        .lib-label { font-size: 11px; font-weight: 700; opacity: 0.5; margin-bottom: 8px; text-transform: uppercase; }
        .score-bar-group { display: flex; align-items: center; gap: 10px; margin-bottom: 8px; }
        .progress-bg { flex: 1; height: 6px; background: var(--ifm-color-emphasis-200); border-radius: 3px; overflow: hidden; min-width: 0; }
        .progress-fill { height: 100%; border-radius: 3px; }
        .score-text { font-weight: 800; font-size: 14px; min-width: 20px; }
        .note-text { font-size: 12px; line-height: 1.5; opacity: 0.9; color: var(--ifm-font-color-base); word-break: keep-all; }

        /* ── Detail Cards ── */
        .detail-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; }
        .detail-card {
          padding: 1.5rem;
          border-radius: 16px;
          border: 1px solid var(--ifm-color-emphasis-200);
          background: var(--ifm-background-surface-color);
          min-width: 0;
        }
        .detail-card h3 { font-size: 15px; margin-bottom: 1.2rem; font-weight: 800; display: flex; align-items: center; gap: 8px; }
        .detail-item { font-size: 12px; margin-bottom: 10px; display: flex; gap: 10px; line-height: 1.6; align-items: flex-start; }
        .dot { width: 6px; height: 6px; border-radius: 50%; margin-top: 7px; flex-shrink: 0; }

        /* ── 반응형 breakpoints ── */

        /* Docusaurus 사이드바 포함 시 실제 컨텐츠 폭 기준으로 조정 */
        @media (max-width: 1100px) {
          .diag-header {
            grid-template-columns: 1fr;
            gap: 24px;
          }
          .radar-box {
            max-width: 520px;
            margin: 0 auto;
          }
          .score-summary { flex-direction: row; flex-wrap: wrap; }
          .score-card { flex: 1 1 140px; }
        }

        @media (max-width: 768px) {
          .row-content, .detail-grid {
            grid-template-columns: 1fr;
          }
          .lib-col {
            border-right: none;
            border-bottom: 1px solid var(--ifm-color-emphasis-200);
          }
          .lib-col:last-child { border-bottom: none; }
          .score-summary { flex-direction: column; }
        }

        @media (max-width: 480px) {
          .sc-val { font-size: 24px; }
          .radar-box { max-width: 100%; }
        }
      `}</style>

      {/* 1. Header: Radar & Summary */}
      <div className="diag-header">
        <div className="radar-box">
          <canvas ref={chartRef}></canvas>
        </div>
        <div className="score-summary">
          {dataList.map(d => (
            <div key={d.label} className="score-card">
              <div className="sc-name" style={{ color: d.color }}>{d.label}</div>
              <div className="sc-val-box">
                <span className="sc-val" style={{ color: d.color }}>{d.total}</span>
                <span className="sc-sub">/100</span>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* 2. Breakdown */}
      <div className="section-title">Detailed Breakdown</div>
      <div className="breakdown-list">
        {axisKo.map((axis, i) => (
          <div key={axis} className="breakdown-row">
            <div className="row-header">{axis}</div>
            <div className="row-content">
              {dataList.map((lib, li) => (
                <div key={lib.label} className="lib-col">
                  <div className="lib-label">{lib.label}</div>
                  <div className="score-bar-group">
                    <div className="progress-bg">
                      <div className="progress-fill" style={{ width: `${lib.scores[i] * 10}%`, background: lib.color }} />
                    </div>
                    <span className="score-text" style={{ color: lib.color }}>{lib.scores[i]}</span>
                  </div>
                  <div className="note-text">{notes[i][li]}</div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </div>

      {/* 3. Advantages */}
      <div className="section-title">Key Comparisons</div>
      <div className="detail-grid">
        <AdvantageCard title="UNInject v2.1" color="#534AB7" items={[
          "Bake 기반 제로런타임 스캔 (Awake 부하 X)",
          "Roslyn → ExprTree → FieldInfo 폴백 체인",
          "GraphView 시각화 & BakeValidator 검증",
          "RegistryKey 기반 다중 바인딩 지원",
          "ITickable 스코프 위임 및 풀 연동",
        ]} />
        <AdvantageCard title="VContainer" color="#0F6E56" items={[
          "생성자 주입 중심의 Pure C# 설계",
          "IL emit 기반 고속 주입 성능",
          "LifetimeScope 기반의 명확한 계층",
          "PlayerLoop 기반 Pure C# 생명주기",
          "MessagePipe 공식 통합",
        ]} />
        <AdvantageCard title="Zenject" color="#993C1D" items={[
          "가장 방대한 생태계와 레퍼런스",
          "SubContainer를 통한 정밀 스코프 제어",
          "강력한 내장 Signals 시스템",
          "Scene/Project Context 계층 구조",
          "다양한 확장 플러그인 존재",
        ]} />
      </div>
    </div>
  );
};

const AdvantageCard = ({ title, color, items }) => (
  <div className="detail-card">
    <h3 style={{ color }}>
      <span style={{ width: 4, height: 16, background: color, borderRadius: 2, display: 'inline-block' }}></span>
      {title}
    </h3>
    {items.map((item, idx) => (
      <div key={idx} className="detail-item">
        <span className="dot" style={{ background: color }}></span>
        <span>{item}</span>
      </div>
    ))}
  </div>
);

export default PerformGraph;