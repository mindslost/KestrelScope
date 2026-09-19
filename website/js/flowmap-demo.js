/* ==========================================================================
   KestrelScope — Interactive Application Flow Map Demo
   Simulates dynamic service dependency topology with animated particle requests
   ========================================================================== */

(function () {
  const nodes = [
    { id: 'gateway', name: 'api-gateway', x: 70, y: 180, r: 36, status: 'normal', rps: '482 req/s', latency: '12ms', errorRate: '0.04%', instances: 3 },
    { id: 'order', name: 'order-service', x: 230, y: 120, r: 34, status: 'normal', rps: '310 req/s', latency: '48ms', errorRate: '0.12%', instances: 2 },
    { id: 'payment', name: 'payment-api', x: 400, y: 70, r: 32, status: 'warning', rps: '185 req/s', latency: '198ms', errorRate: '3.42%', instances: 2 },
    { id: 'inventory', name: 'inventory-svc', x: 400, y: 220, r: 32, status: 'normal', rps: '290 req/s', latency: '24ms', errorRate: '0.00%', instances: 2 },
    { id: 'notification', name: 'notifier-worker', x: 230, y: 280, r: 30, status: 'normal', rps: '95 req/s', latency: '8ms', errorRate: '0.00%', instances: 1 },
    { id: 'postgres', name: 'primary-db', x: 550, y: 140, r: 32, status: 'normal', rps: '620 ops/s', latency: '6ms', errorRate: '0.01%', instances: 1 }
  ];

  const edges = [
    { from: 'gateway', to: 'order', label: 'HTTP 200' },
    { from: 'gateway', to: 'notification', label: 'gRPC' },
    { from: 'order', to: 'payment', label: 'HTTP POST' },
    { from: 'order', to: 'inventory', label: 'gRPC' },
    { from: 'payment', to: 'postgres', label: 'TCP / 5432' },
    { from: 'inventory', to: 'postgres', label: 'TCP / 5432' }
  ];

  let selectedNode = nodes[1]; // default to order-service

  function initFlowMap() {
    const svg = document.getElementById('flowmapCanvas');
    if (!svg) return;

    svg.innerHTML = `
      <defs>
        <!-- Grid pattern -->
        <pattern id="grid" width="24" height="24" patternUnits="userSpaceOnUse">
          <path d="M 24 0 L 0 0 0 24" fill="none" stroke="rgba(255,255,255,0.03)" stroke-width="1"/>
        </pattern>
        <!-- Gradients -->
        <linearGradient id="edgeGrad" x1="0%" y1="0%" x2="100%" y2="0%">
          <stop offset="0%" stop-color="#0f6cbd" stop-opacity="0.8"/>
          <stop offset="100%" stop-color="#2886de" stop-opacity="0.4"/>
        </linearGradient>
        <filter id="glowBlue" x="-20%" y="-20%" width="140%" height="140%">
          <feDropShadow dx="0" dy="0" stdDeviation="6" flood-color="#2886de" flood-opacity="0.5"/>
        </filter>
        <filter id="glowWarning" x="-20%" y="-20%" width="140%" height="140%">
          <feDropShadow dx="0" dy="0" stdDeviation="6" flood-color="#f59e0b" flood-opacity="0.7"/>
        </filter>
      </defs>
      <rect width="100%" height="100%" fill="url(#grid)"/>
      <g id="flowmapEdges"></g>
      <g id="flowmapParticles"></g>
      <g id="flowmapNodes"></g>
    `;

    renderEdges();
    renderNodes();
    startParticleAnimations();
    updateDrawer(selectedNode);
  }

  function renderEdges() {
    const edgeGroup = document.getElementById('flowmapEdges');
    if (!edgeGroup) return;

    edgeGroup.innerHTML = edges.map(edge => {
      const src = nodes.find(n => n.id === edge.from);
      const dst = nodes.find(n => n.id === edge.to);
      const pathD = `M ${src.x} ${src.y} Q ${(src.x + dst.x) / 2} ${(src.y + dst.y) / 2 - 10} ${dst.x} ${dst.y}`;
      return `
        <path d="${pathD}" fill="none" stroke="rgba(40, 134, 222, 0.35)" stroke-width="2" stroke-dasharray="4 4" class="flow-path" id="path-${edge.from}-${edge.to}" />
      `;
    }).join('');
  }

  function renderNodes() {
    const nodeGroup = document.getElementById('flowmapNodes');
    if (!nodeGroup) return;

    nodeGroup.innerHTML = nodes.map(node => {
      const isSelected = selectedNode && selectedNode.id === node.id;
      let strokeColor = '#2886de';
      let fillColor = '#171f2f';
      let badgeColor = '#10b981';

      if (node.status === 'warning') {
        strokeColor = '#f59e0b';
        badgeColor = '#f59e0b';
      } else if (node.status === 'critical') {
        strokeColor = '#ef4444';
        badgeColor = '#ef4444';
      }

      const glowAttr = isSelected ? 'filter="url(#glowBlue)"' : '';

      return `
        <g class="flow-node" data-id="${node.id}" style="cursor: pointer;" transform="translate(0, 0)">
          <circle cx="${node.x}" cy="${node.y}" r="${node.r + (isSelected ? 4 : 0)}" 
            fill="${fillColor}" stroke="${strokeColor}" stroke-width="${isSelected ? 3 : 1.8}" ${glowAttr} />
          
          <!-- Status dot -->
          <circle cx="${node.x + node.r - 8}" cy="${node.y - node.r + 8}" r="5" fill="${badgeColor}">
            <animate attributeName="opacity" values="0.6;1;0.6" dur="2s" repeatCount="indefinite"/>
          </circle>

          <!-- Label -->
          <text x="${node.x}" y="${node.y + 4}" text-anchor="middle" fill="#ffffff" font-size="11" font-weight="600" font-family="'Inter', sans-serif">
            ${node.name.length > 11 ? node.name.substring(0, 9) + '…' : node.name}
          </text>
          <text x="${node.x}" y="${node.y + node.r + 14}" text-anchor="middle" fill="#94a3b8" font-size="10" font-family="'JetBrains Mono', monospace">
            ${node.latency}
          </text>
        </g>
      `;
    }).join('');

    // Attach click listeners
    const nodeElements = nodeGroup.querySelectorAll('.flow-node');
    nodeElements.forEach(el => {
      el.addEventListener('click', () => {
        const id = el.getAttribute('data-id');
        selectedNode = nodes.find(n => n.id === id);
        renderNodes();
        updateDrawer(selectedNode);
      });
    });
  }

  function startParticleAnimations() {
    const particleGroup = document.getElementById('flowmapParticles');
    if (!particleGroup) return;

    particleGroup.innerHTML = edges.map((edge, i) => {
      const src = nodes.find(n => n.id === edge.from);
      const dst = nodes.find(n => n.id === edge.to);
      const dur = (1.5 + (i * 0.3)).toFixed(1) + 's';
      return `
        <circle r="3" fill="#38bdf8" filter="url(#glowBlue)">
          <animateMotion path="M ${src.x} ${src.y} Q ${(src.x + dst.x) / 2} ${(src.y + dst.y) / 2 - 10} ${dst.x} ${dst.y}"
            dur="${dur}" repeatCount="indefinite" />
        </circle>
      `;
    }).join('');
  }

  function updateDrawer(node) {
    if (!node) return;
    const nameEl = document.getElementById('drawerServiceName');
    const statusEl = document.getElementById('drawerStatusBadge');
    const rpsEl = document.getElementById('drawerRps');
    const latencyEl = document.getElementById('drawerLatency');
    const errorEl = document.getElementById('drawerErrorRate');
    const instEl = document.getElementById('drawerInstances');

    if (nameEl) nameEl.textContent = node.name;
    if (statusEl) {
      statusEl.textContent = node.status.toUpperCase();
      statusEl.className = 'brand-badge ' + (node.status === 'warning' ? 'badge-warning' : 'badge-normal');
      if (node.status === 'warning') {
        statusEl.style.borderColor = 'rgba(245, 158, 11, 0.4)';
        statusEl.style.color = '#f59e0b';
        statusEl.style.background = 'rgba(245, 158, 11, 0.15)';
      } else {
        statusEl.style.borderColor = 'rgba(16, 185, 129, 0.4)';
        statusEl.style.color = '#10b981';
        statusEl.style.background = 'rgba(16, 185, 129, 0.15)';
      }
    }
    if (rpsEl) rpsEl.textContent = node.rps;
    if (latencyEl) latencyEl.textContent = node.latency;
    if (errorEl) errorEl.textContent = node.errorRate;
    if (instEl) instEl.textContent = node.instances;
  }

  window.initFlowMap = initFlowMap;
})();
