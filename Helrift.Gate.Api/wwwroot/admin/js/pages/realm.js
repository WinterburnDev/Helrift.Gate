(function () {
    const { $, esc, fmtUnix, api, fetchRealmState } = Admin;

    async function load() {
        const realm = await fetchRealmState();
        if (!realm) { $('realmStateBody').innerHTML = '<tr><td colspan="2">Failed to load</td></tr>'; return; }

        $('realmStateBody').innerHTML = `
            <tr><td>Deny New Logins</td><td>${realm.denyNewLogins ? '<span class="pill pill-red">Yes</span>' : '<span class="pill pill-green">No</span>'}</td></tr>
            <tr><td>Deny New Joins</td><td>${realm.denyNewJoins ? '<span class="pill pill-red">Yes</span>' : '<span class="pill pill-green">No</span>'}</td></tr>
            <tr><td>Shutdown At</td><td>${realm.shutdownAtUnixUtc ? fmtUnix(realm.shutdownAtUnixUtc) : '<span class="pill pill-green">None</span>'}</td></tr>
            <tr><td>Realm Message</td><td>${realm.realmMessage ? esc(realm.realmMessage) : '—'}</td></tr>
        `;
    }

    // ── Drawer: Schedule Shutdown ─────────────────────────────────────────────

    window.openDrawer_scheduleShutdown = function () {
        Admin.openDrawer(
            'Schedule Shutdown',
            `<p style="font-size:13px; color:var(--muted); margin:0 0 16px;">Broadcasts a countdown to all game servers and schedules a graceful shutdown.</p>
            <div class="form-grid" style="grid-template-columns:1fr 1fr;">
                <div class="field"><label>Minutes</label><input id="dShutdownMinutes" type="number" min="1" value="10" /></div>
                <div class="field"><label>Initiated By</label><input id="dShutdownBy" type="text" value="admin" /></div>
                <div class="field" style="grid-column:1/-1"><label>Message</label><input id="dShutdownMsg" type="text" value="Server restart scheduled" /></div>
            </div>`,
            'Schedule Shutdown',
            doScheduleShutdown,
            'btn-warning'
        );
    };

    async function doScheduleShutdown() {
        const minutes = parseInt($('dShutdownMinutes').value, 10);
        const message = $('dShutdownMsg').value.trim();
        const by = $('dShutdownBy').value.trim() || 'admin';
        const st = $('drawerStatus');
        st.textContent = '';

        if (!minutes || minutes <= 0) { st.textContent = 'Minutes must be > 0.'; return; }
        if (!message) { st.textContent = 'Message is required.'; return; }

        try {
            const res = await api('/api/v1/realm/shutdown/schedule', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ minutes, message, initiatedBy: by })
            });
            if (res.ok) { Admin.closeDrawer(); load(); }
            else { st.textContent = `Error ${res.status}: ${await res.text()}`; }
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    // ── Drawer: Clear All Operations ──────────────────────────────────────────

    window.openDrawer_clearRealm = function () {
        Admin.openDrawer(
            'Clear All Operations',
            `<p style="font-size:13px; color:var(--muted); margin:0 0 16px;">Cancels all active realm operations (shutdown, maintenance, broadcasts). State is pushed immediately to all game servers.</p>
            <div class="form-grid">
                <div class="field"><label>Initiated By</label><input id="dClearBy" type="text" value="admin" /></div>
            </div>`,
            'Clear All',
            doClearRealm,
            'btn-danger'
        );
    };

    async function doClearRealm() {
        const by = $('dClearBy').value.trim() || 'admin';
        const st = $('drawerStatus');
        st.textContent = '';

        try {
            const res = await api('/api/v1/realm/clear', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ initiatedBy: by })
            });
            if (res.ok) { Admin.closeDrawer(); load(); }
            else { st.textContent = `Error ${res.status}: ${await res.text()}`; }
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    Admin.registerPage('realm', { onEnter: load });
})();