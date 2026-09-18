/**
 * KestrelScope — Sovereign Observability Platform
 * Frontend Application Client
 */

// ============================================================================
// Auth & User Management
// ============================================================================
let currentUser = null;

async function checkAuth(isLoginPage = false) {
  try {
    const res = await fetch('/api/auth/me');
    const data = await res.json();
    currentUser = data;

    if (isLoginPage) {
      if (data.isAuthenticated) {
        window.location.href = '/dashboard.html';
      }
      return;
      return data;
    }

    if (!data.isAuthenticated) {
      window.location.href = '/login.html';
      return;
      return null;
    }

    const isAdmin = data.role === 'Admin';
    document.querySelectorAll('.nav-users-link').forEach(el => {
      el.style.display = isAdmin ? 'inline-flex' : 'none';
    });
    document.querySelectorAll('.nav-database-link').forEach(el => {
      el.style.display = isAdmin ? 'inline-flex' : 'none';
    });

    const landingUsersBtn = document.getElementById('landingUsersBtn');
    if (landingUsersBtn) {
      landingUsersBtn.style.display = isAdmin ? 'inline-flex' : 'none';
    }

    const landingDatabaseBtn = document.getElementById('landingDatabaseBtn');
    if (landingDatabaseBtn) {
      landingDatabaseBtn.style.display = isAdmin ? 'inline-flex' : 'none';
    }

    if (window.location.pathname.endsWith('/users.html') || window.location.pathname.endsWith('users.html') ||
        window.location.pathname.endsWith('/database.html') || window.location.pathname.endsWith('database.html')) {
      if (!isAdmin) {
        window.location.href = '/dashboard.html';
        return null;
      }
    }

    const avatarEl = document.getElementById('userAvatar');
    if (avatarEl && data.username) {
      avatarEl.textContent = data.username.substring(0, 2).toUpperCase();
      avatarEl.title = `Logged in as ${data.username}`;
      avatarEl.title = `Logged in as ${data.username} (${data.role || 'Standard'})`;
    }

    return data;
  } catch (err) {
    console.error('Auth verification failed', err);
    if (!isLoginPage) window.location.href = '/login.html';
    return null;
  }
}

async function handleLogout() {
  try {
    await fetch('/api/auth/logout', { method: 'POST' });
    window.location.href = '/login.html';
  } catch (err) {
    console.error('Logout error', err);
  }
}

async function handleLogin(username, password) {
  const errEl = document.getElementById('loginError');
  if (errEl) errEl.style.display = 'none';

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });

    if (res.ok) {
      window.location.href = '/dashboard.html';
    } else {
      const data = await res.json();
      if (errEl) {
        errEl.textContent = data.error || 'Authentication failed.';
        errEl.style.display = 'block';
      }
    }
  } catch (err) {
    if (errEl) {
      errEl.textContent = 'Server communication error.';
      errEl.style.display = 'block';
    }
  }
}

// ============================================================================
// Metrics Dashboard Controller
// ============================================================================
let metricsChart = null;

async function initDashboard() {
  await checkAuth();
  const user = await checkAuth();
  if (!user || !user.isAuthenticated) return;

  const serviceSelect = document.getElementById('serviceSelect');
  const metricSelect = document.getElementById('metricSelect');
  const windowSelect = document.getElementById('windowSelect');

  // Load KPI Stats
  async function loadStats() {
    try {
      const res = await fetch('/api/metrics/stats');
      if (res.ok) {
        const stats = await res.json();
        document.getElementById('statServices').textContent = stats.serviceCount;
        document.getElementById('statSamples').textContent = formatSampleCount(stats.sampleCount24h);
        document.getElementById('statAlerts').textContent = stats.activeAlerts;
      }
    } catch (err) {
      console.error('Failed to load stats', err);
    }
  }

  function formatSampleCount(num) {
    if (num >= 1_000_000) return (num / 1_000_000).toFixed(1) + 'M';
    if (num >= 1_000) return (num / 1_000).toFixed(1) + 'K';
    return num.toString();
  }

  // Load Services
  async function loadServices() {
    try {
      const res = await fetch('/api/metrics/services');
      const services = await res.json();
      serviceSelect.innerHTML = '';

      if (services.length === 0) {
        serviceSelect.innerHTML = '<option value="">No services detected</option>';
        metricSelect.innerHTML = '<option value="">No metrics available</option>';
        return;
      }

      services.forEach(s => {
        const opt = document.createElement('option');
        opt.value = s;
        opt.textContent = s;
        if (s === 'order-service') opt.selected = true;
        serviceSelect.appendChild(opt);
      });

      await loadMetrics();
    } catch (err) {
      console.error('Failed to load services', err);
    }
  }

  // Load Metrics for selected service
  async function loadMetrics() {
    const service = serviceSelect.value;
    if (!service) return;

    try {
      const res = await fetch(`/api/metrics/names?service=${encodeURIComponent(service)}`);
      const metrics = await res.json();
      metricSelect.innerHTML = '';

      if (metrics.length === 0) {
        metricSelect.innerHTML = '<option value="">No metrics available</option>';
        return;
      }

      metrics.forEach(m => {
        const opt = document.createElement('option');
        opt.value = m;
        opt.textContent = m;
        metricSelect.appendChild(opt);
      });

      await updateChart();
    } catch (err) {
      console.error('Failed to load metrics', err);
    }
  }

  // Fetch series and render Chart.js
  async function updateChart() {
    const service = serviceSelect.value;
    const metric = metricSelect.value;
    const minutes = parseInt(windowSelect.value, 10) || 60;

    if (!service || !metric) return;

    const windowText = windowSelect.options[windowSelect.selectedIndex]?.text || `Last ${minutes} Minutes`;

    try {
      const res = await fetch(`/api/metrics/series?service=${encodeURIComponent(service)}&metric=${encodeURIComponent(metric)}&minutes=${minutes}`);
      const series = await res.json();

      document.getElementById('chartTitle').textContent = `${metric} — ${service} (${windowText} • ${series.length} samples)`;

      if (!series || series.length === 0) {
        renderEmptyChart(metric);
        return;
      }

      // Uniformly bucket samples across the full selected time window [now - minutes, now]
      // so the chart's X-axis always accurately covers the entire requested time window.
      const now = Date.now();
      const startTime = now - minutes * 60 * 1000;
      const bucketCount = minutes <= 15 ? 30 : (minutes <= 60 ? 60 : 48);
      const bucketDuration = (now - startTime) / bucketCount;

      const buckets = [];
      for (let i = 0; i < bucketCount; i++) {
        const t = new Date(startTime + (i + 0.5) * bucketDuration);
        buckets.push({
          time: t,
          label: formatDate(t, minutes),
          values: []
        });
      }

      // Distribute series samples into buckets
      series.forEach(pt => {
        let d = new Date(pt.timestamp);
        if (isNaN(d.getTime())) {
          const cleaned = pt.timestamp.replace(/(\.\d{3})\d+Z$/, '$1Z');
          d = new Date(cleaned);
        }
        const timeMs = d.getTime();
        if (timeMs >= startTime && timeMs <= now) {
          const idx = Math.min(bucketCount - 1, Math.max(0, Math.floor((timeMs - startTime) / bucketDuration)));
          buckets[idx].values.push(pt.value);
        }
      });

      let labels = buckets.map(b => b.label);
      let values = buckets.map(b => b.values.length > 0
        ? Math.round((b.values.reduce((sum, v) => sum + v, 0) / b.values.length) * 100) / 100
        : null
      );

      // Fallback: If no samples fell inside the window buckets, use raw points
      if (values.every(v => v === null)) {
        labels = series.map(pt => {
          let d = new Date(pt.timestamp);
          if (isNaN(d.getTime())) {
            const cleaned = pt.timestamp.replace(/(\.\d{3})\d+Z$/, '$1Z');
            d = new Date(cleaned);
          }
          return formatDate(d, minutes);
        });
        values = series.map(pt => pt.value);
      }

      renderChart(labels, values, metric);
    } catch (err) {
      console.error('Failed to fetch metric series', err);
    }
  }

  function formatDate(d, minutes) {
    if (minutes > 360) {
      return d.toLocaleDateString([], { month: 'short', day: 'numeric' }) + ' ' +
             d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }
    if (minutes >= 60) {
      return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  }

  function renderChart(labels, values, metricName) {
    const canvas = document.getElementById('metricsCanvas');
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const gradient = ctx.createLinearGradient(0, 0, 0, 350);
    gradient.addColorStop(0, 'rgba(40, 134, 222, 0.32)');
    gradient.addColorStop(1, 'rgba(40, 134, 222, 0.0)');

    if (metricsChart) {
      metricsChart.data.labels = labels;
      metricsChart.data.datasets[0].data = values;
      metricsChart.data.datasets[0].label = metricName;
      metricsChart.data.datasets[0].spanGaps = true;
      metricsChart.update();
      return;
    }

    metricsChart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: labels,
        datasets: [{
          label: metricName,
          data: values,
          spanGaps: true,
          borderColor: '#2886de',
          borderWidth: 2,
          backgroundColor: gradient,
          fill: true,
          tension: 0.35,
          pointBackgroundColor: '#2886de',
          pointBorderColor: '#141414',
          pointBorderWidth: 2,
          pointRadius: 4,
          pointHoverRadius: 6,
          pointHoverBackgroundColor: '#479ef5'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: '#242424',
            titleColor: '#ffffff',
            bodyColor: '#479ef5',
            borderColor: 'rgba(255, 255, 255, 0.12)',
            borderWidth: 1,
            padding: 10,
            displayColors: false,
            titleFont: {
              family: "'Segoe UI Variable Text', 'Segoe UI', -apple-system, sans-serif",
              weight: '600'
            },
            bodyFont: {
              family: "'Segoe UI Variable Text', 'Segoe UI', -apple-system, sans-serif"
            },
            callbacks: {
              label: function(item) {
                if (item.raw === null || item.raw === undefined) return '';
                const val = typeof item.raw === 'number' ? item.raw.toFixed(2) : item.raw;
                return `${item.dataset.label}: ${val}`;
              }
            }
          }
        },
        scales: {
          x: {
            grid: { color: 'rgba(255, 255, 255, 0.06)' },
            ticks: {
              color: '#9e9e9e',
              font: {
                family: "'Segoe UI Variable Text', 'Segoe UI', -apple-system, sans-serif",
                size: 11
              },
              maxTicksLimit: 10
            }
          },
          y: {
            grid: { color: 'rgba(255, 255, 255, 0.06)' },
            ticks: {
              color: '#9e9e9e',
              font: {
                family: "'Segoe UI Variable Text', 'Segoe UI', -apple-system, sans-serif",
                size: 11
              }
            },
            beginAtZero: true
          }
        }
      }
    });
  }

  function renderEmptyChart(metricName) {
    if (metricsChart) {
      metricsChart.data.labels = ['No Data in Selected Window'];
      metricsChart.data.datasets[0].data = [0];
      metricsChart.data.datasets[0].label = metricName;
      metricsChart.update();
    }
  }

  // Event Listeners
  serviceSelect.addEventListener('change', loadMetrics);
  metricSelect.addEventListener('change', updateChart);
  windowSelect.addEventListener('change', updateChart);

  // Initialize
  await loadStats();
  await loadServices();

  // Periodic Refresh
  setInterval(() => {
    loadStats();
    updateChart();
  }, 10000);
}

