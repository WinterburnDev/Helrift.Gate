(function () {
    const { $, esc, fmtDate, pillSide, linkGuild, api } = Admin;

    async function doSearch() {
        const q = $('guildSearch').value.trim();
        const side = $('guildSideFilter').value;
        const tb = $('guildsBody');
        tb.innerHTML = '<tr><td colspan="8" class="empty-row">Loading…</td></tr>';

        try {
            const params = new URLSearchParams();
            if (q) params.set('q', q);
            if (side) params.set('side', side);
            const res = await api(`/api/v1/guilds?${params.toString()}`);
            if (!res.ok) { tb.innerHTML = `<tr><td colspan="8" class="empty-row">Error ${res.status}</td></tr>`; return; }
            const guilds = await res.json();
            if (!guilds.length) { tb.innerHTML = '<tr><td colspan="8" class="empty-row">No guilds found.</td></tr>'; return; }

            tb.innerHTML = guilds.map(g => `
                <tr>
                    <td class="mono" style="font-size:11px;">${esc(g.guildId || '')}</td>
                    <td>${esc(g.name || '')}</td>
                    <td>${pillSide(g.side)}</td>
                    <td class="mono" style="font-size:11px;">${esc(g.leaderCharacterId || '')}</td>
                    <td>${(g.memberCharacterIds || []).length}</td>
                    <td>${g.createdAt ? fmtDate(g.createdAt) : '-'}</td>
                    <td style="max-width:200px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">${esc(g.motd || g.moTD || '-')}</td>
                    <td><button class="btn" onclick="Admin.switchPage('guild',{guildId:'${esc(g.guildId)}'})">View</button></td>
                </tr>`).join('');
        } catch (e) { tb.innerHTML = '<tr><td colspan="8" class="empty-row">Network error</td></tr>'; console.error(e); }
    }

    window.loadGuilds = doSearch;

    function onEnter(args) {
        if (args && args.searchQuery) {
            $('guildSearch').value = args.searchQuery;
            doSearch();
        }
    }

    Admin.registerPage('guilds', { onEnter: onEnter });
})();