(function () {
    const { $, esc, fmtDate, pillSide, linkCharByName, fetchPlayers } = Admin;

    async function load() {
        const players = await fetchPlayers();

        let aresden = 0, elvine = 0, other = 0;
        players.forEach(p => {
            const s = (p.side || '').toLowerCase();
            if (s === 'aresden') aresden++; else if (s === 'elvine') elvine++; else other++;
        });
        $('presTotal').textContent = players.length;
        $('presAresden').textContent = aresden;
        $('presElvine').textContent = elvine;
        $('presOther').textContent = other;

        const tb = $('presBody');
        if (!players.length) { tb.innerHTML = '<tr><td colspan="5" class="empty-row">No players online</td></tr>'; return; }

        const sorted = [...players].sort((a, b) => (a.characterName || '').localeCompare(b.characterName || ''));
        tb.innerHTML = sorted.map(p => `
            <tr>
                <td>${linkCharByName(p.characterName)}</td>
                <td class="mono" style="font-size:11px;">${esc(p.characterId || '')}</td>
                <td class="mono">${esc(p.gameServerId || '')}</td>
                <td>${pillSide(p.side)}</td>
                <td>${p.lastSeenUtc ? fmtDate(p.lastSeenUtc) : '-'}</td>
            </tr>`).join('');
    }

    Admin.registerPage('presence', { onEnter: load, onTick: load });
})();