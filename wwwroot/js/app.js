/**
 * KestrelScope — Sovereign Observability Monolith
 * Frontend Application Client
 */

// ============================================================================
// Auth & User Management
// ============================================================================
async function checkAuth(isLoginPage = false) {
  try {
    const res = await fetch('/api/auth/me');
    const data = await res.json();

    if (isLoginPage) {
      if (data.isAuthenticated) {
        window.location.href = '/dashboard.html';
      }
      return;
    }

    if (!data.isAuthenticated) {
      window.location.href = '/login.html';
      return;
    }

    const avatarEl = document.getElementById('userAvatar');
    if (avatarEl && data.username) {
      avatarEl.textContent = data.username.substring(0, 2).toUpperCase();
      avatarEl.title = `Logged in as ${data.username}`;
    }
  } catch (err) {
    console.error('Auth verification failed', err);
    if (!isLoginPage) window.location.href = '/login.html';
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

      // Format time labels based on the selected window
      const labels = series.map(pt => {
        let d = new Date(pt.timestamp);
        if (isNaN(d.getTime())) {
          const cleaned = pt.timestamp.replace(/(\.\d{3})\d+Z$/, '$1Z');
          d = new Date(cleaned);
        }
        return formatDate(d, minutes);
      });

      const values = series.map(pt => pt.value);

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
    if (minutes > 15) {
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
  await checkAuth();

  const tbody = document.getElementById('traceTableBody');
  const searchInput = document.getElementById('traceSearch');
  const timelineSpans = document.getElementById('timelineSpans');
  const timelineTitle = document.getElementById('timelineTitle');

  const urlParams = new URLSearchParams(window.location.search);
  const paramTraceId = urlParams.get('traceId');

  let tracesData = [];

  async function loadTraces() {
    try {
      const res = await fetch('/api/traces?minutes=120&limit=100');
      tracesData = await res.json();
      renderTraceTable(tracesData);
      if (paramTraceId) {
        selectTrace(paramTraceId);
      } else if (tracesData.length > 0) {
        selectTrace(tracesData[0].traceId);
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
      if (paramTraceId === span.traceId) {
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
        document.querySelectorAll('.trace-table tr').forEach(r => r.classList.remove('selected'));
        tr.classList.add('selected');
        selectTrace(span.traceId);
      });

      tbody.appendChild(tr);
    });
  }

  async function selectTrace(traceId) {
    timelineTitle.textContent = `Trace Timeline: ${traceId.substring(0, 8)}`;
    const traceActions = document.getElementById('traceActions');
    if (traceActions) {
      traceActions.innerHTML = `<a href="/logs.html?traceId=${encodeURIComponent(traceId)}" class="trace-pill" style="font-size:0.8rem; padding:0.35rem 0.75rem;">📜 View Correlated Logs</a>`;
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
  await checkAuth();

  const alertGrid = document.getElementById('alertGrid');
  const modal = document.getElementById('alertModal');
  const openBtn = document.getElementById('btnNewAlert');
  const cancelBtn = document.getElementById('btnCancelModal');
  const alertForm = document.getElementById('alertForm');

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
      alertGrid.innerHTML = '<div style="grid-column: 1/-1; text-align:center; color:#64748b; padding:3rem;">No alert rules defined. Click "New Alert" to configure one.</div>';
      return;
    }

    rules.forEach(rule => {
      const card = document.createElement('div');
      const isActive = rule.isEnabled === 1;
      card.className = `alert-card ${isActive ? 'active-rule' : 'disabled-rule'}`;

      const badge = isActive
        ? '<span class="badge badge-ok">Active</span>'
        : '<span class="badge badge-gray">Disabled</span>';

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
        <div class="alert-card-actions">
          <button class="btn-secondary btn-sm" onclick="toggleAlert(${rule.id})">
            ${isActive ? 'Disable' : 'Enable'}
          </button>
          <button class="btn-secondary btn-sm" style="border-color: rgba(239,68,68,0.4); color: #fca5a5;" onclick="deleteAlert(${rule.id})">
            Delete
          </button>
        </div>
      `;

      alertGrid.appendChild(card);
    });
  }

  // Modal handlers
  openBtn.addEventListener('click', () => modal.classList.add('open'));
  cancelBtn.addEventListener('click', () => modal.classList.remove('open'));
  modal.addEventListener('click', (e) => {
    if (e.target === modal) modal.classList.remove('open');
  });

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
      }
    } catch (err) {
      console.error('Failed to create alert rule', err);
    }
  });

  window.toggleAlert = async (id) => {
    try {
      await fetch(`/api/alerts/${id}/toggle`, { method: 'PATCH' });
      await loadAlerts();
    } catch (err) {
      console.error('Failed to toggle alert', err);
    }
  };

  window.deleteAlert = async (id) => {
    if (!confirm('Are you sure you want to delete this alert rule?')) return;
    try {
      await fetch(`/api/alerts/${id}`, { method: 'DELETE' });
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
  await checkAuth();

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
        <td style="font-family:ui-monospace, monospace; font-size:0.8rem; color:#94a3b8; white-space:nowrap;">${timeStr}</td>
        <td><span class="log-badge ${badgeClass}">${sev}</span></td>
        <td style="white-space:nowrap;"><span style="font-weight:600; color:#e2e8f0;">${escapeHtml(log.serviceName)}</span></td>
        <td class="log-body-cell">
          <div>${escapeHtml(log.body)}</div>
          ${attrsHtml}
        </td>
        <td style="white-space:nowrap;">${traceCol}</td>
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