// ============================================================================
// Traces Explorer Controller
// ============================================================================
async function initTraces() {
  const user = await checkAuth();
  if (!user || !user.isAuthenticated) return;

  const tbody = document.getElementById('traceTableBody');
  const searchInput = document.getElementById('traceSearch');
  const timelineSpans = document.getElementById('timelineSpans');
  const timelineTitle = document.getElementById('timelineTitle');

  const urlParams = new URLSearchParams(window.location.search);
  const paramTraceId = urlParams.get('traceId');

  let tracesData = [];
  let activeTraceId = null;

  async function loadTraces() {
    try {
      const res = await fetch('/api/traces?minutes=120&limit=100');
      tracesData = await res.json();

      // If a specific trace was requested (e.g. clicked from logs) but is not in the recent list,
      // fetch it directly and prepend it so it appears in the Trace List
      if (paramTraceId && !tracesData.some(t => t.traceId === paramTraceId)) {
        try {
          const specificRes = await fetch(`/api/traces/${encodeURIComponent(paramTraceId)}`);
          if (specificRes.ok) {
            const specificSpans = await specificRes.json();
            if (specificSpans && specificSpans.length > 0) {
              const root = specificSpans.find(s => !s.parentSpanId) || specificSpans[0];
              tracesData.unshift(root);
            }
          }
        } catch (e) {
          console.warn('Could not fetch correlated trace details', e);
        }
      }

      renderTraceTable(tracesData);

      const targetTraceId = paramTraceId || (tracesData.length > 0 ? tracesData[0].traceId : null);
      if (targetTraceId) {
        // When opening from correlated logs, center the selected trace in view!
        selectTrace(targetTraceId, paramTraceId ? 'center' : null);
      }
    } catch (err) {
      console.error('Failed to load traces', err);
    }
  }

  function renderTraceTable(spans) {
    tbody.innerHTML = '';
    if (spans.length === 0) {
      tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;color:#64748b;padding:2rem;">No traces recorded yet</td></tr>';
      return;
    }

    spans.forEach(span => {
      const tr = document.createElement('tr');
      tr.dataset.traceId = span.traceId;
      if (span.parentSpanId == null) {
        tr.dataset.isRoot = 'true';
      }

      const isCurrent = activeTraceId 
        ? (activeTraceId === span.traceId) 
        : (paramTraceId === span.traceId);

      if (isCurrent) {
        tr.classList.add('selected');
      }

      let statusBadge = '<span class="badge badge-ok">200 OK</span>';
      if (span.statusCode === 'Error' || span.statusCode === '2') {
        statusBadge = '<span class="badge badge-error">500 Error</span>';
      } else if (span.statusCode === '404') {
        statusBadge = '<span class="badge badge-warn">404</span>';
      }

      // Infer HTTP method if name starts with GET/POST
      let method = 'GET';
      let endpoint = span.spanName;
      if (span.spanName.startsWith('POST')) { method = 'POST'; endpoint = span.spanName.substring(5); }
      else if (span.spanName.startsWith('GET')) { method = 'GET'; endpoint = span.spanName.substring(4); }
      else if (span.spanName.startsWith('PUT')) { method = 'PUT'; endpoint = span.spanName.substring(4); }
      else if (span.spanName.startsWith('DELETE')) { method = 'DELETE'; endpoint = span.spanName.substring(7); }

      tr.innerHTML = `
        <td><a href="javascript:void(0)" class="trace-id-link">${span.traceId.substring(0, 8)}</a></td>
        <td><strong>${method}</strong></td>
        <td>${endpoint}</td>
        <td>${statusBadge}</td>
        <td>${span.durationMs.toFixed(1)}ms</td>
      `;

      tr.addEventListener('click', () => {
        selectTrace(span.traceId, 'nearest');
      });

      tbody.appendChild(tr);
    });
  }

  async function selectTrace(traceId, scrollBlock = null) {
    activeTraceId = traceId;
    timelineTitle.textContent = `Trace Timeline: ${traceId.substring(0, 8)}`;
    const traceActions = document.getElementById('traceActions');
    if (traceActions) {
      traceActions.innerHTML = `<a href="/logs.html?traceId=${encodeURIComponent(traceId)}" class="trace-pill" style="font-size:0.8rem; padding:0.35rem 0.75rem;">📜 View Correlated Logs</a>`;
    }

    // Highlight matching row(s) in the table
    let targetRow = null;
    document.querySelectorAll('.trace-table tbody tr').forEach(r => {
      if (r.dataset.traceId === traceId) {
        r.classList.add('selected');
        if (!targetRow || r.dataset.isRoot === 'true') {
          targetRow = r;
        }
      } else {
        r.classList.remove('selected');
      }
    });

    // Scroll selected trace into view in the trace list
    if (targetRow && scrollBlock) {
      requestAnimationFrame(() => {
        targetRow.scrollIntoView({ behavior: 'smooth', block: scrollBlock });
      });
      setTimeout(() => {
        targetRow.scrollIntoView({ behavior: 'smooth', block: scrollBlock });
      }, 100);
    }

    timelineSpans.innerHTML = '<div style="color:#94a3b8;padding:1rem;">Loading span waterfall...</div>';

    try {
      const res = await fetch(`/api/traces/${traceId}`);
      const spans = await res.json();
      renderWaterfall(spans);
    } catch (err) {
      console.error('Failed to load trace spans', err);
    }
  }

  function renderWaterfall(spans) {
    timelineSpans.innerHTML = '';
    if (spans.length === 0) {
      timelineSpans.innerHTML = '<div style="color:#64748b;padding:1rem;">No span details found.</div>';
      return;
    }

    // Determine span tree hierarchy
    const spanMap = new Map();
    spans.forEach(s => spanMap.set(s.spanId, { ...s, children: [] }));

    let rootSpans = [];
    spans.forEach(s => {
      const node = spanMap.get(s.spanId);
      if (s.parentSpanId && spanMap.has(s.parentSpanId)) {
        spanMap.get(s.parentSpanId).children.push(node);
      } else {
        rootSpans.push(node);
      }
    });

    // Flatten tree in execution order with depth levels
    const ordered = [];
    function traverse(node, depth) {
      ordered.push({ ...node, depth });
      node.children.forEach(child => traverse(child, depth + 1));
    }
    rootSpans.forEach(root => traverse(root, 0));

    // Render waterfall bars
    ordered.forEach(span => {
      const row = document.createElement('div');
      row.className = 'waterfall-row';

      const levelClass = `level-${Math.min(span.depth, 3)}`;
      const isError = span.statusCode === 'Error' || span.statusCode === '2';
      const errorClass = isError ? 'has-error' : '';

      const indent = span.depth > 0 ? `style="margin-left: ${span.depth * 28}px"` : '';

      row.innerHTML = `
        <div class="waterfall-bar ${levelClass} ${errorClass}" ${indent}>
          <div class="waterfall-info">
            ${span.depth > 0 ? '<span style="color:var(--text-muted)">↳</span>' : ''}
            <span><strong>${span.serviceName}</strong>: ${span.spanName}</span>
          </div>
          <div class="waterfall-duration">
            ${span.durationMs.toFixed(1)}ms
          </div>
        </div>
      `;

      timelineSpans.appendChild(row);
    });
  }

  // Search Filter
  searchInput.addEventListener('input', (e) => {
    const q = e.target.value.toLowerCase();
    const filtered = tracesData.filter(s => 
      s.traceId.toLowerCase().includes(q) ||
      s.spanName.toLowerCase().includes(q) ||
      s.serviceName.toLowerCase().includes(q)
    );
    renderTraceTable(filtered);
  });

  await loadTraces();
}

// ============================================================================
// Alerts Manager Controller
// ============================================================================
async function initAlerts() {
  const user = await checkAuth();
  if (!user || !user.isAuthenticated) return;
  const isAdmin = user.role === 'Admin';

  const alertGrid = document.getElementById('alertGrid');
  const modal = document.getElementById('alertModal');
  const openBtn = document.getElementById('btnNewAlert');
  const cancelBtn = document.getElementById('btnCancelModal');
  const alertForm = document.getElementById('alertForm');
  const readOnlyBanner = document.getElementById('standardUserAlertNotice');

  if (!isAdmin) {
    if (openBtn) openBtn.style.display = 'none';
    if (readOnlyBanner) readOnlyBanner.style.display = 'flex';
  }

  async function loadAlerts() {
    try {
      const res = await fetch('/api/alerts');
      const rules = await res.json();
      renderAlerts(rules);
    } catch (err) {
      console.error('Failed to load alert rules', err);
    }
  }

  function renderAlerts(rules) {
    alertGrid.innerHTML = '';
    if (rules.length === 0) {
      const emptyText = isAdmin 
        ? 'No alert rules defined. Click "New Alert" to configure one.' 
        : 'No alert rules defined.';
      alertGrid.innerHTML = `<div style="grid-column: 1/-1; text-align:center; color:#64748b; padding:3rem;">${emptyText}</div>`;
      return;
    }

    rules.forEach(rule => {
      const card = document.createElement('div');
      const isActive = rule.isEnabled === 1;
      card.className = `alert-card ${isActive ? 'active-rule' : 'disabled-rule'}`;

      const badge = isActive
        ? '<span class="badge badge-ok">Active</span>'
        : '<span class="badge badge-gray">Disabled</span>';

      let actionsHtml = '';
      if (isAdmin) {
        actionsHtml = `
          <div class="alert-card-actions">
            <button class="btn-secondary btn-sm" onclick="toggleAlert(${rule.id})">
              ${isActive ? 'Disable' : 'Enable'}
            </button>
            <button class="btn-secondary btn-sm" style="border-color: rgba(239,68,68,0.4); color: #fca5a5;" onclick="deleteAlert(${rule.id})">
              Delete
            </button>
          </div>
        `;
      } else {
        actionsHtml = `
          <div class="alert-card-actions" style="justify-content: flex-end;">
            <span style="font-size: 0.78rem; color: var(--fui-colorNeutralForeground4); font-style: italic;">
              Admin access required to modify
            </span>
          </div>
        `;
      }

      card.innerHTML = `
        <div class="alert-card-header">
          <div>
            <div class="alert-card-title">${rule.name}</div>
            <div class="alert-card-metric">${rule.metricName}</div>
          </div>
          ${badge}
        </div>
        <div class="alert-card-details">
          <div>Threshold: <strong>&gt; ${rule.threshold}</strong></div>
          <div>Evaluation Window: <strong>${rule.windowMinutes} min</strong></div>
          <div style="word-break: break-all; margin-top: 0.35rem; color: #64748b; font-size: 0.78rem;">${rule.webhookUrl}</div>
        </div>
        ${actionsHtml}
      `;

      alertGrid.appendChild(card);
    });
  }

  // Modal handlers (admin only)
  if (openBtn) openBtn.addEventListener('click', () => modal.classList.add('open'));
  if (cancelBtn) cancelBtn.addEventListener('click', () => modal.classList.remove('open'));
  if (modal) {
    modal.addEventListener('click', (e) => {
      if (e.target === modal) modal.classList.remove('open');
    });
  }

  if (alertForm) {
    alertForm.addEventListener('submit', async (e) => {
      e.preventDefault();

      const newRule = {
        name: document.getElementById('ruleName').value,
        metricName: document.getElementById('metricName').value,
        threshold: parseFloat(document.getElementById('threshold').value),
        windowMinutes: parseInt(document.getElementById('windowMinutes').value, 10),
        webhookUrl: document.getElementById('webhookUrl').value,
        isEnabled: 1
      };

      try {
        const res = await fetch('/api/alerts', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(newRule)
        });

        if (res.ok) {
          modal.classList.remove('open');
          alertForm.reset();
          await loadAlerts();
        } else {
          const errData = await res.json();
          alert(errData.error || 'Failed to create alert rule.');
        }
      } catch (err) {
        console.error('Failed to create alert rule', err);
      }
    });
  }

  window.toggleAlert = async (id) => {
    try {
      const res = await fetch(`/api/alerts/${id}/toggle`, { method: 'PATCH' });
      if (!res.ok) {
        const errData = await res.json();
        alert(errData.error || 'Failed to toggle alert rule.');
        return;
      }
      await loadAlerts();
    } catch (err) {
      console.error('Failed to toggle alert', err);
    }
  };

  window.deleteAlert = async (id) => {
    if (!confirm('Are you sure you want to delete this alert rule?')) return;
    try {
      const res = await fetch(`/api/alerts/${id}`, { method: 'DELETE' });
      if (!res.ok) {
        const errData = await res.json();
        alert(errData.error || 'Failed to delete alert rule.');
        return;
      }
      await loadAlerts();
    } catch (err) {
      console.error('Failed to delete alert', err);
    }
  };

  await loadAlerts();
}

// ============================================================================
// Logs Explorer Controller
// ============================================================================
let logTimeMinutes = 60;
let logSearchDebounceTimer = null;

