import React, { useState } from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';

const COLOR = '#534AB7';

const techs = [
  {
    icon: '⚡',
    title: '주입 엔진',
    tag: 'Roslyn Code Gen',
    desc: '빌드 타임에 코드를 생성해 런타임 리플렉션 없이 상수 O(1) 주입을 실행합니다.',
    points: ['Roslyn 1순위 (빌드 타임 생성)', 'Expression Tree 폴백', 'FieldInfo 최종 폴백'],
  },
  {
    icon: '🔒',
    title: 'Bake 시스템',
    tag: 'Zero Runtime Scan',
    desc: '에디터에서 의존성을 사전 분석해 Awake에서 스캔 비용이 전혀 발생하지 않습니다.',
    points: ['Awake 부하 완전 제거', 'GC Alloc 없는 주입', '스냅샷 배열 기반 Tick 처리'],
  },
  {
    icon: '🔧',
    title: '에디터 툴링',
    tag: 'GraphView · BakeValidator',
    desc: '의존성 구조를 에디터 안에서 실시간으로 시각화하고 오류를 사전 감지합니다.',
    points: ['GraphView 의존성 그래프', 'BakeValidator 빌드 검증', 'FallbackGuard 감지'],
  },
  {
    icon: '🛡️',
    title: 'IL2CPP / AOT',
    tag: 'Safe Fallback Chain',
    desc: 'Roslyn 우선 폴백 체인으로 IL2CPP Strip과 AOT 환경을 완전히 대응합니다.',
    points: ['IL2CPP Strip 안전', 'AOT 환경 폴백 보장', '플랫폼 무관 동작'],
  },
  {
    icon: '🗂️',
    title: '스코프 관리',
    tag: '3-Tier Lifecycle',
    desc: 'Global · Scene · Object 3계층으로 Unity 생명주기를 그대로 매핑합니다.',
    points: ['Global / Scene / Object 계층', 'Named 바인딩 (RegistryKey)', 'parentScope 폴백 체인'],
  },
  {
    icon: '🍃',
    title: 'GC 최적화',
    tag: 'Setter Inject',
    desc: 'Setter Inject가 필드 Resolve Getter를 자동 제거해 런타임 GC 비용을 줄입니다.',
    points: ['필드 Resolve 비용 자동 제거', 'ITickable 스코프 위임', '오브젝트 풀 완전 연동'],
  },
];

