(function () {
    const { $, esc, pillState, fmtUnix, fmtDate, fetchServers } = Admin;

    window.viewGameServer = function (id) {
        Admin.switchPage('server', { id: id });
    };

    function renderServerList(servers) {
        const tb = $('serversBody');

        if (!servers.length) {
            tb.innerHTML = `<tr><td colspan="7" class="empty-row">No game servers found</td></tr>`;
            return;
        }

        const sorted = [...servers].sort((a, b) => (a.id || '').localeCompare(b.id || ''));

        tb.innerHTML = sorted.map(s => `
            <tr>
                <td class="mono">${esc(s.id || '')}</td>
                <td>${pillState(s.state)}</td>
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