async function initLogs() {
  const user = await checkAuth();
  if (!user || !user.isAuthenticated) return;

  const serviceSelect = document.getElementById('logServiceSelect');
  const traceInput = document.getElementById('logTraceInput');

  // Check URL parameters (e.g. from trace click: /logs.html?traceId=...)
  const urlParams = new URLSearchParams(window.location.search);
  if (urlParams.has('traceId')) {
    traceInput.value = urlParams.get('traceId');
  }

  // Load log services
  try {
    const res = await fetch('/api/logs/services');
    const services = await res.json();
    services.forEach(s => {
      const opt = document.createElement('option');
      opt.value = s;
      opt.textContent = s;
      if (urlParams.get('service') === s) opt.selected = true;
      serviceSelect.appendChild(opt);
    });
  } catch (err) {
    console.error('Failed to load log services', err);
  }

  await loadLogs();

  // Auto-refresh every 10 seconds
  setInterval(loadLogs, 10000);
}

function setLogTimeWindow(minutes) {
  logTimeMinutes = minutes;
  document.querySelectorAll('.time-btn').forEach(btn => {
    btn.classList.toggle('active', parseInt(btn.dataset.minutes) === minutes);
  });
  loadLogs();
}

function debounceLogSearch() {
  clearTimeout(logSearchDebounceTimer);
  logSearchDebounceTimer = setTimeout(loadLogs, 350);
}

async function loadLogs() {
  const service = document.getElementById('logServiceSelect')?.value || '';
  const severity = document.getElementById('logSeveritySelect')?.value || '';
  const query = document.getElementById('logSearchInput')?.value || '';
  const traceId = document.getElementById('logTraceInput')?.value || '';
  const tbody = document.getElementById('logsTableBody');

  try {
    const params = new URLSearchParams({
      minutes: logTimeMinutes,
      limit: 150
    });
    if (service) params.append('service', service);
    if (severity) params.append('severity', severity);
    if (query) params.append('query', query);
    if (traceId) params.append('traceId', traceId);

    const res = await fetch(`/api/logs?${params.toString()}`);
    const logs = await res.json();

    // Update Stats
    let total = logs.length;
    let errors = 0;
    let warns = 0;
    let infos = 0;

    logs.forEach(l => {
      const sev = (l.severityText || '').toUpperCase();
      if (sev === 'ERROR' || sev === 'FATAL') errors++;
      else if (sev === 'WARN') warns++;
      else infos++;
    });

    const statTotal = document.getElementById('statTotalLogs');
    const statError = document.getElementById('statErrorLogs');
    const statWarn = document.getElementById('statWarnLogs');
    const statInfo = document.getElementById('statInfoLogs');
    if (statTotal) statTotal.textContent = total;
    if (statError) statError.textContent = errors;
    if (statWarn) statWarn.textContent = warns;
    if (statInfo) statInfo.textContent = infos;

    // Render Table
    if (!tbody) return;
    tbody.innerHTML = '';

    if (logs.length === 0) {
      tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;color:#64748b;padding:2.5rem;">No log events matched the current filters</td></tr>';
      return;
    }

    logs.forEach(log => {
      const tr = document.createElement('tr');
      const sev = (log.severityText || 'INFO').toUpperCase();
      let badgeClass = 'log-badge-info';
      if (sev === 'ERROR' || sev === 'FATAL') badgeClass = 'log-badge-error';
      else if (sev === 'WARN') badgeClass = 'log-badge-warn';
      else if (sev === 'DEBUG' || sev === 'TRACE') badgeClass = 'log-badge-debug';

      const d = new Date(log.timestamp);
      const timeStr = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) + 
        '.' + String(d.getMilliseconds()).padStart(3, '0');

      let traceCol = '<span style="color:#64748b; font-size:0.75rem;">—</span>';
      if (log.traceId) {
        traceCol = `<a href="/traces.html?traceId=${encodeURIComponent(log.traceId)}" class="trace-pill" title="Click to view full trace in Traces Explorer">🔍 ${log.traceId.substring(0, 8)}...</a>`;
      }

      let attrsHtml = '';
      if (log.attributesJson && log.attributesJson !== '{}') {
        try {
          const parsed = JSON.parse(log.attributesJson);
          if (Array.isArray(parsed) && parsed.length > 0) {
            const attrPairs = parsed.map(a => `${a.key}=${JSON.stringify(a.value?.stringValue ?? a.value)}`).join('  ');
            attrsHtml = `<div class="log-attrs-box">${escapeHtml(attrPairs)}</div>`;
          } else if (typeof parsed === 'object' && Object.keys(parsed).length > 0) {
            attrsHtml = `<div class="log-attrs-box">${escapeHtml(JSON.stringify(parsed))}</div>`;
          }
        } catch {
          attrsHtml = `<div class="log-attrs-box">${escapeHtml(log.attributesJson)}</div>`;
        }
      }

      tr.innerHTML = `
        <td class="log-time-cell">${timeStr}</td>
        <td class="log-sev-cell"><span class="log-badge ${badgeClass}">${sev}</span></td>
        <td class="log-svc-cell"><span class="log-svc-name">${escapeHtml(log.serviceName)}</span></td>
        <td class="log-body-cell"><div class="log-msg-text">${escapeHtml(log.body)}</div>${attrsHtml}</td>
        <td class="log-trace-cell">${traceCol}</td>
      `;

      tbody.appendChild(tr);
    });
  } catch (err) {
    console.error('Failed to load logs', err);
  }
}

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

// ============================================================================
// Users Management Controller
// ============================================================================
async function initUsers() {
  const user = await checkAuth();
  if (!user || !user.isAuthenticated) return;
  if (user.role !== 'Admin') {
    window.location.href = '/dashboard.html';
    return;
  }

  const tableBody = document.getElementById('usersTableBody');
  const modal = document.getElementById('userModal');
  const modalTitle = document.getElementById('userModalTitle');
  const openBtn = document.getElementById('btnNewUser');
  const cancelBtn = document.getElementById('btnCancelUserModal');
  const userForm = document.getElementById('userForm');
  const userIdInput = document.getElementById('userId');
  const usernameInput = document.getElementById('userUsername');
  const roleSelect = document.getElementById('userRole');
  const passwordInput = document.getElementById('userPassword');
  const passwordHelp = document.getElementById('passwordHelp');
  const userFormError = document.getElementById('userFormError');

  let allUsers = [];

  async function loadUsers() {
    try {
      const res = await fetch('/api/users');
      if (!res.ok) {
        if (res.status === 403) {
          window.location.href = '/dashboard.html';
          return;
        }
        throw new Error('Failed to fetch users');
      }
      allUsers = await res.json();
      renderUsers(allUsers);
    } catch (err) {
      console.error('Failed to load users', err);
      if (tableBody) {
        tableBody.innerHTML = `<tr><td colspan="5" style="text-align:center; color:#ef4444; padding:2rem;">Error loading users: ${escapeHtml(err.message)}</td></tr>`;
      }
    }
  }

  function renderUsers(users) {
    if (!tableBody) return;
    tableBody.innerHTML = '';
    if (users.length === 0) {
      tableBody.innerHTML = '<tr><td colspan="5" style="text-align:center; color:#64748b; padding:2rem;">No users found.</td></tr>';
      return;
    }

    users.forEach(u => {
      const tr = document.createElement('tr');
      const isAdmin = String(u.role).toLowerCase() === 'admin';
      const roleBadge = isAdmin
        ? '<span class="badge" style="background:rgba(15,108,189,0.2); color:#479ef5; border:1px solid rgba(15,108,189,0.5);">Admin</span>'
        : '<span class="badge badge-gray">Standard</span>';

      const isCurrentLoggedInUser = u.username.toLowerCase() === (user?.username || '').toLowerCase();
      const createdAtStr = u.createdAt ? new Date(u.createdAt).toLocaleDateString() : '—';

      const deleteBtnDisabled = isCurrentLoggedInUser ? 'disabled title="You cannot delete your own account"' : '';

      tr.innerHTML = `
        <td style="font-weight:600; color:var(--fui-colorNeutralForeground1);">${escapeHtml(u.username)} ${isCurrentLoggedInUser ? '<span style="font-size:0.75rem; color:#479ef5; font-weight:normal;">(You)</span>' : ''}</td>
        <td>${roleBadge}</td>
        <td style="color:var(--fui-colorNeutralForeground3); font-size:0.85rem;">${createdAtStr}</td>
        <td style="font-family:var(--fui-fontFamilyMonospace); font-size:0.8rem; color:var(--fui-colorNeutralForeground4);">#${u.id}</td>
        <td style="text-align:right;">
          <button class="btn-secondary btn-sm" onclick="editUser(${u.id})">Edit</button>
          <button class="btn-secondary btn-sm" style="border-color: rgba(239,68,68,0.4); color: #fca5a5; margin-left: 0.35rem;" onclick="deleteUser(${u.id}, '${escapeHtml(u.username)}')" ${deleteBtnDisabled}>
            Delete
          </button>
        </td>
      `;
      tableBody.appendChild(tr);
    });
  }

  if (openBtn) {
    openBtn.addEventListener('click', () => {
      if (userFormError) userFormError.style.display = 'none';
      if (userForm) userForm.reset();
      if (userIdInput) userIdInput.value = '';
      if (usernameInput) {
        usernameInput.disabled = false;
        usernameInput.focus();
      }
      if (modalTitle) modalTitle.textContent = 'Create New User';
      if (passwordInput) passwordInput.required = true;
      if (passwordHelp) passwordHelp.textContent = 'Minimum 4 characters recommended.';
      if (modal) modal.classList.add('open');
    });
  }

  if (cancelBtn && modal) {
    cancelBtn.addEventListener('click', () => modal.classList.remove('open'));
  }
  if (modal) {
    modal.addEventListener('click', (e) => {
      if (e.target === modal) modal.classList.remove('open');
    });
  }

  window.editUser = (id) => {
    const target = allUsers.find(u => u.id === id);
    if (!target) return;

    if (userFormError) userFormError.style.display = 'none';
    if (userIdInput) userIdInput.value = target.id;
    if (usernameInput) {
      usernameInput.value = target.username;
      usernameInput.disabled = true;
    }
    if (roleSelect) roleSelect.value = String(target.role).toLowerCase() === 'admin' ? 'admin' : 'standard';
    if (passwordInput) {
      passwordInput.value = '';
      passwordInput.required = false;
    }
    if (passwordHelp) passwordHelp.textContent = 'Leave password blank to keep current password.';
    if (modalTitle) modalTitle.textContent = `Edit User (${target.username})`;
    if (modal) modal.classList.add('open');
  };

  window.deleteUser = async (id, username) => {
    if (!confirm(`Are you sure you want to permanently delete user '${username}'?`)) return;

    try {
      const res = await fetch(`/api/users/${id}`, { method: 'DELETE' });
      const data = await res.json();
      if (!res.ok) {
        alert(data.error || 'Failed to delete user.');
        return;
      }
      await loadUsers();
    } catch (err) {
      alert('Network error while deleting user.');
    }
  };

  if (userForm) {
    userForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      if (userFormError) userFormError.style.display = 'none';

      const id = userIdInput?.value;
      const role = roleSelect?.value || 'standard';
      const password = passwordInput?.value;

      try {
        if (!id) {
          const username = usernameInput?.value.trim();
          const res = await fetch('/api/users', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username, password, role })
          });
          const data = await res.json();
          if (!res.ok) {
            if (userFormError) {
              userFormError.textContent = data.error || 'Failed to create user.';
              userFormError.style.display = 'block';
            }
            return;
          }
        } else {
          const body = { role };
          if (password) body.password = password;
          const res = await fetch(`/api/users/${id}`, {
            method: 'PATCH',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
          });
          const data = await res.json();
          if (!res.ok) {
            if (userFormError) {
              userFormError.textContent = data.error || 'Failed to update user.';
              userFormError.style.display = 'block';
            }
            return;
          }
        }

        if (modal) modal.classList.remove('open');
        await loadUsers();
      } catch (err) {
        if (userFormError) {
          userFormError.textContent = 'Communication error with server.';
          userFormError.style.display = 'block';
        }
      }
    });
  }

  await loadUsers();
}

// ============================================================================
// Application Flow Map (AppDynamics Style)
// ============================================================================
let flowMapState = {
  data: null,
  zoom: 1.0,
  panX: 0,
  panY: 0,
  isPanning: false,
  panStartX: 0,
  panStartY: 0,
  draggedNode: null,
  dragStartMouseX: 0,
  dragStartMouseY: 0,
  dragStartNodeX: 0,
  dragStartNodeY: 0,
  selectedNode: null,
  autoRefreshTimer: null,
  charts: {
    load: null,
    latency: null,
    errors: null
  }
};

