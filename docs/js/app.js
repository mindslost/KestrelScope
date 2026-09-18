/* ==========================================================================
   KestrelScope — Main Application & Interactive Showcase Logic
   ========================================================================== */

document.addEventListener('DOMContentLoaded', () => {
  initShowcaseTabs();
  initMetricsChart();
  initLogsFilter();
  initCalculator();
  initQuickstartTabs();
  initCopyButtons();
  initMobileNav();
  initDatabaseDemo();

  // Initialize FlowMap demo by default
  if (typeof window.initFlowMap === 'function') {
    window.initFlowMap();
  }
});

/* ==========================================================================
   1. Showcase Tab Switching
   ========================================================================== */
let metricsChartInstance = null;

function initShowcaseTabs() {
  const tabs = document.querySelectorAll('.showcase-tab');
  const panels = document.querySelectorAll('.showcase-panel');

  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const target = tab.getAttribute('data-tab');

      tabs.forEach(t => t.classList.remove('active'));
      panels.forEach(p => p.classList.remove('active'));

      tab.classList.add('active');
      const activePanel = document.getElementById('panel-' + target);
      if (activePanel) {
        activePanel.classList.add('active');
      }

      if (target === 'flowmap' && typeof window.initFlowMap === 'function') {
        setTimeout(window.initFlowMap, 50);
      } else if (target === 'metrics') {
        setTimeout(() => {
          if (metricsChartInstance) {
            metricsChartInstance.resize();
          } else {
            initMetricsChart();
          }
        }, 50);
      }
    });
  });
}

/* ==========================================================================
   2. Chart.js Metrics Explorer
   ========================================================================== */
