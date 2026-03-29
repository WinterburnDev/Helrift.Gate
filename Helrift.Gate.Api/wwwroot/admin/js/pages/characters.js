(function () {
    const { $, esc, fmtUnix, pillSide, linkAccount, linkCharDirect, api } = Admin;

    async function doSearch() {
        const name = $('charSearchName').value.trim();
        const st = $('charSearchStatus');
        st.textContent = '';
        if (!name) { st.textContent = 'Enter a character name.'; return; }
        st.textContent = 'Searching…';

        // Refresh presence cache so badges are current
        await Admin.fetchPlayers();

        try {
            const res = await api('/admin/api/characters/search?name=' + encodeURIComponent(name));
            if (!res.ok) { st.textContent = `Error ${res.status}`; return; }
            const results = await res.json();
            st.textContent = '';
            $('charResultCount').textContent = results.length;

            const tb = $('charResultsBody');
            if (!results.length) { tb.innerHTML = '<tr><td colspan="8" class="empty-row">No characters found.</td></tr>'; return; }
            results.sort((a, b) => (a.name || '').localeCompare(b.name || ''));
            tb.innerHTML = results.map(c => `
                <tr>
                    <td>${linkCharDirect(c.accountId, c.characterId, c.name)}</td>
                    <td>${esc(c.realmId || 'default')}</td>
                    <td class="mono" style="font-size:11px;">${linkAccount(c.accountId)}</td>
                    <td class="mono" style="font-size:11px;">${esc(c.characterId || '')}</td>
                    <td>${pillSide(c.side)}</td>
                    <td>${c.level != null ? c.level : '-'}</td>
                    <td>${c.lastSeenUnixUtc ? fmtUnix(c.lastSeenUnixUtc) : '-'}</td>
                    <td><button class="btn" onclick="viewCharacter('${esc(c.accountId)}','${esc(c.characterId)}')">View</button></td>
                </tr>`).join('');
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    window.searchCharacters = doSearch;

    window.viewCharacter = function (accountId, charId) {
        Admin.switchPage('character', { accountId, charId });
    };

    function onEnter(args) {
        if (args && args.searchName) {
            $('charSearchName').value = args.searchName;
            doSearch();
        }
    }

    Admin.registerPage('characters', { onEnter: onEnter });
})();