async function initFlowMap() {
  const svg = document.getElementById('flowmapSvg');
  const viewport = document.getElementById('flowmapViewport');
  const edgesLayer = document.getElementById('flowmapEdgesLayer');
  const nodesLayer = document.getElementById('flowmapNodesLayer');
  if (!svg || !viewport || !edgesLayer || !nodesLayer) return;

  const appSelect = document.getElementById('appSelect');
  const timeWindowSelect = document.getElementById('timeWindowSelect');
  const autoRefreshToggle = document.getElementById('autoRefreshToggle');
  const btnRefresh = document.getElementById('btnRefreshFlowMap');
  const btnResetPositions = document.getElementById('btnResetPositions');
  const btnResetLayout = document.getElementById('btnResetLayout');
  const btnZoomIn = document.getElementById('btnZoomIn');
  const btnZoomOut = document.getElementById('btnZoomOut');
  const nodeDrawer = document.getElementById('nodeDrawer');
  const btnDrawerClose = document.getElementById('btnDrawerClose');

  const LAYOUT_CACHE_KEY_PREFIX = 'kestrelscope_flowmap_layout_';

  function getLayoutCacheKey() {
    const app = (appSelect ? appSelect.value : 'ECommerce') || 'ECommerce';
    return `${LAYOUT_CACHE_KEY_PREFIX}${app}`;
  }

  function saveNodeLayout() {
    if (!flowMapState.data || !Array.isArray(flowMapState.data.nodes)) return;
    const cacheKey = getLayoutCacheKey();
    const layout = {};
    flowMapState.data.nodes.forEach(n => {
      layout[n.id] = { x: n.x, y: n.y };
    });
    try {
      sessionStorage.setItem(cacheKey, JSON.stringify(layout));
      localStorage.setItem(cacheKey, JSON.stringify(layout));
    } catch (e) {
      console.warn('Failed to save node layout to session cache', e);
    }
    updateResetButtonState();
  }

  function getSavedNodeLayout() {
    const cacheKey = getLayoutCacheKey();
    try {
      const raw = sessionStorage.getItem(cacheKey) || localStorage.getItem(cacheKey);
      return raw ? JSON.parse(raw) : null;
    } catch (e) {
      return null;
    }
  }

  function clearSavedNodeLayout() {
    const cacheKey = getLayoutCacheKey();
    try {
      sessionStorage.removeItem(cacheKey);
      localStorage.removeItem(cacheKey);
    } catch (e) {}
    updateResetButtonState();
  }

  function updateResetButtonState() {
    if (!btnResetPositions) return;
    const saved = getSavedNodeLayout();
    btnResetPositions.style.display = (saved && Object.keys(saved).length > 0) ? 'inline-flex' : 'none';
  }

  function updateViewport() {
    viewport.setAttribute('transform', `translate(${flowMapState.panX}, ${flowMapState.panY}) scale(${flowMapState.zoom})`);
  }

  function screenToSvg(clientX, clientY) {
    const pt = svg.createSVGPoint();
    pt.x = clientX;
    pt.y = clientY;
    const globalPt = pt.matrixTransform(svg.getScreenCTM().inverse());
    // Convert to viewport coordinates (accounting for pan and zoom)
    return {
      x: (globalPt.x - flowMapState.panX) / flowMapState.zoom,
      y: (globalPt.y - flowMapState.panY) / flowMapState.zoom
    };
  }

  // --- Canvas Pan & Zoom Events ---
  svg.addEventListener('mousedown', (e) => {
    // Only pan if clicking canvas background or grid
    if (e.target === svg || e.target.tagName === 'rect') {
      flowMapState.isPanning = true;
      flowMapState.panStartX = e.clientX - flowMapState.panX;
      flowMapState.panStartY = e.clientY - flowMapState.panY;
      svg.classList.add('grabbing');
    }
  });

  window.addEventListener('mousemove', (e) => {
    if (flowMapState.isPanning) {
      flowMapState.panX = e.clientX - flowMapState.panStartX;
      flowMapState.panY = e.clientY - flowMapState.panStartY;
      updateViewport();
      return;
    }

    if (flowMapState.draggedNode) {
      const coords = screenToSvg(e.clientX, e.clientY);
      flowMapState.draggedNode.x = Math.round(coords.x - flowMapState.dragOffsetX);
      flowMapState.draggedNode.y = Math.round(coords.y - flowMapState.dragOffsetY);

      // Update node position in DOM
      const nodeEl = document.querySelector(`.topo-node[data-id="${CSS.escape(flowMapState.draggedNode.id)}"]`);
      if (nodeEl) {
        nodeEl.setAttribute('transform', `translate(${flowMapState.draggedNode.x}, ${flowMapState.draggedNode.y})`);
      }

      // Re-render edges connected to this node
      renderEdges(flowMapState.data.edges, flowMapState.data.nodes);
    }
  });

  window.addEventListener('mouseup', () => {
    if (flowMapState.isPanning) {
      flowMapState.isPanning = false;
      svg.classList.remove('grabbing');
    }
    if (flowMapState.draggedNode) {
      saveNodeLayout();
      flowMapState.draggedNode = null;
    }
  });

  svg.addEventListener('wheel', (e) => {
    e.preventDefault();
    const zoomFactor = e.deltaY < 0 ? 1.08 : 0.92;
    const newZoom = Math.max(0.4, Math.min(2.5, flowMapState.zoom * zoomFactor));

    // Zoom centered around mouse pointer
    const pt = svg.createSVGPoint();
    pt.x = e.clientX;
    pt.y = e.clientY;
    const globalPt = pt.matrixTransform(svg.getScreenCTM().inverse());

    flowMapState.panX = globalPt.x - (globalPt.x - flowMapState.panX) * (newZoom / flowMapState.zoom);
    flowMapState.panY = globalPt.y - (globalPt.y - flowMapState.panY) * (newZoom / flowMapState.zoom);
    flowMapState.zoom = newZoom;
    updateViewport();
  }, { passive: false });

  if (btnZoomIn) {
    btnZoomIn.addEventListener('click', () => {
      flowMapState.zoom = Math.min(2.5, flowMapState.zoom * 1.15);
      updateViewport();
    });
  }

  if (btnZoomOut) {
    btnZoomOut.addEventListener('click', () => {
      flowMapState.zoom = Math.max(0.4, flowMapState.zoom / 1.15);
      updateViewport();
    });
  }

  if (btnResetPositions) {
    btnResetPositions.addEventListener('click', () => {
      clearSavedNodeLayout();
      loadFlowMap();
    });
  }

  if (btnResetLayout) {
    btnResetLayout.addEventListener('click', () => {
      flowMapState.zoom = 1.0;
      flowMapState.panX = 0;
      flowMapState.panY = 0;
      updateViewport();
    });
  }

  if (btnDrawerClose && nodeDrawer) {
    btnDrawerClose.addEventListener('click', () => {
      nodeDrawer.classList.remove('open');
    });
  }

  // --- Edge Rendering ---
  function renderEdges(edges, nodes) {
    edgesLayer.innerHTML = '';
    const nodeMap = {};
    nodes.forEach(n => { nodeMap[n.id] = n; });

    edges.forEach(edge => {
      const src = nodeMap[edge.source];
      const tgt = nodeMap[edge.target];
      if (!src || !tgt) return;

      const dx = tgt.x - src.x;
      const dy = tgt.y - src.y;
      const angle = Math.atan2(dy, dx);
      const dist = Math.hypot(dx, dy);

      // Node boundary radii
      const srcRadius = src.type === 'service' ? 44 : (src.type === 'database' ? 36 : 30);
      const tgtRadius = tgt.type === 'service' ? 44 : (tgt.type === 'database' ? 36 : 30);

      const startX = src.x + Math.cos(angle) * srcRadius;
      const startY = src.y + Math.sin(angle) * srcRadius;
      const endX = tgt.x - Math.cos(angle) * (tgtRadius + 8);
      const endY = tgt.y - Math.sin(angle) * (tgtRadius + 8);

      const marker = edge.health === 'critical' ? 'arrowCritical' 
                   : edge.health === 'warning' ? 'arrowWarning' 
                   : 'arrowNormal';

      const edgeG = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      edgeG.setAttribute('class', 'topo-edge');

      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', startX);
      line.setAttribute('y1', startY);
      line.setAttribute('x2', endX);
      line.setAttribute('y2', endY);
      line.setAttribute('class', `topo-edge-line ${edge.isAsync ? 'async-jms' : ''}`);
      line.setAttribute('marker-end', `url(#${marker})`);

      edgeG.appendChild(line);

      // Midpoint badge
      const midX = (startX + endX) / 2;
      const midY = (startY + endY) / 2;
      const labelText = `${edge.protocol} ${edge.callsPerMin} cpm${edge.avgLatencyMs > 0 ? ', ' + edge.avgLatencyMs + ' ms' : ''}`;
      const badgeWidth = Math.max(80, labelText.length * 6.5 + 14);

      const badgeRect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      badgeRect.setAttribute('x', midX - badgeWidth / 2);
      badgeRect.setAttribute('y', midY - 9);
      badgeRect.setAttribute('width', badgeWidth);
      badgeRect.setAttribute('height', 18);
      badgeRect.setAttribute('class', 'topo-edge-badge');

      const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      text.setAttribute('x', midX);
      text.setAttribute('y', midY + 4);
      text.setAttribute('class', 'topo-edge-text');
      text.textContent = labelText;

      edgeG.appendChild(badgeRect);
      edgeG.appendChild(text);
      edgesLayer.appendChild(edgeG);
    });
  }

  // --- Node Rendering ---
  function renderNodes(nodes) {
    nodesLayer.innerHTML = '';

    nodes.forEach(node => {
      const g = document.createElementNS('http://www.w3.org/2000/svg', 'g');
      g.setAttribute('class', 'topo-node');
      g.setAttribute('data-id', node.id);
      g.setAttribute('transform', `translate(${node.x}, ${node.y})`);

      const healthColor = node.health === 'critical' ? '#f1707b' 
                        : node.health === 'warning' ? '#faa05a' 
                        : '#54b054';

      const glowFilter = node.health === 'critical' ? 'url(#glowRed)' 
                       : node.health === 'warning' ? 'url(#glowAmber)' 
                       : 'url(#glowGreen)';

      if (node.type === 'service') {
        // Outer glowing ring
        const ring = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        ring.setAttribute('r', '40');
        ring.setAttribute('fill', 'none');
        ring.setAttribute('stroke', healthColor);
        ring.setAttribute('stroke-width', '3');
        ring.setAttribute('filter', glowFilter);
        g.appendChild(ring);

        // Inner circle body
        const body = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
        body.setAttribute('r', '36');
        body.setAttribute('fill', '#171d27');
        body.setAttribute('stroke', '#2c3746');
        body.setAttribute('stroke-width', '1.5');
        g.appendChild(body);

        // Node count pill
        const pillG = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        pillG.setAttribute('transform', 'translate(-24, -26)');
        const pillRect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        pillRect.setAttribute('width', '48');
        pillRect.setAttribute('height', '15');
        pillRect.setAttribute('rx', '7.5');
        pillRect.setAttribute('fill', '#0f6cbd');
        const pillText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        pillText.setAttribute('x', '24');
        pillText.setAttribute('y', '11');
        pillText.setAttribute('font-size', '9');
        pillText.setAttribute('fill', '#ffffff');
        pillText.setAttribute('font-weight', '700');
        pillText.setAttribute('text-anchor', 'middle');
        pillText.textContent = `${node.nodeCount} Node${node.nodeCount > 1 ? 's' : ''}`;
        pillG.appendChild(pillRect);
        pillG.appendChild(pillText);
        g.appendChild(pillG);

        // Tech icon / bracket
        const iconText = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        iconText.setAttribute('x', '0');
        iconText.setAttribute('y', '3');
        iconText.setAttribute('font-size', '16');
        iconText.setAttribute('fill', '#479ef5');
        iconText.setAttribute('text-anchor', 'middle');
        iconText.textContent = '⚙';
        g.appendChild(iconText);

        // Label
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', '0');
        label.setAttribute('y', '54');
        label.setAttribute('font-size', '11');
        label.setAttribute('font-weight', '600');
        label.setAttribute('fill', '#ffffff');
        label.setAttribute('text-anchor', 'middle');
        label.textContent = node.label;
        g.appendChild(label);

        // Metrics subtext
        const sub = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        sub.setAttribute('x', '0');
        sub.setAttribute('y', '68');
        sub.setAttribute('font-size', '9.5');
        sub.setAttribute('fill', '#8ba2b9');
        sub.setAttribute('text-anchor', 'middle');
        sub.textContent = `${node.callsPerMin} cpm | ${node.avgLatencyMs} ms`;
        g.appendChild(sub);

      } else if (node.type === 'database') {
        // AppDynamics Database Cylinder Icon
        const cylG = document.createElementNS('http://www.w3.org/2000/svg', 'g');
        cylG.setAttribute('transform', 'translate(-24, -26)');

        // Body rect
        const cylBody = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        cylBody.setAttribute('x', '0');
        cylBody.setAttribute('y', '8');
        cylBody.setAttribute('width', '48');
        cylBody.setAttribute('height', '32');
        cylBody.setAttribute('fill', '#1a2636');
        cylBody.setAttribute('stroke', '#2886de');
        cylBody.setAttribute('stroke-width', '1.5');
        cylG.appendChild(cylBody);

        // Bottom ellipse
        const botEllipse = document.createElementNS('http://www.w3.org/2000/svg', 'ellipse');
        botEllipse.setAttribute('cx', '24');
        botEllipse.setAttribute('cy', '40');
        botEllipse.setAttribute('rx', '24');
        botEllipse.setAttribute('ry', '8');
        botEllipse.setAttribute('fill', '#1a2636');
        botEllipse.setAttribute('stroke', '#2886de');
        botEllipse.setAttribute('stroke-width', '1.5');
        cylG.appendChild(botEllipse);

        // Top ellipse
        const topEllipse = document.createElementNS('http://www.w3.org/2000/svg', 'ellipse');
        topEllipse.setAttribute('cx', '24');
        topEllipse.setAttribute('cy', '8');
        topEllipse.setAttribute('rx', '24');
        topEllipse.setAttribute('ry', '8');
        topEllipse.setAttribute('fill', '#253549');
        topEllipse.setAttribute('stroke', '#479ef5');
        topEllipse.setAttribute('stroke-width', '1.5');
        cylG.appendChild(topEllipse);

        // Tech badge text inside cylinder
        const dbTech = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        dbTech.setAttribute('x', '24');
        dbTech.setAttribute('y', '28');
        dbTech.setAttribute('font-size', '9');
        dbTech.setAttribute('font-weight', '700');
        dbTech.setAttribute('fill', '#4ad1dc');
        dbTech.setAttribute('text-anchor', 'middle');
        dbTech.textContent = node.techBadge || 'DB';
        cylG.appendChild(dbTech);

        g.appendChild(cylG);

        // Label
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', '0');
        label.setAttribute('y', '32');
        label.setAttribute('font-size', '11');
        label.setAttribute('font-weight', '600');
        label.setAttribute('fill', '#ffffff');
        label.setAttribute('text-anchor', 'middle');
        label.textContent = node.label;
        g.appendChild(label);

        // Subtext
        const sub = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        sub.setAttribute('x', '0');
        sub.setAttribute('y', '46');
        sub.setAttribute('font-size', '9.5');
        sub.setAttribute('fill', '#8ba2b9');
        sub.setAttribute('text-anchor', 'middle');
        sub.textContent = `${node.callsPerMin} cpm | ${node.avgLatencyMs} ms`;
        g.appendChild(sub);

      } else if (node.type === 'queue') {
        // AppDynamics Queue Lozenge
        const lozenge = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        lozenge.setAttribute('x', '-40');
        lozenge.setAttribute('y', '-16');
        lozenge.setAttribute('width', '80');
        lozenge.setAttribute('height', '32');
        lozenge.setAttribute('rx', '16');
        lozenge.setAttribute('fill', '#142232');
        lozenge.setAttribute('stroke', '#4ad1dc');
        lozenge.setAttribute('stroke-width', '1.6');
        lozenge.setAttribute('stroke-dasharray', '4 2');
        g.appendChild(lozenge);

        // Queue icon
        const qIcon = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        qIcon.setAttribute('x', '0');
        qIcon.setAttribute('y', '5');
        qIcon.setAttribute('font-size', '11');
        qIcon.setAttribute('font-weight', '700');
        qIcon.setAttribute('fill', '#4ad1dc');
        qIcon.setAttribute('text-anchor', 'middle');
        qIcon.textContent = 'ActiveMQ';
        g.appendChild(qIcon);

        // Label
        const label = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        label.setAttribute('x', '0');
        label.setAttribute('y', '28');
        label.setAttribute('font-size', '11');
        label.setAttribute('font-weight', '600');
        label.setAttribute('fill', '#ffffff');
        label.setAttribute('text-anchor', 'middle');
        label.textContent = node.label;
        g.appendChild(label);

        // Subtext
        const sub = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        sub.setAttribute('x', '0');
        sub.setAttribute('y', '41');
        sub.setAttribute('font-size', '9.5');
        sub.setAttribute('fill', '#4ad1dc');
        sub.setAttribute('text-anchor', 'middle');
        sub.textContent = `${node.callsPerMin} cpm`;
        g.appendChild(sub);
      }

      // Drag and Click Handlers on Node
      let hasMoved = false;
      g.addEventListener('mousedown', (e) => {
        e.stopPropagation();
        hasMoved = false;
        const coords = screenToSvg(e.clientX, e.clientY);
        flowMapState.draggedNode = node;
        flowMapState.dragOffsetX = coords.x - node.x;
        flowMapState.dragOffsetY = coords.y - node.y;
      });

      g.addEventListener('mousemove', () => {
        hasMoved = true;
      });

      g.addEventListener('click', (e) => {
        e.stopPropagation();
        if (!hasMoved) {
          openNodeDrawer(node);
        }
      });

      nodesLayer.appendChild(g);
    });
  }

  // --- Scorecard Panel Update ---
  function updateScorecard(scorecard) {
    if (!scorecard) return;

    // Segmented bar
    const barTxNormal = document.getElementById('barTxNormal');
    const barTxSlow = document.getElementById('barTxSlow');
    const barTxVerySlow = document.getElementById('barTxVerySlow');
    const barTxError = document.getElementById('barTxError');

    if (barTxNormal) barTxNormal.style.width = `${scorecard.normalPercent}%`;
    if (barTxSlow) barTxSlow.style.width = `${scorecard.slowPercent}%`;
    if (barTxVerySlow) barTxVerySlow.style.width = `${scorecard.verySlowPercent}%`;
    if (barTxError) barTxError.style.width = `${scorecard.errorPercent}%`;

    // Percentage labels
    const txNormalPct = document.getElementById('txNormalPct');
    const txSlowPct = document.getElementById('txSlowPct');
    const txErrorPct = document.getElementById('txErrorPct');
    if (txNormalPct) txNormalPct.textContent = `${scorecard.normalPercent.toFixed(1)}%`;
    if (txSlowPct) txSlowPct.textContent = `${scorecard.slowPercent.toFixed(1)}%`;
    if (txErrorPct) txErrorPct.textContent = `${scorecard.errorPercent.toFixed(1)}%`;

    // Node health counts
    const countNodesNormal = document.getElementById('countNodesNormal');
    const countNodesWarning = document.getElementById('countNodesWarning');
    const countNodesCritical = document.getElementById('countNodesCritical');
    if (countNodesNormal) countNodesNormal.textContent = scorecard.nodesNormal;
    if (countNodesWarning) countNodesWarning.textContent = scorecard.nodesWarning;
    if (countNodesCritical) countNodesCritical.textContent = scorecard.nodesCritical;

    // Transaction scorecard rows
    const scoreNormalBar = document.getElementById('scoreNormalBar');
    const valScoreNormal = document.getElementById('valScoreNormal');
    if (scoreNormalBar) scoreNormalBar.style.width = `${scorecard.normalPercent}%`;
    if (valScoreNormal) valScoreNormal.textContent = `${scorecard.normalPercent.toFixed(1)}%`;

    const scoreSlowBar = document.getElementById('scoreSlowBar');
    const valScoreSlow = document.getElementById('valScoreSlow');
    if (scoreSlowBar) scoreSlowBar.style.width = `${scorecard.slowPercent}%`;
    if (valScoreSlow) valScoreSlow.textContent = `${scorecard.slowPercent.toFixed(1)}%`;

    const scoreVerySlowBar = document.getElementById('scoreVerySlowBar');
    const valScoreVerySlow = document.getElementById('valScoreVerySlow');
    if (scoreVerySlowBar) scoreVerySlowBar.style.width = `${scorecard.verySlowPercent}%`;
    if (valScoreVerySlow) valScoreVerySlow.textContent = `${scorecard.verySlowPercent.toFixed(1)}%`;

    const scoreStallBar = document.getElementById('scoreStallBar');
    const valScoreStall = document.getElementById('valScoreStall');
    if (scoreStallBar) scoreStallBar.style.width = `${scorecard.stallPercent}%`;
    if (valScoreStall) valScoreStall.textContent = `${scorecard.stallPercent.toFixed(1)}%`;

    const scoreErrorsBar = document.getElementById('scoreErrorsBar');
    const valScoreErrors = document.getElementById('valScoreErrors');
    if (scoreErrorsBar) scoreErrorsBar.style.width = `${scorecard.errorPercent}%`;
    if (valScoreErrors) valScoreErrors.textContent = `${scorecard.errorPercent.toFixed(1)}%`;

    // Key metrics
    const statTotalCalls = document.getElementById('statTotalCalls');
    const statCallsPerMin = document.getElementById('statCallsPerMin');
    const statAvgLatency = document.getElementById('statAvgLatency');
    const statErrorsPerMin = document.getElementById('statErrorsPerMin');

    if (statTotalCalls) statTotalCalls.textContent = scorecard.totalCalls.toLocaleString();
    if (statCallsPerMin) statCallsPerMin.textContent = scorecard.callsPerMin.toLocaleString();
    if (statAvgLatency) statAvgLatency.textContent = `${scorecard.avgLatencyMs} ms`;
    if (statErrorsPerMin) statErrorsPerMin.textContent = scorecard.errorsPerMin.toFixed(2);

    // Headlines
    const headlineLoadCpm = document.getElementById('headlineLoadCpm');
    const headlineLatencyMs = document.getElementById('headlineLatencyMs');
    const headlineErrorRate = document.getElementById('headlineErrorRate');
    if (headlineLoadCpm) headlineLoadCpm.textContent = Math.round(scorecard.callsPerMin).toLocaleString();
    if (headlineLatencyMs) headlineLatencyMs.textContent = Math.round(scorecard.avgLatencyMs);
    if (headlineErrorRate) headlineErrorRate.textContent = `${scorecard.errorPercent.toFixed(1)}%`;
  }

  // --- Bottom Ribbon Area Charts (Load, Response Time, Errors) ---
  function renderRibbonCharts(timeSeries) {
    if (!timeSeries || timeSeries.length === 0) return;

    const labels = timeSeries.map(pt => {
      const d = new Date(pt.timestamp);
      return isNaN(d) ? '' : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    });

    const commonOptions = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: {
          backgroundColor: '#242424',
          borderColor: 'rgba(255, 255, 255, 0.12)',
          borderWidth: 1,
          padding: 6,
          titleFont: { size: 10 },
          bodyFont: { size: 10 }
        }
      },
      scales: {
        x: {
          grid: { display: false },
          ticks: { color: '#707070', font: { size: 9 }, maxTicksLimit: 6 }
        },
        y: {
          grid: { color: 'rgba(255, 255, 255, 0.04)' },
          ticks: { color: '#707070', font: { size: 9 }, maxTicksLimit: 3 },
          beginAtZero: true
        }
      }
    };

    // 1. Load Chart
    const canvasLoad = document.getElementById('chartRibbonLoad');
    if (canvasLoad) {
      const ctxLoad = canvasLoad.getContext('2d');
      const gradLoad = ctxLoad.createLinearGradient(0, 0, 0, 100);
      gradLoad.addColorStop(0, 'rgba(40, 134, 222, 0.4)');
      gradLoad.addColorStop(1, 'rgba(40, 134, 222, 0.0)');

      const loadValues = timeSeries.map(pt => pt.callsPerMin);
      if (flowMapState.charts.load) {
        flowMapState.charts.load.data.labels = labels;
        flowMapState.charts.load.data.datasets[0].data = loadValues;
        flowMapState.charts.load.update();
      } else {
        flowMapState.charts.load = new Chart(ctxLoad, {
          type: 'line',
          data: {
            labels: labels,
            datasets: [{
              label: 'Calls / min',
              data: loadValues,
              borderColor: '#2886de',
              borderWidth: 1.8,
              backgroundColor: gradLoad,
              fill: true,
              tension: 0.35,
              pointRadius: 0
            }]
          },
          options: commonOptions
        });
      }
    }

    // 2. Response Time Chart
    const canvasLatency = document.getElementById('chartRibbonResponseTime');
    if (canvasLatency) {
      const ctxLat = canvasLatency.getContext('2d');
      const gradLat = ctxLat.createLinearGradient(0, 0, 0, 100);
      gradLat.addColorStop(0, 'rgba(84, 176, 84, 0.4)');
      gradLat.addColorStop(1, 'rgba(84, 176, 84, 0.0)');

      const latValues = timeSeries.map(pt => pt.avgLatencyMs);
      if (flowMapState.charts.latency) {
        flowMapState.charts.latency.data.labels = labels;
        flowMapState.charts.latency.data.datasets[0].data = latValues;
        flowMapState.charts.latency.update();
      } else {
        flowMapState.charts.latency = new Chart(ctxLat, {
          type: 'line',
          data: {
            labels: labels,
            datasets: [{
              label: 'Avg Latency (ms)',
              data: latValues,
              borderColor: '#54b054',
              borderWidth: 1.8,
              backgroundColor: gradLat,
              fill: true,
              tension: 0.35,
              pointRadius: 0
            }]
          },
          options: commonOptions
        });
      }
    }

    // 3. Errors Chart
    const canvasErrors = document.getElementById('chartRibbonErrors');
    if (canvasErrors) {
      const ctxErr = canvasErrors.getContext('2d');
      const gradErr = ctxErr.createLinearGradient(0, 0, 0, 100);
      gradErr.addColorStop(0, 'rgba(241, 112, 123, 0.4)');
      gradErr.addColorStop(1, 'rgba(241, 112, 123, 0.0)');

      const errValues = timeSeries.map(pt => pt.errorRatePercent);
      if (flowMapState.charts.errors) {
        flowMapState.charts.errors.data.labels = labels;
        flowMapState.charts.errors.data.datasets[0].data = errValues;
        flowMapState.charts.errors.update();
      } else {
        flowMapState.charts.errors = new Chart(ctxErr, {
          type: 'line',
          data: {
            labels: labels,
            datasets: [{
              label: 'Error Rate (%)',
              data: errValues,
              borderColor: '#f1707b',
              borderWidth: 1.8,
              backgroundColor: gradErr,
              fill: true,
              tension: 0.35,
              pointRadius: 0
            }]
          },
          options: commonOptions
        });
      }
    }
  }

  function getSpanStatusBadge(statusCode) {
    const s = String(statusCode || '').trim();
    if (s === 'Error' || s === '2' || s === '500') {
      return '<span class="badge badge-error">Error</span>';
    }
    if (s === '404' || s === 'Warn' || s === 'Warning') {
      return '<span class="badge badge-warn">Warn</span>';
    }
    if (s === 'Unset' || s === '0') {
      return '<span class="badge badge-gray">Unset</span>';
    }
    if (s === 'Ok' || s === '1' || s === '200') {
      return '<span class="badge badge-ok">OK</span>';
    }
    if (s) {
      return `<span class="badge badge-gray">${escapeHtml(s)}</span>`;
    }
    return '<span class="badge badge-ok">OK</span>';
  }

  function getLogSeverityBadge(severityText) {
    const sev = (severityText || 'INFO').toUpperCase();
    let badgeClass = 'log-badge-info';
    if (sev === 'ERROR' || sev === 'FATAL') badgeClass = 'log-badge-error';
    else if (sev === 'WARN' || sev === 'WARNING') badgeClass = 'log-badge-warn';
    else if (sev === 'DEBUG' || sev === 'TRACE') badgeClass = 'log-badge-debug';
    return `<span class="log-badge ${badgeClass}">${escapeHtml(sev)}</span>`;
  }

  // --- Node Drawer Detail Loading ---
  async function openNodeDrawer(node) {
    if (!nodeDrawer) return;

    flowMapState.selectedNode = node;
    const titleEl = document.getElementById('drawerNodeTitle');
    const typeEl = document.getElementById('drawerNodeType');
    const techEl = document.getElementById('drawerNodeTech');
    const countEl = document.getElementById('drawerNodeCount');
    const cpmEl = document.getElementById('drawerCallsPerMin');
    const latEl = document.getElementById('drawerAvgLatency');
    const errEl = document.getElementById('drawerErrorRate');

    const btnDrillTraces = document.getElementById('btnDrillTraces');
    const btnDrillLogs = document.getElementById('btnDrillLogs');
    const btnDrillMetrics = document.getElementById('btnDrillMetrics');

    if (titleEl) titleEl.textContent = node.label;
    if (typeEl) typeEl.textContent = node.type.toUpperCase();
    if (techEl) techEl.textContent = node.techBadge || 'Service';
    if (countEl) countEl.textContent = `${node.nodeCount} Node${node.nodeCount > 1 ? 's' : ''}`;
    if (cpmEl) cpmEl.textContent = node.callsPerMin.toLocaleString();
    if (latEl) latEl.textContent = `${node.avgLatencyMs} ms`;
    if (errEl) errEl.textContent = `${node.errorRatePercent}%`;

    // Action links
    if (btnDrillTraces) btnDrillTraces.href = `/traces.html?service=${encodeURIComponent(node.id)}`;
    if (btnDrillLogs) btnDrillLogs.href = `/logs.html?service=${encodeURIComponent(node.id)}`;
    if (btnDrillMetrics) btnDrillMetrics.href = `/dashboard.html?service=${encodeURIComponent(node.id)}`;

    nodeDrawer.classList.add('open');

    // Fetch node telemetry
    const minutes = timeWindowSelect ? timeWindowSelect.value : 15;
    try {
      const res = await fetch(`/api/topology/nodes/${encodeURIComponent(node.id)}?minutes=${minutes}`);
      if (res.ok) {
        const details = await res.json();

        // Render recent traces in drawer
        const tracesTbody = document.getElementById('drawerTracesList');
        if (tracesTbody && details.recentTraces) {
          if (details.recentTraces.length === 0) {
            tracesTbody.innerHTML = '<tr><td colspan="4" style="text-align:center; color:#707070;">No recent traces</td></tr>';
          } else {
            tracesTbody.innerHTML = details.recentTraces.map(tr => `
              <tr>
                <td style="font-weight:600; color:#fff; max-width:160px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${escapeHtml(tr.spanName)}">${escapeHtml(tr.spanName)}</td>
                <td style="white-space:nowrap;">${tr.durationMs.toFixed(1)} ms</td>
                <td style="white-space:nowrap;">${getSpanStatusBadge(tr.statusCode)}</td>
                <td style="white-space:nowrap;"><a href="/traces.html?traceId=${encodeURIComponent(tr.traceId)}" class="fui-btn fui-btn-subtle" style="padding:2px 8px; font-size:11px;">View</a></td>
              </tr>
            `).join('');
          }
        }

        // Render recent logs in drawer
        const logsTbody = document.getElementById('drawerLogsList');
        if (logsTbody && details.recentLogs) {
          if (details.recentLogs.length === 0) {
            logsTbody.innerHTML = '<tr><td colspan="3" style="text-align:center; color:#707070;">No recent logs</td></tr>';
          } else {
            logsTbody.innerHTML = details.recentLogs.map(lg => `
              <tr>
                <td style="white-space:nowrap;">${getLogSeverityBadge(lg.severityText)}</td>
                <td style="max-width:210px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${escapeHtml(lg.body)}">${escapeHtml(lg.body)}</td>
                <td style="color:#707070; font-size:10px; white-space:nowrap;">${new Date(lg.timestamp).toLocaleTimeString()}</td>
              </tr>
            `).join('');
          }
        }
      }
    } catch (err) {
      console.error('Failed to load node details', err);
    }
  }

  // --- Main Data Loading ---
  async function loadFlowMap() {
    if (flowMapState.draggedNode) {
      return;
    }

    const minutes = timeWindowSelect ? timeWindowSelect.value : 15;
    const app = appSelect ? appSelect.value : 'ECommerce';

    try {
      const res = await fetch(`/api/topology/flow-map?minutes=${minutes}&application=${encodeURIComponent(app)}`);
      if (!res.ok) return;

      const data = await res.json();
      flowMapState.data = data;

      // Apply saved node layout from session cache if available
      const savedLayout = getSavedNodeLayout();
      if (savedLayout && Array.isArray(data.nodes)) {
        data.nodes.forEach(node => {
          if (savedLayout[node.id] && typeof savedLayout[node.id].x === 'number' && typeof savedLayout[node.id].y === 'number') {
            node.x = savedLayout[node.id].x;
            node.y = savedLayout[node.id].y;
          }
        });
      }
      updateResetButtonState();

      renderEdges(data.edges, data.nodes);
      renderNodes(data.nodes);
      updateScorecard(data.scorecard);
      renderRibbonCharts(data.timeSeries);

      // If a node was already selected, update its stats
      if (flowMapState.selectedNode) {
        const updatedNode = data.nodes.find(n => n.id === flowMapState.selectedNode.id);
        if (updatedNode) openNodeDrawer(updatedNode);
      }
    } catch (err) {
      console.error('Failed to load flow map data', err);
    }
  }

  // Event Listeners
  if (timeWindowSelect) {
    timeWindowSelect.addEventListener('change', () => loadFlowMap());
  }

  if (appSelect) {
    appSelect.addEventListener('change', () => loadFlowMap());
  }

  if (btnRefresh) {
    btnRefresh.addEventListener('click', () => loadFlowMap());
  }

  // Auto-refresh interval (15s)
  function setupAutoRefresh() {
    if (flowMapState.autoRefreshTimer) {
      clearInterval(flowMapState.autoRefreshTimer);
      flowMapState.autoRefreshTimer = null;
    }
    if (autoRefreshToggle && autoRefreshToggle.checked) {
      flowMapState.autoRefreshTimer = setInterval(() => {
        loadFlowMap();
      }, 15000);
    }
  }

  if (autoRefreshToggle) {
    autoRefreshToggle.addEventListener('change', setupAutoRefresh);
  }

  setupAutoRefresh();
  await loadFlowMap();

  // Check URL query parameters (e.g. ?service=...)
  const params = new URLSearchParams(window.location.search);
  const targetService = params.get('service');
  if (targetService && flowMapState.data) {
    const matchedNode = flowMapState.data.nodes.find(n => n.id === targetService);
    if (matchedNode) {
      openNodeDrawer(matchedNode);
    }
  }
}

