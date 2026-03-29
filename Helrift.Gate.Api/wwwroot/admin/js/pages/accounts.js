(function () {
    const { $, esc, fmtDate, api, linkCharDirect, linkGuild, pillSide } = Admin;

    const SIDE_NAMES = { 0: 'None', 1: 'Aresden', 2: 'Elvine', 3: 'Traveller' };

    function kv(label, value) {
        return `<tr><td>${esc(label)}</td><td>${value === null || value === undefined || value === '' ? '-' : esc(String(value))}</td></tr>`;
    }

    function kvHtml(label, html) {
        return `<tr><td>${esc(label)}</td><td>${html}</td></tr>`;
    }

    function section(title, content, collapsed) {
        const id = 'accsec_' + title.replace(/\W/g, '_');
        return `
            <div class="card" style="margin-bottom:12px;">
                <div class="card-title" style="cursor:pointer;" onclick="document.getElementById('${id}').classList.toggle('collapsed')">
                    ${esc(title)} <span style="font-size:11px; color:var(--dim);">▾</span>
                </div>
                <div id="${id}" class="${collapsed ? 'collapsed' : ''}">
                    ${content}
                </div>
            </div>`;
    }

    function renderCharacterRow(accountId, c) {
        const sideName = SIDE_NAMES[c.side] || String(c.side || '-');
        const guildName = c.guild?.guildName || '-';
        const guildId = c.guild?.guildId || '';
        const level = c.level ?? c.progression?.level ?? '-';
        const rebirths = c.progression?.rebirths ?? 0;
        const ekp = c.enemyKillPoints ?? 0;

        return `<tr>
            <td>${linkCharDirect(accountId, c.id, c.characterName || c.id)}</td>
            <td class="mono" style="font-size:11px;">${esc(c.id || '')}</td>
            <td>${level}${rebirths > 0 ? ` <span class="pill pill-orange">R${rebirths}</span>` : ''}</td>
            <td>${pillSide(sideName)}</td>
            <td>${esc(c.mapId || '-')}</td>
            <td>${ekp > 0 ? ekp.toLocaleString() : '-'}</td>
            <td>${guildId ? linkGuild(guildId) : esc(guildName)}</td>
            <td>${c.lastLoggedIn ? fmtDate(c.lastLoggedIn) : '-'}</td>
        </tr>`;
    }

    function renderCharacterSummary(accountId, chars) {
        if (!chars || !chars.length)
            return '<p style="color:var(--dim); font-size:12px;">No characters on this account.</p>';

        // Sort: highest level first
        const sorted = [...chars].sort((a, b) => (b.level || 0) - (a.level || 0));

        return `<div class="scroll-x"><table>
            <thead><tr><th>Name</th><th>Character ID</th><th>Level</th><th>Side</th><th>Map</th><th>EKP</th><th>Guild</th><th>Last Login</th></tr></thead>
            <tbody>${sorted.map(c => renderCharacterRow(accountId, c)).join('')}</tbody>
        </table></div>`;
    }

    function renderUnlockables(ids) {
        if (!ids || !ids.length) return '<p style="color:var(--dim); font-size:12px;">No unlockables.</p>';
        const sorted = [...ids].sort();
        return `<div class="scroll-x"><table>
            <thead><tr><th>#</th><th>Unlockable ID</th></tr></thead>
            <tbody>${sorted.map((u, i) => `<tr><td>${i + 1}</td><td class="mono" style="font-size:11px;">${esc(u)}</td></tr>`).join('')}</tbody>
        </table></div>`;
    }

    function renderAccount(acc) {
        const accountId = acc.id || '';
        const chars = acc.characters || [];

        // Aggregate stats across characters
        const totalChars = chars.length;
        const totalEkp = chars.reduce((sum, c) => sum + (c.enemyKillPoints || 0), 0);
        const highestLevel = chars.reduce((max, c) => Math.max(max, c.level || 0), 0);
        const totalRebirths = chars.reduce((sum, c) => sum + (c.progression?.rebirths || 0), 0);

        let html = '';

        // ── Account Info ──
        html += section('Account Info', `<table class="kv-table"><tbody>
            ${kv('Account ID', accountId)}
            ${kv('Username', acc.username)}
            ${kv('Email', acc.emailAddress)}
            ${kv('Last Login', acc.lastLogIn ? fmtDate(acc.lastLogIn) : '-')}
        </tbody></table>`);

        // ── Account Stats (aggregate) ──
        html += section('Account Summary', `
            <div class="stats-grid">
                <div class="stat-card"><div class="label">Characters</div><div class="value">${totalChars}</div></div>
                <div class="stat-card"><div class="label">Highest Level</div><div class="value">${highestLevel}</div></div>
                <div class="stat-card"><div class="label">Total Rebirths</div><div class="value">${totalRebirths}</div></div>
                <div class="stat-card"><div class="label">Total EKP</div><div class="value">${totalEkp.toLocaleString()}</div></div>
            </div>`);

        // ── Characters ──
        html += section('Characters (' + totalChars + ')', renderCharacterSummary(accountId, chars));

        // ── Owned Unlockables ──
        const unlockables = acc.ownedUnlockableIds || [];
        html += section('Owned Unlockables (' + unlockables.length + ')', renderUnlockables(unlockables), true);

        // ── Raw JSON ──
        html += section('Raw JSON', `<div class="json-box"><pre class="json-block">${esc(JSON.stringify(acc, null, 2))}</pre></div>`, true);

        return html;
    }

    async function doLookup() {
        const id = $('accountLookupId').value.trim();
        const st = $('accountStatus');
        const result = $('accountResult');
        st.textContent = ''; result.innerHTML = '';
        if (!id) { st.textContent = 'Enter an account ID.'; return; }
        st.textContent = 'Loading…';

        try {
            const res = await api(`/api/v1/accounts/${encodeURIComponent(id)}`);
            if (res.status === 404) { st.textContent = 'Account not found.'; return; }
            if (!res.ok) { st.textContent = `Error ${res.status}`; return; }
            const acc = await res.json();
            st.textContent = '';
            result.innerHTML = renderAccount(acc);
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    window.lookupAccount = doLookup;

    function onEnter(args) {
        if (args && args.accountId) {
            $('accountLookupId').value = args.accountId;
            doLookup();
        }
    }

    Admin.registerPage('accounts', { onEnter: onEnter });
})();