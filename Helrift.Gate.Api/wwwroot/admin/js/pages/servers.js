(function () {
    const { $, esc, pillState, fmtUnix, fmtDate, fetchServers } = Admin;

    function weatherSummary(weather) {
        if (!weather || !weather.weatherKind) {
            return '<span class="pill pill-neutral">Unknown</span>';
        }

        const pct = Math.max(0, Math.min(100, (weather.intensity01 || 0) * 100));
        return `<span class="pill pill-blue">${esc(weather.weatherKind)}</span> <span class="mono">${pct.toFixed(0)}%</span>`;
    }

    window.viewGameServer = function (id) {
        Admin.switchPage('server', { id: id });
    };

    function renderServerList(servers) {
        const tb = $('serversBody');

        if (!servers.length) {
            tb.innerHTML = `<tr><td colspan="8" class="empty-row">No game servers found</td></tr>`;
            return;
        }

        const sorted = [...servers].sort((a, b) => (a.id || '').localeCompare(b.id || ''));

        tb.innerHTML = sorted.map(s => `
            <tr>
                <td class="mono">${esc(s.id || '')}</td>
                <td>${pillState(s.state)}</td>
                <td>${weatherSummary(s.weather)}</td>
                <td class="mono">${esc(s.buildVersion || '-')}</td>
                <td>${s.mapCount ?? 0}</td>
                <td>${s.registeredAtUnixUtc ? fmtUnix(s.registeredAtUnixUtc) : '-'}</td>
                <td>${s.lastHeartbeatUtc ? fmtDate(s.lastHeartbeatUtc) : '-'}</td>
                <td><button class="btn" onclick="viewGameServer('${esc(s.id || '')}')">View</button></td>
            </tr>`).join('');
    }

    async function load() {
        const servers = await fetchServers();
        renderServerList(servers);
    }

    Admin.registerPage('servers', { onEnter: load, onTick: load });
})();