// ============================================================================
// Database Management Controller (/database.html)
// ============================================================================
let currentDbStorage = null;
let currentDbPolicies = null;
let currentDbBackups = [];

function switchDbTab(tabName) {
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.tab === tabName);
  });
  document.querySelectorAll('.tab-pane').forEach(pane => {
    pane.classList.toggle('active', pane.id === `pane-${tabName}`);
  });

  if (tabName === 'audit') {
    loadAuditLogs();
  }
}

async function initDatabaseManagement() {
  const user = await checkAuth();
  if (!user || !user.isAuthenticated) return;
  if (user.role !== 'Admin') {
    window.location.href = '/dashboard.html';
    return;
  }

  // Bind restore token confirmation input
  const tokenInput = document.getElementById('restoreConfirmToken');
  const submitRestoreBtn = document.getElementById('btnSubmitRestore');
  if (tokenInput && submitRestoreBtn) {
    tokenInput.addEventListener('input', () => {
      submitRestoreBtn.disabled = tokenInput.value.trim() !== 'CONFIRM_RESTORE';
    });
  }

  // Bind backdrop clicks to close modals
  const createModal = document.getElementById('createBackupModal');
  if (createModal) {
    createModal.addEventListener('click', (e) => {
      if (e.target === createModal) closeCreateBackupModal();
    });
  }

  const pruneModal = document.getElementById('confirmPruneModal');
  if (pruneModal) {
    pruneModal.addEventListener('click', (e) => {
      if (e.target === pruneModal) closeConfirmPruneModal();
    });
  }

  await loadDatabaseData();
}

