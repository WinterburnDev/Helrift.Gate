(function () {
    const { $, esc, fmtUnix, fmtDate, pillState, pillSide, linkCharByName, fetchServers, fetchPlayers, fetchRealmState, api } = Admin;

    // CrusadePhase enum values from C# (int serialisation)
    const CRUSADE_PHASE = { 0: 'Setup', 1: 'Active', 2: 'Cleansing', 3: 'Ended' };
    const CRUSADE_PHASE_PILL = { 'Setup': 'pill-yellow', 'Active': 'pill-green', 'Cleansing': 'pill-orange', 'Ended': 'pill-neutral' };

    async function fetchLatestCrusadeSnapshot(realmId) {
        try {
            const res = await api(`/api/v1/realm-events/recent/${encodeURIComponent(realmId)}?limit=100`);
            if (!res.ok) return null;
            const events = await res.json();
            const snapshot = events.find(e => e.type === 'crusade.snapshot');
            if (!snapshot || !snapshot.payloadJson) return null;
            return JSON.parse(snapshot.payloadJson);
        } catch { return null; }
    }

    function manaBar(label, rawValue) {
        // Unity sends 0.0–1.0; multiply by 100 for display
        const pct = Math.min(100, Math.max(0, (rawValue || 0) * 100));
        const colour = label === 'Aresden' ? 'var(--accent)' : 'var(--purple)';
        return `
            <div style="margin-bottom:8px;">
                <div style="display:flex; justify-content:space-between; font-size:11px; color:var(--muted); margin-bottom:3px;">
                    <span>${esc(label)}</span><span>${pct.toFixed(1)}%</span>
                </div>
                <div style="height:8px; border-radius:4px; background:var(--border); overflow:hidden;">
                    <div style="height:100%; width:${pct}%; background:${colour}; transition:width .4s;"></div>
                </div>
            </div>`;
    }

    function resolvePhase(snap) {
        // Phase may be int (enum value) or string name depending on serialiser settings
        const raw = snap.Phase ?? snap.phase;
        if (typeof raw === 'number') return CRUSADE_PHASE[raw] ?? `Phase(${raw})`;
        if (typeof raw === 'string') {
            // Could be a string int "1" or a name "Active"
            const asInt = parseInt(raw, 10);
            if (!isNaN(asInt)) return CRUSADE_PHASE[asInt] ?? raw;
            return raw;
        }
        return 'Unknown';
    }

    function renderCrusade(snap) {
        if (!snap) {
            return `<p style="font-size:12px; color:var(--dim);">No crusade event received yet.</p>`;
        }

        const phase = resolvePhase(snap);
        const isOver = phase === 'Ended';
        const pillClass = CRUSADE_PHASE_PILL[phase] ?? 'pill-neutral';

        const buildings = snap.Buildings || snap.buildings || [];
        const destroyed = buildings.filter(b => b.IsDestroyed || b.isDestroyed);
        const manaA = snap.ManaPercentAresden ?? snap.manaPercentAresden ?? 0;
        const manaE = snap.ManaPercentElvine   ?? snap.manaPercentElvine   ?? 0;

        const buildingsByTown = buildings.reduce((acc, b) => {
            const id = (b.BuildingId || b.buildingId || '').toLowerCase();
            const town = id.startsWith('elvine') ? 'Elvine' : 'Aresden';
            acc[town] = acc[town] || [];
            acc[town].push(b);
            return acc;
        }, {});

        function buildingTable(bs) {
            if (!bs || !bs.length) return `<p style="font-size:11px; color:var(--dim);">None</p>`;
            return `<table><thead><tr><th>Building</th><th>Shield</th><th>Damage</th><th>Status</th></tr></thead><tbody>` +
                bs.map(b => {
                    const type  = b.BuildingType  ?? b.buildingType  ?? '-';
                    const shield = b.ShieldValue  ?? b.shieldValue   ?? 0;
                    const dmg   = b.Damage        ?? b.damage        ?? 0;
                    const dead  = b.IsDestroyed   || b.isDestroyed;
                    return `<tr>
                        <td>${esc(String(type))}</td>
                        <td>${shield}</td>
                        <td>${dmg}</td>
                        <td>${dead ? '<span class="pill pill-red">Destroyed</span>' : '<span class="pill pill-green">Standing</span>'}</td>
                    </tr>`;
                }).join('') + '</tbody></table>';
        }

        if (isOver) {
            return `
                <div style="display:flex; align-items:center; gap:10px; margin-bottom:8px;">
                    <span class="pill pill-neutral">Ended</span>
                    <span style="font-size:11px; color:var(--muted);">Last snapshot: ${snap.Utc ? fmtDate(snap.Utc) : '-'}</span>
                </div>
                <p style="font-size:12px; color:var(--dim);">Crusade has ended. Waiting for the next event.</p>`;
        }

        return `
            <div style="margin-bottom:8px; display:flex; align-items:center; gap:10px; flex-wrap:wrap;">
                <span class="pill ${pillClass}">${esc(phase)}</span>
                <span style="font-size:11px; color:var(--muted);">Seq: ${snap.Sequence ?? snap.sequence ?? '-'}</span>
                <span style="font-size:11px; color:var(--muted);">${snap.Utc ? fmtDate(snap.Utc) : ''}</span>
                ${destroyed.length > 0 ? `<span style="font-size:11px; color:var(--red);">⚠ ${destroyed.length} building(s) destroyed</span>` : ''}
            </div>
            ${manaBar('Aresden', manaA)}
            ${manaBar('Elvine', manaE)}
            <div class="inline-grid-2" style="margin-top:12px; gap:12px;">
                <div>
                    <div style="font-size:11px; font-weight:600; color:var(--muted); margin-bottom:6px; text-transform:uppercase; letter-spacing:.06em;">Aresden Buildings</div>
                    ${buildingTable(buildingsByTown['Aresden'])}
                </div>
                <div>
                    <div style="font-size:11px; font-weight:600; color:var(--muted); margin-bottom:6px; text-transform:uppercase; letter-spacing:.06em;">Elvine Buildings</div>
                    ${buildingTable(buildingsByTown['Elvine'])}
                </div>
            </div>`;
    }

    async function refresh() {
        const [servers, players, realm, crusade] = await Promise.all([
            fetchServers(), fetchPlayers(), fetchRealmState(),
            fetchLatestCrusadeSnapshot('default')
        ]);

        const aresden = players.filter(p => (p.side || '').toLowerCase() === 'aresden').length;
        const elvine  = players.filter(p => (p.side || '').toLowerCase() === 'elvine').length;
        const neutral = players.length - aresden - elvine;
        const onlineServers = servers.filter(s => (s.state || '').toLowerCase() === 'online').length;

        $('dashStats').innerHTML = `
    <div class="stat-card stat-card-players">
        <div class="label">Players Online</div>
        <div class="players-online-layout">
            <div class="players-online-total">
                <div class="value">${players.length}</div>
            </div>
            <div class="players-online-breakdown">
                <div class="players-online-row">${pillSide('Aresden')}<span class="players-online-count">${aresden ?? 0}</span></div>
                <div class="players-online-row">${pillSide('Elvine')}<span class="players-online-count">${elvine ?? 0}</span></div>
                <div class="players-online-row">${pillSide('Traveller')}<span class="players-online-count">${neutral ?? 0}</span></div>
            </div>
        </div>
    </div>
    <div class="stat-card"><div class="label">Game Servers</div><div class="value">${onlineServers} / ${servers.length}</div><div class="sub">${onlineServers === servers.length ? 'All healthy' : (servers.length - onlineServers) + ' offline'}</div></div>
    <div class="stat-card"><div class="label">Deny Logins</div><div class="value">${realm?.denyNewLogins ? '🔴 Yes' : '🟢 No'}</div></div>
    <div class="stat-card"><div class="label">Shutdown Scheduled</div><div class="value">${realm?.shutdownAtUnixUtc ? '⚠️ Yes' : '✅ No'}</div>${realm?.shutdownAtUnixUtc ? `<div class="sub">${fmtUnix(realm.shutdownAtUnixUtc)}</div>` : ''}</div>
    <div class="stat-card dash-crusade-card">
        <div class="label">Active Crusade</div>
        <div id="dashCrusade"><p style="font-size:12px; color:var(--dim);">Loading…</p></div>
    </div>
`;

        if (realm?.shutdownAtUnixUtc) {
            $('realmAlert').innerHTML = `<div class="alert alert-warn">⚠️ Shutdown scheduled at ${fmtUnix(realm.shutdownAtUnixUtc)}${realm.realmMessage ? ' — ' + esc(realm.realmMessage) : ''}</div>`;
        } else if (realm?.denyNewLogins) {
            $('realmAlert').innerHTML = `<div class="alert alert-danger">🔴 Realm is in maintenance mode — new logins are denied.</div>`;
        } else {
            $('realmAlert').innerHTML = '';
        }

        const stb = $('dashServersBody');
        stb.innerHTML = !servers.length
            ? `<tr><td colspan="2" class="empty-row">No game servers</td></tr>`
            : servers.map(s => `<tr><td class="mono">${esc(s.id)}</td><td>${pillState(s.state)}</td></tr>`).join('');

        const sorted = [...players].sort((a, b) => (a.characterName || '').localeCompare(b.characterName || ''));
        const ptb = $('dashPlayersBody');
        ptb.innerHTML = !sorted.length
            ? `<tr><td colspan="3" class="empty-row">No players online</td></tr>`
            : sorted.slice(0, 20).map(p =>
                `<tr><td>${linkCharByName(p.characterName)}</td><td class="mono">${esc(p.gameServerId || '')}</td><td>${pillSide(p.side)}</td></tr>`
            ).join('') + (sorted.length > 20 ? `<tr><td colspan="3" class="empty-row">+${sorted.length - 20} more…</td></tr>` : '');

        const onlineMaps = [];
        servers.forEach(s => {
            if ((s.state || '').toLowerCase() !== 'online') return;
            (s.maps || []).forEach(m => {
                onlineMaps.push({
                    serverId: s.id || '',
                    mapName: m.mapName || '',
                    mapId: m.id || '',
                    sceneName: m.sceneName || '',
                    cellX: m.cellX,
                    cellY: m.cellY,
                    isSafeMap: !!m.isSafeMap,
                    isOutside: !!m.isOutside
                });
            });
        });

        onlineMaps.sort((a, b) => {
            const byName = (a.mapName || '').localeCompare(b.mapName || '');
            if (byName !== 0) return byName;
            return (a.serverId || '').localeCompare(b.serverId || '');
        });

        const mtb = $('dashMapsBody');
        if (mtb) {
            mtb.innerHTML = !onlineMaps.length
                ? `<tr><td colspan="4" class="empty-row">No online maps reported.</td></tr>`
                : onlineMaps.map(r => `
                    <tr>
                        <td>${esc(r.mapName || r.mapId || '-')}</td>
                        <td class="mono">${Number.isFinite(r.cellX) && Number.isFinite(r.cellY) ? `${r.cellX}, ${r.cellY}` : '-'}</td>
                        <td>
                            <span class="pill ${r.isSafeMap ? 'pill-green' : 'pill-neutral'}">${r.isSafeMap ? 'Safe' : 'Combat'}</span>
                            <span class="pill ${r.isOutside ? 'pill-green' : 'pill-neutral'}">${r.isOutside ? 'Outside' : 'Inside'}</span>
                        </td>
                        <td><a class="admin-link" onclick="viewGameServer('${esc(r.serverId)}')">${esc(r.serverId)}</a></td>
                    </tr>`).join('');
        }

        const cc = $('dashCrusade');
        if (cc) cc.innerHTML = renderCrusade(crusade);
    }

    Admin.registerPage('dashboard', { onEnter: refresh, onTick: refresh });
})();