function initMetricsChart() {
  const canvas = document.getElementById('metricsCanvas');
  if (!canvas || typeof Chart === 'undefined') return;

  const ctx = canvas.getContext('2d');

  // Realistic sample telemetry points
  const timeLabels = ['10:00', '10:05', '10:10', '10:15', '10:20', '10:25', '10:30', '10:35', '10:40', '10:45', '10:50', '10:55'];
  const p95Latency = [42, 45, 48, 85, 142, 65, 52, 49, 44, 47, 50, 48];
  const p50Latency = [18, 19, 17, 24, 38, 22, 19, 18, 17, 19, 18, 18];

  const gradientP95 = ctx.createLinearGradient(0, 0, 0, 280);
  gradientP95.addColorStop(0, 'rgba(40, 134, 222, 0.3)');
  gradientP95.addColorStop(1, 'rgba(40, 134, 222, 0.0)');

  metricsChartInstance = new Chart(ctx, {
    type: 'line',
    data: {
      labels: timeLabels,
      datasets: [
        {
          label: 'P95 Latency (ms)',
          data: p95Latency,
          borderColor: '#2886de',
          backgroundColor: gradientP95,
          borderWidth: 2,
          pointBackgroundColor: '#2886de',
          pointRadius: 3,
          pointHoverRadius: 6,
          fill: true,
          tension: 0.35
        },
        {
          label: 'P50 Latency (ms)',
          data: p50Latency,
          borderColor: '#34d399',
          borderWidth: 1.8,
          borderDash: [4, 4],
          pointBackgroundColor: '#34d399',
          pointRadius: 2,
          fill: false,
          tension: 0.35
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      interaction: {
        mode: 'index',
        intersect: false
      },
      plugins: {
        legend: {
          position: 'top',
          labels: {
            color: '#cbd5e1',
            font: { family: "'Inter', sans-serif", size: 12 },
            boxWidth: 12
          }
        },
        tooltip: {
          backgroundColor: '#111622',
          borderColor: 'rgba(255, 255, 255, 0.1)',
          borderWidth: 1,
          titleColor: '#fff',
          bodyColor: '#94a3b8',
          titleFont: { family: "'JetBrains Mono', monospace", size: 12 },
          bodyFont: { family: "'Inter', sans-serif", size: 12 },
          padding: 10,
          boxPadding: 4
        }
      },
      scales: {
        x: {
          grid: { color: 'rgba(255, 255, 255, 0.04)' },
          ticks: { color: '#64748b', font: { family: "'JetBrains Mono', monospace", size: 11 } }
        },
        y: {
          grid: { color: 'rgba(255, 255, 255, 0.05)' },
          ticks: {
            color: '#64748b',
            font: { family: "'JetBrains Mono', monospace", size: 11 },
            callback: v => v + ' ms'
          }
        }
      }
    }
  });

  // Time-range filter buttons
  const filterBtns = document.querySelectorAll('.btn-filter');
  filterBtns.forEach(btn => {
    btn.addEventListener('click', () => {
      filterBtns.forEach(b => b.classList.remove('active'));
      btn.classList.add('active');

      // Slightly perturb data to simulate window shift
      const factor = btn.textContent === '24h' ? 1.5 : btn.textContent === '1h' ? 1.1 : 1.0;
      metricsChartInstance.data.datasets[0].data = p95Latency.map(v => Math.round(v * factor * (0.9 + Math.random() * 0.2)));
      metricsChartInstance.update();
    });
  });
}

/* ==========================================================================
   3. Log Search & Trace Cross-Link
   ========================================================================== */
function initLogsFilter() {
  const searchInput = document.getElementById('logSearchInput');
  if (!searchInput) return;

  searchInput.addEventListener('input', (e) => {
    const term = e.target.value.toLowerCase();
    const rows = document.querySelectorAll('.log-row');
    rows.forEach(row => {
      const text = row.textContent.toLowerCase();
      row.style.display = text.includes(term) ? '' : 'none';
    });
  });

  // Trace pill click: jumps directly to Traces tab
  const tracePills = document.querySelectorAll('.trace-pill');
  tracePills.forEach(pill => {
    pill.addEventListener('click', () => {
      const tracesTab = document.querySelector('.showcase-tab[data-tab="traces"]');
      if (tracesTab) {
        tracesTab.click();
        tracesTab.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    });
  });
}

/* ==========================================================================
   4. SaaS Tax ROI Savings Calculator
   ========================================================================== */
function initCalculator() {
  const hostsSlider = document.getElementById('hostsSlider');
  const gbSlider = document.getElementById('gbSlider');
  const hostsVal = document.getElementById('hostsVal');
  const gbVal = document.getElementById('gbVal');

  const saasCostEl = document.getElementById('calcSaasCost');
  const savingsEl = document.getElementById('calcSavings');

  if (!hostsSlider || !gbSlider) return;

  function recalculate() {
    const hosts = parseInt(hostsSlider.value, 10);
    const gbPerDay = parseInt(gbSlider.value, 10);

    hostsVal.textContent = hosts + (hosts === 1 ? ' host' : ' hosts');
    gbVal.textContent = gbPerDay + ' GB/day';

    // Pricing benchmark:
    // Datadog APM Host: ~$15/host/mo
    // Datadog Custom Metrics: ~$5/host/mo
    // Log Ingestion & 15-day Retention: $0.10/GB * (gbPerDay * 30 days)
    const hostCostPerMonth = hosts * 20;
    const logCostPerMonth = gbPerDay * 30 * 0.10;
    const monthlySaaSCost = Math.round(hostCostPerMonth + logCostPerMonth);
    const annualSavings = monthlySaaSCost * 12;

    saasCostEl.textContent = '$' + monthlySaaSCost.toLocaleString() + ' / mo';
    savingsEl.textContent = '$' + annualSavings.toLocaleString() + ' / yr';
  }

  hostsSlider.addEventListener('input', recalculate);
  gbSlider.addEventListener('input', recalculate);
  recalculate();
}

/* ==========================================================================
   5. Quickstart Tab Switching
   ========================================================================== */
function initQuickstartTabs() {
  const tabs = document.querySelectorAll('.code-tab');
  const panels = document.querySelectorAll('.code-panel');

  tabs.forEach(tab => {
    tab.addEventListener('click', () => {
      const target = tab.getAttribute('data-code');

      tabs.forEach(t => t.classList.remove('active'));
      panels.forEach(p => p.classList.remove('active'));

      tab.classList.add('active');
      const panel = document.getElementById('code-' + target);
      if (panel) panel.classList.add('active');
    });
  });
}

/* ==========================================================================
   6. Copy-to-Clipboard Buttons
   ========================================================================== */
function initCopyButtons() {
  const copyButtons = document.querySelectorAll('.btn-copy');

  copyButtons.forEach(btn => {
    btn.addEventListener('click', async () => {
      const text = btn.getAttribute('data-copy') || btn.closest('.hero-terminal, .code-panel')?.querySelector('code, .terminal-cmd')?.textContent;
      if (!text) return;

      const cleanText = text.replace(/^\$\s*/, '').trim();

      try {
        await navigator.clipboard.writeText(cleanText);
        const originalHtml = btn.innerHTML;
        btn.innerHTML = `
          <svg viewBox="0 0 24 24" width="14" height="14" fill="#10b981"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>
          <span style="color:#10b981">Copied!</span>
        `;
        setTimeout(() => {
          btn.innerHTML = originalHtml;
        }, 2000);
      } catch (err) {
        console.error('Clipboard copy failed', err);
      }
    });
  });
}

/* ==========================================================================
   7. Mobile Navigation Drawer
   ========================================================================== */
function initMobileNav() {
  const toggle = document.querySelector('.mobile-toggle');
  const navMenu = document.querySelector('.nav-menu');

  if (!toggle || !navMenu) return;

  toggle.addEventListener('click', () => {
    const isVisible = navMenu.style.display === 'flex';
    navMenu.style.display = isVisible ? 'none' : 'flex';
    navMenu.style.flexDirection = 'column';
    navMenu.style.position = 'absolute';
    navMenu.style.top = '70px';
    navMenu.style.left = '0';
    navMenu.style.width = '100%';
    navMenu.style.background = '#0a0d14';
    navMenu.style.padding = '1.5rem';
    navMenu.style.borderBottom = '1px solid rgba(255,255,255,0.1)';
  });
}

/* ==========================================================================
   8. Database Management Showcase Interactivity
   ========================================================================== */
function initDatabaseDemo() {
  const panel = document.getElementById('panel-database');
  if (!panel) return;

  panel.querySelectorAll('button.btn-filter').forEach(btn => {
    btn.addEventListener('click', () => {
      const text = btn.textContent.trim();
      if (text.includes('Integrity')) {
        alert('Database Integrity Status: HEALTHY\n\nIntegrity Check: ok\nForeign Key Check: ok\nValidated all schema tables & indexes.');
      } else if (text.includes('Flush WAL')) {
        alert('PRAGMA wal_checkpoint(TRUNCATE) executed:\n\n0 dirty pages remaining. WAL journal synchronized and truncated to 0 bytes.');
      } else if (text.includes('New Snapshot')) {
        const now = new Date();
        const dateStr = now.toISOString().replace(/[-:T.]/g, '').substring(0, 14);
        alert(`Live Online Hot Backup Generated:\n\nkestrelscope-backup-${dateStr}.db.gz\nFormat: Gzip Compressed SQLite (.db.gz)\nChecksum: 9f8a2bc471d8e93...\nStatus: Crash-consistent online snapshot ready for download.`);
      }
    });
  });
}