async function loadDatabaseData() {
  await Promise.all([
    loadStorageStats(),
    loadRetentionPolicies(),
    loadBackups()
  ]);
}

async function loadStorageStats() {
  try {
    const res = await fetch('/api/admin/database/storage');
    if (!res.ok) throw new Error('Failed to load storage stats');
    currentDbStorage = await res.json();
    renderStorageStats(currentDbStorage);
  } catch (err) {
    console.error('Storage stats error', err);
  }
}

function renderStorageStats(stats) {
  if (!stats) return;

  const statDbSize = document.getElementById('statDbSize');
  const statWalSize = document.getElementById('statWalSize');
  const statFreelist = document.getElementById('statFreelist');
  const statPages = document.getElementById('statPages');
  const statBackupsSize = document.getElementById('statBackupsSize');
  const statBackupsCount = document.getElementById('statBackupsCount');
  const warningBanner = document.getElementById('storageWarningBanner');
  const warningText = document.getElementById('storageWarningText');

  if (statDbSize) statDbSize.textContent = stats.databaseSizeFormatted;
  if (statWalSize) statWalSize.textContent = `WAL: ${stats.walSizeFormatted} | SHM: ${stats.shmSizeFormatted}`;
  if (statFreelist) statFreelist.textContent = stats.freelistSizeFormatted;
  if (statPages) statPages.textContent = `${Number(stats.freelistCount).toLocaleString()} free pages / ${Number(stats.pageCount).toLocaleString()} total`;
  if (statBackupsSize) statBackupsSize.textContent = stats.totalBackupsSizeFormatted;
  if (statBackupsCount) statBackupsCount.textContent = `${stats.backupCount} snapshot archives`;

  // Storage warning threshold check
  if (warningBanner && warningText) {
    if (stats.isStorageWarning) {
      warningText.textContent = stats.storageWarningMessage;
      warningBanner.style.display = 'flex';
    } else {
      warningBanner.style.display = 'none';
    }
  }

  // Render Table Breakdown
  const tableBody = document.getElementById('tablesTableBody');
  if (tableBody && stats.tables) {
    tableBody.innerHTML = '';
    stats.tables.forEach(t => {
      const tr = document.createElement('tr');
      const oldestStr = t.oldestRecord ? new Date(t.oldestRecord).toLocaleDateString() : '—';
      const newestStr = t.newestRecord ? new Date(t.newestRecord).toLocaleDateString() : '—';
      const spanStr = (t.oldestRecord && t.newestRecord) ? `${oldestStr} → ${newestStr}` : '—';

      tr.innerHTML = `
        <td style="font-weight:600; color:var(--fui-colorNeutralForeground1); font-family:var(--fui-fontFamilyMonospace); vertical-align:middle;">${escapeHtml(t.tableName)}</td>
        <td style="font-weight:600; text-align:right; font-family:var(--fui-fontFamilyMonospace); vertical-align:middle; white-space:nowrap;">${Number(t.rowCount).toLocaleString()}</td>
        <td style="color:var(--fui-colorNeutralForeground3); text-align:right; font-family:var(--fui-fontFamilyMonospace); vertical-align:middle; white-space:nowrap;">${t.estimatedSizeFormatted}</td>
        <td style="font-size:0.85rem; color:var(--fui-colorNeutralForeground3); vertical-align:middle;">${spanStr}</td>
        <td style="text-align:right; vertical-align:middle; white-space:nowrap;">
          <button class="btn-secondary btn-sm" onclick="quickPruneTable('${escapeHtml(t.tableName)}')">Prune</button>
        </td>
      `;
      tableBody.appendChild(tr);
    });
  }
}

