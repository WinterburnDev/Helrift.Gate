(function () {
    const { $, esc, fmtUnix, fmtDate, pillState, pillSide, linkCharByName, fetchServers, fetchPlayers, fetchRealmState, api } = Admin;

    // CrusadePhase enum values from C# (int serialisation)
    const CRUSADE_PHASE = { 0: 'Setup', 1: 'Active', 2: 'Cleansing', 3: 'Ended' };
    const CRUSADE_PHASE_PILL = { 'Setup': 'pill-yellow', 'Active': 'pill-green', 'Cleansing': 'pill-orange', 'Ended': 'pill-neutral' };

    // TownProjectStatus enum values from C#
    const PROJECT_STATUS_LABEL = { 1: 'Active', 2: 'Complete (Pending)', 3: 'Activated', 4: 'Failed', 5: 'Expired' };
    const PROJECT_STATUS_PILL  = { 1: 'pill-green', 2: 'pill-yellow', 3: 'pill-neutral', 4: 'pill-red', 5: 'pill-neutral' };

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

    async function fetchTownProjectState(townId) {
        try {
            const res = await api(`/admin/api/towns/${encodeURIComponent(townId)}/projects/state`);
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    function fmtRemaining(seconds) {
        if (seconds === null || seconds === undefined) return '∞';
        if (seconds <= 0) return 'Expired';
        const days = Math.floor(seconds / 86400);
        const hours = Math.floor((seconds % 86400) / 3600);
        const mins = Math.floor((seconds % 3600) / 60);
        if (days > 0) return `${days}d ${hours}h`;
        if (hours > 0) return `${hours}h ${mins}m`;
        return `${mins}m`;
    }

    function progressBar(pct, colour) {
        const clamped = Math.min(100, Math.max(0, pct || 0));
        return `<div style="height:6px; border-radius:3px; background:var(--border); overflow:hidden; margin:4px 0 2px;">
            <div style="height:100%; width:${clamped}%; background:${colour}; transition:width .4s;"></div>
        </div>`;
    }

    function renderTownPanel(side, data) {
        const colour = side === 'aresden' ? 'var(--accent)' : 'var(--purple)';
        const headerHtml = `<div style="display:flex; align-items:center; gap:8px; margin-bottom:14px;">${pillSide(side)} <span style="font-size:11px; color:var(--muted); font-weight:500; text-transform:uppercase; letter-spacing:.06em;">Town Projects</span></div>`;

        if (!data) {
            return `${headerHtml}<p style="font-size:12px; color:var(--dim);">Failed to load.</p>`;
        }

        const projects = data.projects || [];
        const rewards  = data.activeRewards || [];

        let projectsHtml = '';
        if (!projects.length) {
            projectsHtml = `<p style="font-size:12px; color:var(--dim); margin-bottom:8px;">No active projects.</p>`;
        } else {
            projectsHtml = projects.map(p => {
                const statusLabel = PROJECT_STATUS_LABEL[p.status] ?? `Status(${p.status})`;
                const pillClass   = PROJECT_STATUS_PILL[p.status] ?? 'pill-neutral';
                const remaining   = fmtRemaining(p.remainingSeconds);
                return `<div style="margin-bottom:10px; padding-bottom:10px; border-bottom:1px solid var(--border);">
                    <div style="display:flex; align-items:center; gap:6px; margin-bottom:3px; flex-wrap:wrap;">
                        <span style="font-size:12px; font-weight:600;">${esc(p.name)}</span>
                        <span class="pill ${pillClass}" style="font-size:10px;">${esc(statusLabel)}</span>
                    </div>
                    ${progressBar(p.progressPct, colour)}
                    <div style="display:flex; justify-content:space-between; font-size:11px; color:var(--muted);">
                        <span>${p.currentProgress} / ${p.targetProgress} (${p.progressPct.toFixed(1)}%)</span>
                        <span>${remaining}</span>
                    </div>
                </div>`;
            }).join('');
        }

        let rewardsHtml = '';
        if (rewards.length) {
            rewardsHtml = `<div style="margin-top:8px;">
                <div style="font-size:11px; font-weight:600; color:var(--muted); text-transform:uppercase; letter-spacing:.06em; margin-bottom:4px;">Active Rewards</div>
                ${rewards.map(r => `
                    <div style="font-size:11px; color:var(--text); display:flex; justify-content:space-between; gap:8px; padding:2px 0;">
                        <span>${esc(r.rewardType)}: ${esc(r.rewardValue)}</span>
                        <span style="color:var(--muted);">${fmtRemaining(r.remainingSeconds)}</span>
                    </div>`).join('')}
            </div>`;
        }

        return `${headerHtml}${projectsHtml}${rewardsHtml}`;
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

    function weatherCell(weather) {
        const kind = (weather?.weatherKind || 'clear').toLowerCase();
        const pct = Math.max(0, Math.min(100, (weather?.intensity01 || 0) * 100));

        const icon =
            kind === 'storm' ? '⛈' :
            kind === 'snow' ? '❄' :
            kind === 'heavyrain' ? '🌧' :
            kind === 'lightrain' ? '🌦' :
            kind === 'overcast' ? '☁' :
            '☀';


        const label =
            kind === 'storm' ? 'Storm' :
            kind === 'snow' ? 'Snow' :
            kind === 'heavyrain' ? 'Heavy Rain' :
            kind === 'lightrain' ? 'Light Rain' :
            kind === 'overcast' ? 'Overcast' :
            'Clear';

        return `${icon} ${esc(label)} (${pct.toFixed(0)}%)`;
    }

    async function refresh() {
        const [servers, players, realm, crusade, aresdenState, elvineState] = await Promise.all([
            fetchServers(), fetchPlayers(), fetchRealmState(),
            fetchLatestCrusadeSnapshot('default'),
            fetchTownProjectState('aresden'),
            fetchTownProjectState('elvine')
        ]);

        const aresden = players.filter(p => (p.side || '').toLowerCase() === 'aresden').length;
        const elvine  = players.filter(p => (p.side || '').toLowerCase() === 'elvine').length;
        const neutral = players.length - aresden - elvine;
        const onlineServers = servers.filter(s => (s.state || '').toLowerCase() === 'online').length;
        const weatherReportingServers = servers.filter(s => !!(s.weather && s.weather.weatherKind)).length;

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
    <div class="stat-card"><div class="label">Weather Reporting</div><div class="value">${weatherReportingServers} / ${servers.length}</div><div class="sub">Servers publishing weather state</div></div>
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

        const playersByMap = players.reduce((acc, p) => {
            const serverId = (p.gameServerId || '').toLowerCase();
            const mapId = (p.mapId || '').toLowerCase();
            const mapName = (p.mapName || '').toLowerCase();
            if (!serverId) return acc;

            if (mapId) {
                const key = `${serverId}|id:${mapId}`;
                acc[key] = (acc[key] || 0) + 1;
                return acc;
            }

            if (mapName) {
                const key = `${serverId}|name:${mapName}`;
                acc[key] = (acc[key] || 0) + 1;
            }

            return acc;
        }, {});

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
                    isOutside: !!m.isOutside,
                    playerCount:
                        playersByMap[`${(s.id || '').toLowerCase()}|id:${(m.id || '').toLowerCase()}`] ||
                        playersByMap[`${(s.id || '').toLowerCase()}|name:${(m.mapName || '').toLowerCase()}`] ||
                        0,
                    weather: s.weather || null
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
                ? `<tr><td colspan="6" class="empty-row">No online maps reported.</td></tr>`
                : onlineMaps.map(r => `
                    <tr>
                        <td>${esc(r.mapName || r.mapId || '-')}</td>
                        <td class="mono">${Number.isFinite(r.cellX) && Number.isFinite(r.cellY) ? `${r.cellX}, ${r.cellY}` : '-'}</td>
                        <td>
                            <span class="pill ${r.isSafeMap ? 'pill-green' : 'pill-neutral'}">${r.isSafeMap ? 'Safe' : 'Combat'}</span>
                            <span class="pill ${r.isOutside ? 'pill-green' : 'pill-neutral'}">${r.isOutside ? 'Outside' : 'Inside'}</span>
                        </td>
                        <td>${weatherCell(r.weather)}</td>
                        <td class="mono">${r.playerCount}</td>
                        <td><a class="admin-link" onclick="viewGameServer('${esc(r.serverId)}')">${esc(r.serverId)}</a></td>
                    </tr>`).join('');
        }

        const cc = $('dashCrusade');
        if (cc) cc.innerHTML = renderCrusade(crusade);

        const ap = $('dashAresden');
        if (ap) ap.innerHTML = renderTownPanel('aresden', aresdenState);

        const ep = $('dashElvine');
        if (ep) ep.innerHTML = renderTownPanel('elvine', elvineState);
    }

    Admin.registerPage('dashboard', { onEnter: refresh, onTick: refresh });
})();