(function () {
    const { $, esc, api } = Admin;

    // ── Active ban list ───────────────────────────────────────────────────────

    async function loadActiveBans() {
        const realmId = $('banRealmId').value.trim() || 'default';
        const tb = $('activeBansBody');
        tb.innerHTML = '<tr><td colspan="7" class="empty-row">Loading…</td></tr>';

        try {
            const res = await api(`/admin/api/bans?realmId=${encodeURIComponent(realmId)}`);
            if (!res.ok) { tb.innerHTML = `<tr><td colspan="7" class="empty-row">Error ${res.status}</td></tr>`; return; }
            const bans = await res.json();

            if (!bans.length) {
                tb.innerHTML = '<tr><td colspan="7" class="empty-row">No active bans.</td></tr>';
                return;
            }

            tb.innerHTML = bans.map(b => {
                const bannedAt = b.bannedAtUnixUtc
                    ? new Date(b.bannedAtUnixUtc * 1000).toISOString().replace('T', ' ').replace('Z', ' UTC')
                    : '-';
                const expires = b.expiresAtUnixUtc
                    ? new Date(b.expiresAtUnixUtc * 1000).toISOString().replace('T', ' ').replace('Z', ' UTC')
                    : '<span class="pill pill-red">Permanent</span>';
                const revokeArgs = [
                    `'${encodeURIComponent(b.realmId || realmId)}'`,
                    `'${encodeURIComponent(b.steamId || '')}'`,
                    `'${encodeURIComponent(b.ipAddress || '')}'`
                ].join(', ');
                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(b.steamId || '-')}</td>
                    <td class="mono" style="font-size:11px;">${esc(b.ipAddress || '-')}</td>
                    <td>${esc(b.reason || '-')}</td>
                    <td style="font-size:11px;">${esc(b.bannedBy || '-')}</td>
                    <td style="font-size:11px;">${bannedAt}</td>
                    <td>${expires}</td>
                    <td><button class="btn btn-danger" style="padding:2px 8px; font-size:11px;" onclick="revokeBan(${revokeArgs})">Revoke</button></td>
                </tr>`;
            }).join('');
        } catch (e) {
            tb.innerHTML = '<tr><td colspan="7" class="empty-row">Network error.</td></tr>';
            console.error(e);
        }
    }

    window.revokeBan = async function (realmId, steamId, ipAddress) {
        const target = steamId || ipAddress;
        if (!confirm(`Revoke ban for ${target}? This will immediately unban them.`)) return;

        const params = new URLSearchParams({ realmId });
        if (steamId) params.set('steamId', steamId);
        if (ipAddress) params.set('ipAddress', ipAddress);

        try {
            const res = await api(`/admin/api/bans?${params.toString()}`, { method: 'DELETE' });
            if (res.ok || res.status === 204) {
                await loadActiveBans();
            } else {
                alert(`Failed to revoke ban: ${res.status}`);
            }
        } catch (e) {
            alert('Network error revoking ban.');
            console.error(e);
        }
    };

    // ── Drawer: Create Ban ────────────────────────────────────────────────────

    window.openDrawer_createBan = function () {
        Admin.openDrawer(
            'Create Ban',
            `<div class="form-grid" style="grid-template-columns:1fr 1fr;">
                <div class="field"><label>Steam ID</label><input id="dBanSteamId" type="text" placeholder="76561198…" /></div>
                <div class="field"><label>IP Address</label><input id="dBanIp" type="text" placeholder="192.168.x.x" /></div>
                <div class="field"><label>Duration (minutes)</label><input id="dBanDuration" type="number" min="1" /><small>Empty = permanent ban</small></div>
                <div class="field"><label>Created By</label><input id="dBanCreatedBy" type="text" value="admin" /></div>
                <div class="field" style="grid-column:1/-1"><label>Reason</label><input id="dBanReason" type="text" placeholder="e.g. Cheating, exploiting, toxic behaviour…" /></div>
            </div>`,
            'Create Ban',
            submitBan,
            'btn-danger'
        );
    };

    async function submitBan() {
        const realmId = $('banRealmId').value.trim() || 'default';
        const steamId = $('dBanSteamId').value.trim();
        const ip = $('dBanIp').value.trim();
        const durationRaw = $('dBanDuration').value.trim();
        const reason = $('dBanReason').value.trim();
        const createdBy = $('dBanCreatedBy').value.trim();
        const st = $('drawerStatus');
        st.textContent = '';

        if (!steamId && !ip) { st.textContent = 'Steam ID or IP address is required.'; return; }
        if (!reason) { st.textContent = 'Reason is required.'; return; }

        try {
            const res = await api('/admin/api/bans', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    realmId,
                    steamId: steamId || null,
                    ipAddress: ip || null,
                    reason,
                    durationMinutes: durationRaw ? parseInt(durationRaw, 10) : null,
                    createdBy: createdBy || 'admin'
                })
            });
            if (res.ok) {
                Admin.closeDrawer();
                await loadActiveBans();
            } else {
                const txt = await res.text();
                st.textContent = `Error ${res.status}: ${txt}`;
            }
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    Admin.registerPage('bans', { onEnter: loadActiveBans });
})();