async function loadRetentionPolicies() {
  try {
    const res = await fetch('/api/admin/database/retention');
    if (!res.ok) throw new Error('Failed to load retention policies');
    currentDbPolicies = await res.json();
    renderRetentionPolicies(currentDbPolicies);
  } catch (err) {
    console.error('Retention policy error', err);
  }
}

function renderRetentionPolicies(p) {
  if (!p) return;
  const mInput = document.getElementById('policyMetricsDays');
  const tInput = document.getElementById('policyTracesDays');
  const lInput = document.getElementById('policyLogsDays');
  const aInput = document.getElementById('policyAlertsDays');
  const wInput = document.getElementById('policyWarningMb');
  const autoPrune = document.getElementById('policyAutoPrune');
  const autoPruneHour = document.getElementById('policyAutoPruneHour');
  const autoBackup = document.getElementById('policyAutoBackup');
  const autoBackupHour = document.getElementById('policyAutoBackupHour');
  const backupRetention = document.getElementById('policyBackupRetentionCount');

  if (mInput) mInput.value = p.metricsRetentionDays;
  if (tInput) tInput.value = p.tracesRetentionDays;
  if (lInput) lInput.value = p.logsRetentionDays;
  if (aInput) aInput.value = p.alertsRetentionDays;
  if (wInput) wInput.value = p.storageWarningThresholdMb;
  if (autoPrune) autoPrune.checked = p.autoPruneEnabled;
  if (autoPruneHour) autoPruneHour.value = p.autoPruneHourUtc;
  if (autoBackup) autoBackup.checked = p.autoBackupEnabled;
  if (autoBackupHour) autoBackupHour.value = p.autoBackupHourUtc;
  if (backupRetention) backupRetention.value = p.backupRetentionCount;
}

async function handleSaveRetention(e) {
  e.preventDefault();
  const payload = {
    metricsRetentionDays: parseInt(document.getElementById('policyMetricsDays').value, 10),
    tracesRetentionDays: parseInt(document.getElementById('policyTracesDays').value, 10),
    logsRetentionDays: parseInt(document.getElementById('policyLogsDays').value, 10),
    alertsRetentionDays: parseInt(document.getElementById('policyAlertsDays').value, 10),
    auditLogsRetentionDays: currentDbPolicies?.auditLogsRetentionDays || 180,
    storageWarningThresholdMb: parseInt(document.getElementById('policyWarningMb').value, 10),
    autoPruneEnabled: document.getElementById('policyAutoPrune').checked,
    autoPruneHourUtc: parseInt(document.getElementById('policyAutoPruneHour').value, 10),
    autoBackupEnabled: document.getElementById('policyAutoBackup').checked,
    autoBackupHourUtc: parseInt(document.getElementById('policyAutoBackupHour').value, 10),
    backupRetentionCount: parseInt(document.getElementById('policyBackupRetentionCount').value, 10)
  };

  try {
    const res = await fetch('/api/admin/database/retention', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Failed to update retention policies');
    currentDbPolicies = await res.json();
    alert('Retention policies updated successfully.');
    await loadStorageStats();
  } catch (err) {
    alert(`Error updating retention: ${err.message}`);
  }
}

async function loadBackups() {
  try {
    const res = await fetch('/api/admin/database/backups');
    if (!res.ok) throw new Error('Failed to load backups');
    currentDbBackups = await res.json();
    renderBackups(currentDbBackups);
  } catch (err) {
    console.error('Backups catalog error', err);
  }
}

function renderBackups(backups) {
  const tableBody = document.getElementById('backupsTableBody');
  const restoreSelect = document.getElementById('restoreSelectBackup');

  if (restoreSelect) {
    restoreSelect.innerHTML = '<option value="">Select a snapshot...</option>';
    backups.forEach(b => {
      const opt = document.createElement('option');
      opt.value = b.fileName;
      opt.textContent = `${b.fileName} (${b.sizeFormatted} — ${new Date(b.createdAt).toLocaleDateString()})`;
      restoreSelect.appendChild(opt);
    });
  }

  if (!tableBody) return;
  tableBody.innerHTML = '';

  if (backups.length === 0) {
    tableBody.innerHTML = '<tr><td colspan="6" style="text-align:center; color:var(--fui-colorNeutralForeground4); padding:2rem;">No backup snapshots found.</td></tr>';
    return;
  }

  backups.forEach(b => {
    const tr = document.createElement('tr');
    let typeBadge = '<span class="badge badge-gray">Manual</span>';
    if (b.type === 'Scheduled') {
      typeBadge = '<span class="badge" style="background:rgba(15,108,189,0.2); color:#479ef5; border:1px solid rgba(15,108,189,0.4);">Scheduled</span>';
    } else if (b.type === 'SafetySnapshot') {
      typeBadge = '<span class="badge" style="background:rgba(245,158,11,0.2); color:#f59e0b; border:1px solid rgba(245,158,11,0.4);">Safety Rollback</span>';
    }

    const shortHash = b.checksumSha256 ? b.checksumSha256.substring(0, 10) + '...' : '—';
    const createdStr = new Date(b.createdAt).toLocaleString();

    tr.innerHTML = `
      <td style="vertical-align: middle;">
        <div style="display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap;">
          <svg viewBox="0 0 24 24" style="width: 16px; height: 16px; fill: var(--fui-colorBrandForeground1); flex-shrink: 0;">
            <path d="M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM14 13v4h-4v-4H7l5-5 5 5h-3z"/>
          </svg>
          <span style="font-weight: 600; font-family: var(--fui-fontFamilyMonospace); color: var(--fui-colorNeutralForeground1); font-size: 0.85rem; word-break: break-word;">
            ${escapeHtml(b.fileName)}
          </span>
          ${b.isCompressed ? '<span class="badge" style="background: rgba(16,185,129,0.18); color: #34d399; border: 1px solid rgba(16,185,129,0.3); font-size: 0.7rem; padding: 0.12rem 0.35rem;">GZ</span>' : ''}
          ${b.label ? `<span class="badge badge-gray" style="font-size: 0.7rem; padding: 0.12rem 0.35rem;">${escapeHtml(b.label)}</span>` : ''}
        </div>
      </td>
      <td style="vertical-align: middle; white-space: nowrap;">${typeBadge}</td>
      <td style="vertical-align: middle; text-align: right; font-weight: 600; font-size: 0.85rem; font-family: var(--fui-fontFamilyMonospace); white-space: nowrap;">${b.sizeFormatted}</td>
      <td style="vertical-align: middle; font-size: 0.85rem; color: var(--fui-colorNeutralForeground3); white-space: nowrap;">${createdStr}</td>
      <td style="vertical-align: middle; font-family: var(--fui-fontFamilyMonospace); font-size: 0.75rem; color: var(--fui-colorNeutralForeground4); white-space: nowrap;" title="${escapeHtml(b.checksumSha256)}">
        <span class="badge badge-gray mono" style="font-size: 0.75rem; cursor: default;">${shortHash}</span>
      </td>
      <td style="vertical-align: middle; text-align: right; white-space: nowrap;">
        <div style="display: inline-flex; align-items: center; justify-content: flex-end; gap: 0.35rem; width: 100%;">
          <button class="btn-secondary btn-sm" onclick="downloadBackup('${escapeHtml(b.fileName)}')">Download</button>
          <button class="btn-secondary btn-sm" onclick="initiateRestoreModal('${escapeHtml(b.fileName)}')">Restore</button>
          <button class="btn-secondary btn-sm" style="border-color: rgba(239,68,68,0.4); color: #fca5a5;" onclick="deleteBackup('${escapeHtml(b.fileName)}')">Delete</button>
        </div>
      </td>
    `;
    tableBody.appendChild(tr);
  });
}

function openCreateBackupModal() {
  const modal = document.getElementById('createBackupModal');
  if (modal) {
    modal.classList.add('open', 'active');
    const input = document.getElementById('backupLabelInput');
    if (input) {
      input.value = '';
      input.focus();
    }
  }
}

function closeCreateBackupModal() {
  const modal = document.getElementById('createBackupModal');
  if (modal) {
    modal.classList.remove('open', 'active');
  }
}

async function handleCreateBackupSubmit(e) {
  e.preventDefault();
  const label = document.getElementById('backupLabelInput')?.value?.trim();
  const compress = document.getElementById('backupCompressInput')?.checked ?? true;
  const btn = document.getElementById('btnConfirmCreateBackup');

  if (btn) {
    btn.disabled = true;
    btn.textContent = 'Generating...';
  }

  try {
    const res = await fetch('/api/admin/database/backups', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label, compress })
    });
    if (!res.ok) throw new Error('Failed to create backup');
    const created = await res.json();
    closeCreateBackupModal();
    alert(`Backup snapshot created successfully:\n${created.fileName} (${created.sizeFormatted})`);
    await loadBackups();
    await loadStorageStats();
  } catch (err) {
    alert(`Backup error: ${err.message}`);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.textContent = 'Generate Snapshot';
    }
  }
}