export default function PerformGraph() {
  const [showBenchmark, setShowBenchmark] = useState(false);
  const benchImgSrc = useBaseUrl('/img/benched.png');

  return (
    <div className="pg-root">
      <style>{`
        .pg-root {
          font-family: var(--ifm-font-family-base);
          color: var(--ifm-font-color-base);
          width: 100%;
          box-sizing: border-box;
          padding: 0.5rem 0 2rem;
        }

        /* ── Modal ── */
        .pg-modal {
          position: fixed;
          inset: 0;
          background: rgba(0, 0, 0, 0.84);
          display: flex;
          align-items: center;
          justify-content: center;
          z-index: 9999;
          opacity: 0;
          pointer-events: none;
          transition: opacity 0.22s ease;
          backdrop-filter: blur(5px);
        }
        .pg-modal.pg-open {
          opacity: 1;
          pointer-events: all;
        }
        .pg-modal img {
          max-width: min(92vw, 640px);
          max-height: 88vh;
          object-fit: contain;
          border-radius: 14px;
          box-shadow: 0 28px 90px rgba(0, 0, 0, 0.65);
          transform: scale(0.93);
          transition: transform 0.22s ease;
        }
        .pg-modal.pg-open img {
          transform: scale(1);
        }

        /* ── Hero banner ── */
        .pg-hero {
          display: flex;
          align-items: center;
          justify-content: space-between;
          gap: 24px;
          padding: 28px 32px;
          border-radius: 18px;
          border: 1px solid rgba(83, 74, 183, 0.22);
          background: linear-gradient(135deg,
            rgba(83, 74, 183, 0.06) 0%,
            var(--ifm-background-surface-color) 60%
          );
          margin-bottom: 3rem;
          flex-wrap: wrap;
        }
        .pg-hero-text { flex: 1; min-width: 0; }
        .pg-hero-label {
          font-size: 11px;
          font-weight: 800;
          letter-spacing: 0.12em;
          text-transform: uppercase;
          color: #534AB7;
          margin-bottom: 6px;
          opacity: 0.8;
        }
        .pg-hero-title {
          font-size: 22px;
          font-weight: 800;
          color: var(--ifm-font-color-base);
          margin-bottom: 5px;
          line-height: 1.2;
        }
        .pg-hero-sub {
          font-size: 14px;
          opacity: 0.55;
          line-height: 1.6;
        }

        /* ── Benchmark button ── */
        .pg-bench-btn {
          display: inline-flex;
          align-items: center;
          gap: 8px;
          padding: 11px 22px;
          background: #534AB7;
          color: #fff;
          border: none;
          border-radius: 10px;
          font-size: 14px;
          font-weight: 700;
          letter-spacing: 0.02em;
          cursor: pointer;
          white-space: nowrap;
          flex-shrink: 0;
          box-shadow: 0 4px 18px rgba(83, 74, 183, 0.32);
          transition: transform 0.15s ease, box-shadow 0.15s ease, opacity 0.15s;
          opacity: 0.92;
        }
        .pg-bench-btn:hover {
          opacity: 1;
          transform: translateY(-2px);
          box-shadow: 0 8px 28px rgba(83, 74, 183, 0.42);
        }
        .pg-bench-btn:active {
          transform: translateY(0);
          box-shadow: 0 4px 14px rgba(83, 74, 183, 0.28);
        }
        .pg-bench-btn-icon {
          font-size: 16px;
          line-height: 1;
        }

        /* ── Section label ── */
        .pg-section-label {
          font-size: 11px;
          font-weight: 800;
          letter-spacing: 0.14em;
          text-transform: uppercase;
          color: var(--ifm-color-primary);
          display: flex;
          align-items: center;
          gap: 10px;
          margin-bottom: 1.4rem;
        }
        .pg-section-label::after {
          content: '';
          flex: 1;
          height: 1px;
          background: linear-gradient(to right, var(--ifm-color-emphasis-300), transparent);
        }

        /* ── Tech card grid ── */
        .pg-grid {
          display: grid;
          grid-template-columns: repeat(3, minmax(0, 1fr));
          gap: 16px;
        }
        .pg-card {
          background: var(--ifm-background-surface-color);
          border: 1px solid var(--ifm-color-emphasis-200);
          border-radius: 14px;
          padding: 20px 22px 18px;
          transition: border-color 0.18s ease, box-shadow 0.18s ease, transform 0.18s ease;
        }
        .pg-card:hover {
          border-color: rgba(83, 74, 183, 0.38);
          box-shadow: 0 6px 24px rgba(83, 74, 183, 0.1);
          transform: translateY(-2px);
        }
        .pg-card-icon {
          font-size: 22px;
          margin-bottom: 10px;
          line-height: 1;
        }
        .pg-card-title {
          font-size: 15px;
          font-weight: 800;
          color: var(--ifm-font-color-base);
          margin-bottom: 3px;
        }
        .pg-card-tag {
          display: inline-block;
          font-size: 10px;
          font-weight: 700;
          letter-spacing: 0.06em;
          color: #534AB7;
          background: rgba(83, 74, 183, 0.1);
          border-radius: 5px;
          padding: 2px 7px;
          margin-bottom: 10px;
        }
        .pg-card-desc {
          font-size: 12.5px;
          line-height: 1.6;
          opacity: 0.75;
          margin-bottom: 12px;
          word-break: keep-all;
        }
        .pg-card-points {
          margin: 0;
          padding: 0;
          list-style: none;
          display: flex;
          flex-direction: column;
          gap: 5px;
        }
        .pg-card-points li {
          font-size: 12px;
          line-height: 1.5;
          padding-left: 14px;
          position: relative;
          opacity: 0.85;
        }
        .pg-card-points li::before {
          content: '';
          position: absolute;
          left: 0;
          top: 7px;
          width: 5px;
          height: 5px;
          border-radius: 50%;
          background: #534AB7;
          opacity: 0.6;
        }

        /* ── Responsive ── */
        @media (max-width: 900px) {
          .pg-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
        }
        @media (max-width: 600px) {
          .pg-grid { grid-template-columns: 1fr; }
          .pg-hero { padding: 20px; }
          .pg-bench-btn { width: 100%; justify-content: center; }
        }
      `}</style>

      {/* ── Modal ── */}
      <div
        className={'pg-modal' + (showBenchmark ? ' pg-open' : '')}
        onClick={() => setShowBenchmark(false)}
      >
        <img
          src={benchImgSrc}
          alt="UNInject 인젝션 성능 벤치마크"
          onClick={e => e.stopPropagation()}
        />
      </div>

      {/* ── Hero ── */}
      <div className="pg-hero">
        <div className="pg-hero-text">
          <div className="pg-hero-label">Performance</div>
          <div className="pg-hero-title">UNInject v2.1</div>
          <div className="pg-hero-sub">
            MonoBehaviour가 통제권을 갖는 Unity-first DI 솔루션.<br />
            Roslyn Code Gen 기반 O(1) 주입 · IL2CPP 완전 대응 · 에디터 툴링 내장.
          </div>
        </div>
        <button
          className="pg-bench-btn"
          onClick={() => setShowBenchmark(v => !v)}
        >
          <span className="pg-bench-btn-icon">📊</span>
          주입 성능 벤치마크
        </button>
      </div>

      {/* ── Tech cards ── */}
      <div className="pg-section-label">Core Technologies</div>
      <div className="pg-grid">
        {techs.map((t, i) => (
          <div key={i} className="pg-card">
            <div className="pg-card-icon">{t.icon}</div>
            <div className="pg-card-title">{t.title}</div>
            <div className="pg-card-tag">{t.tag}</div>
            <div className="pg-card-desc">{t.desc}</div>
            <ul className="pg-card-points">
              {t.points.map((p, j) => <li key={j}>{p}</li>)}
            </ul>
          </div>
        ))}
      </div>
    </div>
  );
}
