(function () {
    const { $, esc, fmtDate, api, pillSide, linkCharByName } = Admin;

    function kv(label, value) {
        return `<tr><td>${esc(label)}</td><td>${value == null || value === '' ? '-' : esc(String(value))}</td></tr>`;
    }

    function kvHtml(label, html) {
        return `<tr><td>${esc(label)}</td><td>${html}</td></tr>`;
    }

    function section(title, content, collapsed) {
        const id = 'guildsec_' + title.replace(/\W/g, '_');
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

    function renderMembers(members) {
        if (!members || !members.length)
            return '<p style="color:var(--dim); font-size:12px;">No members.</p>';

        return `<div class="scroll-x"><table>
            <thead><tr><th>#</th><th>Character</th><th>Character ID</th><th>Role</th></tr></thead>
            <tbody>${members.map((m, i) => `
                <tr>
                    <td>${i + 1}</td>
                    <td>${m.characterName ? linkCharByName(m.characterName) : esc(m.characterId)}</td>
                    <td class="mono" style="font-size:11px;">${esc(m.characterId)}</td>
                    <td>${m.isLeader ? '<span class="pill pill-yellow">Leader</span>' : '<span class="pill pill-neutral">Member</span>'}</td>
                </tr>`).join('')}
            </tbody></table></div>`;
    }

    function renderEmblem(emblem) {
        if (!emblem || !Object.keys(emblem).length)
            return '<p style="color:var(--dim); font-size:12px;">No emblem data.</p>';
        return `<div class="scroll-x"><table><thead><tr><th>Property</th><th>Value</th></tr></thead><tbody>` +
            Object.entries(emblem).sort((a, b) => a[0].localeCompare(b[0])).map(([k, v]) =>
                `<tr><td>${esc(k)}</td><td>${esc(typeof v === 'object' ? JSON.stringify(v) : String(v))}</td></tr>`
            ).join('') + '</tbody></table></div>';
    }

    function renderGuild(g) {
        const memberCount = (g.members || []).length;
        let html = '';

        html += section('Guild Info', `<table class="kv-table"><tbody>
            ${kv('Guild ID', g.guildId)}
            ${kv('Name', g.name)}
            ${kvHtml('Side', pillSide(g.side))}
            ${kv('Leader Character ID', g.leaderCharacterId)}
            ${kvHtml('Leader Name', g.leaderCharacterName ? linkCharByName(g.leaderCharacterName) : '-')}
            ${kv('Created', g.createdAt ? fmtDate(g.createdAt) : '-')}
            ${kv('MOTD', g.motd || '-')}
            ${kv('Description', g.description || '-')}
        </tbody></table>`);

        html += section('Summary', `
            <div class="stats-grid">
                <div class="stat-card"><div class="label">Members</div><div class="value">${memberCount}</div></div>
                <div class="stat-card"><div class="label">Side</div><div class="value" style="font-size:16px;">${pillSide(g.side)}</div></div>
            </div>`);

        html += section('Members (' + memberCount + ')', renderMembers(g.members));
        html += section('Emblem', renderEmblem(g.emblem), true);
        html += section('Raw JSON', `<div class="json-box"><pre class="json-block">${esc(JSON.stringify(g, null, 2))}</pre></div>`, true);

        return html;
    }

    async function loadGuild(args) {
        const container = $('guildDetailContent');
        if (!args || !args.guildId) {
            container.innerHTML = '<div class="empty-row">No guild specified.</div>';
            return;
        }

        container.innerHTML = '<div class="empty-row">Loading…</div>';

        try {
            // Use admin endpoint which resolves member names
            const res = await api(`/admin/api/guilds/${encodeURIComponent(args.guildId)}`);
            if (res.status === 404) { container.innerHTML = '<div class="empty-row">Guild not found.</div>'; return; }
            if (!res.ok) { container.innerHTML = `<div class="empty-row">Error ${res.status}</div>`; return; }
            const g = await res.json();

            container.innerHTML = renderGuild(g);
            $('pageTitle').textContent = 'Guild: ' + (g.name || g.guildId);
        } catch (e) {
            container.innerHTML = '<div class="empty-row">Network error.</div>';
            console.error(e);
        }
    }

    Admin.registerPage('guild', { onEnter: loadGuild });
})();