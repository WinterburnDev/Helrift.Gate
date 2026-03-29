(function () {
    const { $, esc, pillSide, linkCharByName, api } = Admin;

    async function load() {
        const side = $('partySideFilter').value;
        const tb = $('partiesBody');
        tb.innerHTML = '<tr><td colspan="7" class="empty-row">Loading…</td></tr>';
        $('partyDetail').innerHTML = '';

        try {
            const res = await api(`/admin/api/parties?side=${side}`);
            if (!res.ok) { tb.innerHTML = `<tr><td colspan="7" class="empty-row">Error ${res.status}: ${await res.text()}</td></tr>`; return; }
            const parties = await res.json();
            if (!parties.length) { tb.innerHTML = '<tr><td colspan="7" class="empty-row">No active parties.</td></tr>'; return; }

            tb.innerHTML = parties.map(p => `
                <tr style="cursor:pointer;" onclick='showPartyDetail(${esc(JSON.stringify(JSON.stringify(p)))})'>
                    <td class="mono" style="font-size:11px;">${esc(p.partyId || '')}</td>
                    <td>${esc(p.partyName || '')}</td>
                    <td class="mono" style="font-size:11px;">${esc(p.leaderCharacterId || '')}</td>
                    <td>${pillSide(p.side)}</td>
                    <td><span class="pill pill-neutral">${esc(p.visibility || '')}</span></td>
                    <td>${p.expMode != null ? p.expMode : '-'}</td>
                    <td>${(p.members || []).length}</td>
                </tr>`).join('');
        } catch (e) { tb.innerHTML = '<tr><td colspan="7" class="empty-row">Network error</td></tr>'; console.error(e); }
    }

    window.loadParties = load;

    window.showPartyDetail = function (jsonStr) {
        const p = JSON.parse(jsonStr);
        const members = (p.members || []).map(m => `
            <tr>
                <td>${linkCharByName(m.characterName)}</td>
                <td class="mono" style="font-size:11px;">${esc(m.characterId || '')}</td>
                <td>${m.isOnline ? '<span class="pill pill-green">Online</span>' : '<span class="pill pill-red">Offline</span>'}</td>
                <td class="mono">${esc(m.currentServerId || '-')}</td>
            </tr>`).join('');

        $('partyDetail').innerHTML = `
            <div class="card">
                <div class="card-title">Party: ${esc(p.partyName || p.partyId)}</div>
                <table>
                    <thead><tr><th>Name</th><th>Character ID</th><th>Status</th><th>Server</th></tr></thead>
                    <tbody>${members || '<tr><td colspan="4" class="empty-row">No members</td></tr>'}</tbody>
                </table>
            </div>`;
    };

    Admin.registerPage('parties', { onEnter: load });
})();