function downloadBackup(fileName) {
  window.location.href = `/api/admin/database/backups/${encodeURIComponent(fileName)}/download`;
}

async function deleteBackup(fileName) {
  if (!confirm(`Are you sure you want to permanently delete the backup archive '${fileName}'?`)) return;

  try {
    const res = await fetch(`/api/admin/database/backups/${encodeURIComponent(fileName)}`, {
      method: 'DELETE'
    });
    if (!res.ok) throw new Error('Failed to delete backup');
    await loadBackups();
    await loadStorageStats();
  } catch (err) {
    alert(`Delete error: ${err.message}`);
  }
}

function initiateRestoreModal(fileName) {
  switchDbTab('restore');
  const select = document.getElementById('restoreSelectBackup');
  if (select) select.value = fileName;
}

async function handleExecuteRestore(e) {
  e.preventDefault();
  const backupFileName = document.getElementById('restoreSelectBackup')?.value;
  const confirmationToken = document.getElementById('restoreConfirmToken')?.value?.trim();
  const statusBox = document.getElementById('restoreStatusBox');
  const btn = document.getElementById('btnSubmitRestore');

  if (!backupFileName) {
    alert('Please select a backup file to restore.');
    return;
  }

  if (confirmationToken !== 'CONFIRM_RESTORE') {
    alert('You must type CONFIRM_RESTORE to authorize this operation.');
    return;
  }

  if (btn) {
    btn.disabled = true;
    btn.textContent = 'Restoring database (Safety snapshot in progress)...';
  }

  if (statusBox) {
    statusBox.style.display = 'block';
    statusBox.style.background = 'var(--fui-colorNeutralBackground3)';
    statusBox.style.color = 'var(--fui-colorNeutralForeground1)';
    statusBox.textContent = 'Initiating safe restore: Creating automated rollback snapshot and replacing database pages...';
  }

  try {
    const res = await fetch('/api/admin/database/restore', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ backupFileName, confirmationToken })
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(err.error || 'Restore failed');
    }

    const result = await res.json();
    if (statusBox) {
      statusBox.style.background = 'var(--fui-colorPaletteGreenBackground1)';
      statusBox.style.color = 'var(--fui-colorPaletteGreenForeground1)';
      statusBox.innerHTML = `
        <strong>Restore Successful!</strong><br>
        Restored from: <code>${escapeHtml(result.restoredFrom)}</code><br>
        Safety Rollback Snapshot: <code>${escapeHtml(result.safetySnapshotFileName)}</code><br>
        Elapsed: ${result.elapsedMs}ms | Integrity: ${escapeHtml(result.integrityCheckOutput)}
      `;
    }
    alert('Database restore complete! The system has been synchronized.');
    await loadDatabaseData();
  } catch (err) {
    if (statusBox) {
      statusBox.style.background = 'var(--fui-colorPaletteRedBackground1)';
      statusBox.style.color = 'var(--fui-colorPaletteRedForeground1)';
      statusBox.textContent = `Restore failed: ${err.message}`;
    }
    alert(`Restore failed: ${err.message}`);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.textContent = 'Authorize & Restore Database Snapshot';
    }
  }
}

// Pruning Logic
function quickPruneTable(tableName) {
  switchDbTab('retention');
  const targetSelect = document.getElementById('pruneTargetSelect');
  if (targetSelect) {
    if (tableName === 'MetricSamples') targetSelect.value = 'metrics';
    else if (tableName === 'Traces') targetSelect.value = 'traces';
    else if (tableName === 'Logs') targetSelect.value = 'logs';
    else if (tableName === 'AdminAuditLogs') targetSelect.value = 'audit_logs';
    else targetSelect.value = 'all';
  }
  handleDryRunPrune();
}

async function handleDryRunPrune() {
  const target = document.getElementById('pruneTargetSelect')?.value || 'all';
  const customDaysStr = document.getElementById('pruneCustomDays')?.value;
  const customRetentionDays = customDaysStr ? parseInt(customDaysStr, 10) : null;
  const previewBox = document.getElementById('prunePreviewBox');
  const previewContent = document.getElementById('prunePreviewContent');

  try {
    const res = await fetch('/api/admin/database/prune', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        target,
        customRetentionDays,
        dryRun: true
      })
    });
    if (!res.ok) throw new Error('Simulation failed');
    const data = await res.json();

    if (previewBox && previewContent) {
      previewBox.style.display = 'block';
      let countsHtml = Object.entries(data.deletedCounts)
        .map(([k, v]) => `• <strong>${k}</strong>: ${Number(v).toLocaleString()} candidate rows`)
        .join('<br>');
      if (!countsHtml) countsHtml = 'No candidate rows qualify for pruning under current criteria.';

      previewContent.innerHTML = `
        ${countsHtml}<br>
        <strong>Total candidate records:</strong> ${Number(data.totalRowsDeleted).toLocaleString()}<br>
        <strong>Estimated reclaimable storage:</strong> ${data.freedSizeFormatted}
      `;
    }
  } catch (err) {
    alert(`Dry-run simulation error: ${err.message}`);
  }
}

function openConfirmPruneModal() {
  const modal = document.getElementById('confirmPruneModal');
  const summary = document.getElementById('pruneConfirmSummary');
  const target = document.getElementById('pruneTargetSelect')?.value || 'all';
  const customDaysStr = document.getElementById('pruneCustomDays')?.value;

  if (summary) {
    summary.innerHTML = `Target: <strong>${escapeHtml(target.toUpperCase())}</strong> ${customDaysStr ? `(Cutoff override: ${customDaysStr} days)` : '(Using default policy thresholds)'}`;
  }
  if (modal) modal.classList.add('open', 'active');
}

function closeConfirmPruneModal() {
  const modal = document.getElementById('confirmPruneModal');
  if (modal) modal.classList.remove('open', 'active');
}

async function handleExecutePruneConfirmed() {
  const target = document.getElementById('pruneTargetSelect')?.value || 'all';
  const customDaysStr = document.getElementById('pruneCustomDays')?.value;
  const customRetentionDays = customDaysStr ? parseInt(customDaysStr, 10) : null;
  const runCheckpoint = document.getElementById('pruneCheckpoint')?.checked ?? true;
  const runVacuum = document.getElementById('pruneVacuum')?.checked ?? false;
  const btn = document.getElementById('btnExecutePruneConfirm');

  if (btn) {
    btn.disabled = true;
    btn.textContent = 'Purging in chunked batches...';
  }

  try {
    const res = await fetch('/api/admin/database/prune', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        target,
        customRetentionDays,
        dryRun: false,
        runCheckpoint,
        runVacuum
      })
    });
    if (!res.ok) throw new Error('Prune execution failed');
    const data = await res.json();
    closeConfirmPruneModal();
    alert(`Prune completed successfully!\nTotal rows purged: ${Number(data.totalRowsDeleted).toLocaleString()}\nEstimated space freed: ${data.freedSizeFormatted}\nElapsed: ${data.elapsedMs}ms`);
    await loadStorageStats();
  } catch (err) {
    alert(`Prune error: ${err.message}`);
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.textContent = 'Yes, Execute Prune';
    }
  }
}

// Engine Operations
async function runIntegrityCheck() {
  try {
    const res = await fetch('/api/admin/database/health');
    if (!res.ok) throw new Error('Health check failed');
    const data = await res.json();
    alert(`Database Integrity Status: ${data.status.toUpperCase()}\n\nIntegrity Check: ${data.integrityCheckOutput}\nForeign Key Check: ${data.foreignKeyCheckOutput}`);
  } catch (err) {
    alert(`Integrity check error: ${err.message}`);
  }
}

async function runCheckpoint() {
  if (!confirm('Run PRAGMA wal_checkpoint(TRUNCATE) to sync and truncate the WAL file?')) return;
  try {
    const res = await fetch('/api/admin/database/checkpoint', { method: 'POST' });
    if (!res.ok) throw new Error('Checkpoint failed');
    const data = await res.json();
    alert(data.message || 'WAL Checkpoint completed.');
    await loadStorageStats();
  } catch (err) {
    alert(`Checkpoint error: ${err.message}`);
  }
}

async function runVacuum() {
  if (!confirm('Run VACUUM to rebuild and compact the database? This may take a few seconds on large databases.')) return;
  try {
    const res = await fetch('/api/admin/database/vacuum', { method: 'POST' });
    if (!res.ok) throw new Error('VACUUM failed');
    const data = await res.json();
    alert(data.message || 'VACUUM completed.');
    await loadStorageStats();
  } catch (err) {
    alert(`VACUUM error: ${err.message}`);
  }
}

// Audit Trail
async function loadAuditLogs() {
  const tableBody = document.getElementById('auditTableBody');
  if (!tableBody) return;

  try {
    const res = await fetch('/api/admin/database/audit?limit=100');
    if (!res.ok) throw new Error('Failed to load audit logs');
    const logs = await res.json();

    tableBody.innerHTML = '';
    if (logs.length === 0) {
      tableBody.innerHTML = '<tr><td colspan="6" style="text-align:center; color:var(--fui-colorNeutralForeground4); padding:2rem;">No audit records found.</td></tr>';
      return;
    }

    logs.forEach(l => {
      const tr = document.createElement('tr');
      const timeStr = new Date(l.timestamp).toLocaleString();
      let actionBadge = `<span class="badge badge-gray">${escapeHtml(l.action)}</span>`;
      if (l.action === 'RESTORE' || l.action === 'PRUNE') {
        actionBadge = `<span class="badge" style="background:rgba(239,68,68,0.2); color:#fca5a5; border:1px solid rgba(239,68,68,0.4);">${escapeHtml(l.action)}</span>`;
      } else if (l.action === 'BACKUP_CREATE') {
        actionBadge = `<span class="badge" style="background:rgba(16,185,129,0.2); color:#34d399; border:1px solid rgba(16,185,129,0.4);">${escapeHtml(l.action)}</span>`;
      }

      tr.innerHTML = `
        <td style="font-size:0.85rem; color:var(--fui-colorNeutralForeground3);">${timeStr}</td>
        <td style="font-weight:600; color:var(--fui-colorNeutralForeground1);">${escapeHtml(l.username)}</td>
        <td>${actionBadge}</td>
        <td style="font-family:var(--fui-fontFamilyMonospace); font-size:0.8rem; color:var(--fui-colorNeutralForeground2);">${escapeHtml(l.target || '—')}</td>
        <td style="font-size:0.8rem; color:var(--fui-colorNeutralForeground3); max-width:240px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${escapeHtml(l.detailsJson || '')}">
          ${escapeHtml(l.detailsJson || '—')}
        </td>
        <td style="font-family:var(--fui-fontFamilyMonospace); font-size:0.8rem; color:var(--fui-colorNeutralForeground4);">${escapeHtml(l.ipAddress || '—')}</td>
      `;
      tableBody.appendChild(tr);
    });
  } catch (err) {
    tableBody.innerHTML = `<tr><td colspan="6" style="text-align:center; color:#ef4444; padding:2rem;">Error loading audit logs: ${escapeHtml(err.message)}</td></tr>`;
  }
}

// Window Exports for Database Management UI
window.switchDbTab = switchDbTab;
window.initDatabaseManagement = initDatabaseManagement;
window.loadDatabaseData = loadDatabaseData;
window.loadStorageStats = loadStorageStats;
window.loadRetentionPolicies = loadRetentionPolicies;
window.loadBackups = loadBackups;
window.handleSaveRetention = handleSaveRetention;
window.openCreateBackupModal = openCreateBackupModal;
window.closeCreateBackupModal = closeCreateBackupModal;
window.handleCreateBackupSubmit = handleCreateBackupSubmit;
window.downloadBackup = downloadBackup;
window.deleteBackup = deleteBackup;
window.initiateRestoreModal = initiateRestoreModal;
window.handleExecuteRestore = handleExecuteRestore;
window.quickPruneTable = quickPruneTable;
window.handleDryRunPrune = handleDryRunPrune;
window.openConfirmPruneModal = openConfirmPruneModal;
window.closeConfirmPruneModal = closeConfirmPruneModal;
window.handleExecutePruneConfirmed = handleExecutePruneConfirmed;
window.runIntegrityCheck = runIntegrityCheck;
window.runCheckpoint = runCheckpoint;
window.runVacuum = runVacuum;
window.loadAuditLogs = loadAuditLogs;
