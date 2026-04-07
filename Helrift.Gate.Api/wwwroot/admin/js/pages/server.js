(function () {
    const { $, esc, pillState, fmtUnix, fmtDate, api } = Admin;

    function weatherLabel(weather) {
        if (!weather || !weather.weatherKind) {
            return '<span class="pill pill-neutral">Unknown</span>';
        }

        return `<span class="pill pill-blue">${esc(weather.weatherKind)}</span>`;
    }

    function kv(label, value) {
        return `<tr><td>${esc(label)}</td><td>${value === null || value === undefined || value === '' ? '-' : esc(String(value))}</td></tr>`;
    }

    function kvHtml(label, html) {
        return `<tr><td>${esc(label)}</td><td>${html}</td></tr>`;
    }

    function section(title, content, collapsed) {
        const id = 'serversec_' + title.replace(/\W/g, '_');
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

    function boolPill(v, yes, no) {
        return v
            ? `<span class="pill pill-green">${esc(yes || 'Yes')}</span>`
            : `<span class="pill pill-neutral">${esc(no || 'No')}</span>`;
    }

    function renderMapTable(maps) {
        if (!maps || !maps.length)
            return '<p style="color:var(--dim); font-size:12px;">No maps registered.</p>';

        const sorted = [...maps].sort((a, b) => (a.mapName || '').localeCompare(b.mapName || ''));

        return `<div class="scroll-x"><table>
            <thead><tr><th>#</th><th>Map Name</th><th>Map ID</th><th>Scene</th><th>Cell</th><th>Safe</th><th>Outside</th></tr></thead>
            <tbody>${sorted.map((m, i) => `
                <tr>
                    <td>${i + 1}</td>
                    <td>${esc(m.mapName || '-')}</td>
                    <td class="mono">${esc(m.id || '-')}</td>
                    <td>${esc(m.sceneName || '-')}</td>
                    <td class="mono">${Number.isFinite(m.cellX) && Number.isFinite(m.cellY) ? `${m.cellX}, ${m.cellY}` : '-'}</td>
                    <td>${boolPill(!!m.isSafeMap, 'Yes', 'No')}</td>
                    <td>${boolPill(!!m.isOutside, 'Yes', 'No')}</td>
                </tr>`).join('')}
            </tbody></table></div>`;
    }

    function renderServerDetail(s) {
        let html = `
            <div class="action-bar">
                <button class="btn" onclick="Admin.switchPage('servers')">← Back</button>
                <div class="action-bar-spacer"></div>
                <button class="btn" onclick="loadGameServerDetail({ id: '${esc(s.id || '')}' })">Refresh</button>
            </div>`;

        html += section('Connection', `<table class="kv-table"><tbody>
            ${kv('Server ID', s.id)}
            ${kvHtml('Status', pillState(s.state))}
            ${kv('Connected', s.isConnected ? 'Yes' : 'No')}
            ${kv('Build Version', s.buildVersion || '-')}
            ${kv('Registered At (UTC)', s.registeredAtUnixUtc ? fmtUnix(s.registeredAtUnixUtc) : '-')}
            ${kv('Last Heartbeat (UTC)', s.lastHeartbeatUtc ? fmtDate(s.lastHeartbeatUtc) : '-')}
            ${kv('Map Count', s.mapCount ?? 0)}
        </tbody></table>`);

        const weather = s.weather || null;
        const weatherIntensityPct = weather ? Math.max(0, Math.min(100, (weather.intensity01 || 0) * 100)) : 0;
        const windIntensityPct = weather ? Math.max(0, Math.min(100, (weather.windIntensity01 || 0) * 100)) : 0;
        const fogDensityPct = weather ? Math.max(0, Math.min(100, (weather.fogDensity01 || 0) * 100)) : 0;

        html += section('Weather', `<table class="kv-table"><tbody>
            ${kvHtml('Weather Kind', weatherLabel(weather))}
            ${kv('Observed At (UTC)', weather?.observedAtUnixUtc ? fmtUnix(weather.observedAtUnixUtc) : '-')}
            ${kv('Intensity', weather ? `${weatherIntensityPct.toFixed(0)}%` : '-')}
            ${kv('Has Precipitation', weather ? (weather.hasPrecipitation ? 'Yes' : 'No') : '-')}
            ${kv('Wind Intensity', weather ? `${windIntensityPct.toFixed(0)}%` : '-')}
            ${kv('Fog Density', weather ? `${fogDensityPct.toFixed(0)}%` : '-')}
            ${kv('Source Map ID', weather?.sourceMapId || '-')}
            ${kv('Source Map Name', weather?.sourceMapName || '-')}
        </tbody></table>`);

        html += section(`Maps (${s.mapCount ?? 0})`, renderMapTable(s.maps), false);
        html += section('Raw JSON', `<div class="json-box"><pre class="json-block">${esc(JSON.stringify(s, null, 2))}</pre></div>`, true);

        return html;
    }

    window.loadGameServerDetail = async function (args) {
        const container = $('serverDetailContent');
        const id = args && args.id ? args.id : null;

        if (!id) {
            container.innerHTML = '<div class="empty-row">No game server specified.</div>';
            return;
        }

        container.innerHTML = '<div class="empty-row">Loading…</div>';

        try {
            const res = await api(`/admin/api/gameservers/${encodeURIComponent(id)}`);
            if (res.status === 404) {
                container.innerHTML = '<div class="empty-row">Game server not found.</div>';
                return;
            }
            if (!res.ok) {
                container.innerHTML = `<div class="empty-row">Error ${res.status}</div>`;
                return;
            }

            const s = await res.json();
            container.innerHTML = renderServerDetail(s);
            $('pageTitle').textContent = 'Game Server: ' + (s.id || id);
        } catch (e) {
            container.innerHTML = '<div class="empty-row">Network error.</div>';
            console.error(e);
        }
    };

    Admin.registerPage('server', {
        onEnter: window.loadGameServerDetail,
        onTick: function () {
            const active = location.hash || '';
            if (!active.startsWith('#server')) return;
            const q = active.split('?')[1] || '';
            const p = new URLSearchParams(q);
            const id = p.get('id');
            if (id) window.loadGameServerDetail({ id: id });
        }
    });
})();