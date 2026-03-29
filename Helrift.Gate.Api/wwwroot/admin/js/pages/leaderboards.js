(function () {
    const { $, esc, fmtDate, api } = Admin;

    let _currentMetricKey = null;

    const WINDOW_NAMES = { 1: 'Daily', 2: 'Weekly', 3: 'Monthly' };
    const SIDE_NAMES = { 1: 'Aresden', 2: 'Elvine' };

    // ── Key list ──────────────────────────────────────

    async function loadKeyList() {
        const listEl = $('lbKeyList');
        listEl.innerHTML = '<p style="color:var(--dim); font-size:12px;">Loading…</p>';

        try {
            const res = await api('/admin/api/leaderboards/keys');
            if (!res.ok) { listEl.innerHTML = `<p style="color:var(--red);">Error ${res.status}</p>`; return; }
            const keys = await res.json();

            if (!keys.length) {
                listEl.innerHTML = '<p style="color:var(--dim); font-size:12px;">No leaderboard data in memory yet. Keys appear once game servers submit increments.</p>';
                return;
            }

            listEl.innerHTML = `<div style="display:flex; flex-wrap:wrap; gap:8px;">` +
                keys.map(k => `<button class="btn btn-primary" onclick="openLeaderboard('${esc(k)}')">${esc(k)}</button>`).join('') +
                `</div>`;
        } catch (e) { listEl.innerHTML = `<p style="color:var(--red);">Network error</p>`; console.error(e); }
    }

    // ── Drilldown ─────────────────────────────────────

    window.openLeaderboard = function (metricKey) {
        _currentMetricKey = metricKey;
        $('lbDrilldownTitle').textContent = metricKey;
        $('lbKeyList').style.display = 'none';
        $('lbDrilldownSection').style.display = '';
        loadRankings();
    };

    window.closeLbDrilldown = function () {
        _currentMetricKey = null;
        $('lbDrilldownSection').style.display = 'none';
        $('lbKeyList').style.display = '';
    };

    window.lbRefresh = loadRankings;

    async function loadRankings() {
        if (!_currentMetricKey) return;
        const side = parseInt($('lbSide').value, 10);
        const window = parseInt($('lbWindow').value, 10);
        const limit = parseInt($('lbLimit').value, 10) || 50;
        const tb = $('lbBody');
        tb.innerHTML = '<tr><td colspan="4" class="empty-row">Loading…</td></tr>';

        try {
            const params = new URLSearchParams({ realmId: 'default', side, window, limit });
            const res = await api(`/admin/api/leaderboards/${encodeURIComponent(_currentMetricKey)}?${params.toString()}`);
            if (!res.ok) { tb.innerHTML = `<tr><td colspan="4" class="empty-row">Error ${res.status}</td></tr>`; return; }
            const data = await res.json();

            $('lbMeta').innerHTML = `Realm: <strong>${esc(data.realmId || 'default')}</strong> · Side: <strong>${SIDE_NAMES[data.side] || data.side}</strong> · Window: <strong>${WINDOW_NAMES[data.window] || data.window}</strong> · Bucket: <strong>${data.bucketStartUtc ? fmtDate(data.bucketStartUtc) : '-'}</strong>`;

            const items = data.items || [];
            if (!items.length) { tb.innerHTML = '<tr><td colspan="4" class="empty-row">No entries.</td></tr>'; return; }

            tb.innerHTML = items.map(e => `
                <tr>
                    <td>${e.rank}</td>
                    <td class="mono" style="font-size:11px;">${esc(e.subjectId || '')}</td>
                    <td>${esc(e.displayName || '')}</td>
                    <td><strong>${(e.value || 0).toLocaleString()}</strong></td>
                </tr>`).join('');
        } catch (e) { tb.innerHTML = '<tr><td colspan="4" class="empty-row">Network error</td></tr>'; console.error(e); }
    }

    function onEnter(args) {
        $('lbDrilldownSection').style.display = 'none';
        $('lbKeyList').style.display = '';
        _currentMetricKey = null;
        loadKeyList();

        if (args && args.metricKey) {
            openLeaderboard(args.metricKey);
        }
    }

    Admin.registerPage('leaderboards', { onEnter: onEnter });
})();