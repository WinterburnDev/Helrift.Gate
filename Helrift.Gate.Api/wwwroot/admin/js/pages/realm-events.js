(function () {
    const { $, esc, fmtDate, api } = Admin;

    window.loadRealmEvents = async function () {
        const realmId = $('realmEventsRealmId').value.trim() || 'default';
        const limit = parseInt($('realmEventsLimit').value, 10) || 25;
        const tb = $('realmEventsBody');
        tb.innerHTML = '<tr><td colspan="5" class="empty-row">Loading…</td></tr>';

        try {
            const res = await api(`/api/v1/realm-events/recent/${encodeURIComponent(realmId)}?limit=${limit}`);
            if (!res.ok) { tb.innerHTML = `<tr><td colspan="5" class="empty-row">Error ${res.status}</td></tr>`; return; }
            const events = await res.json();
            if (!events.length) { tb.innerHTML = '<tr><td colspan="5" class="empty-row">No events found.</td></tr>'; return; }
            tb.innerHTML = events.map(e => `
                <tr>
                    <td>${fmtDate(e.utc)}</td>
                    <td class="mono">${esc(e.type || '')}</td>
                    <td>${esc(e.eventType || '')}</td>
                    <td class="mono" style="font-size:11px;">${esc(e.eventInstanceId || '')}</td>
                    <td>${e.sequence ?? '-'}</td>
                </tr>`).join('');
        } catch (e) { tb.innerHTML = '<tr><td colspan="5" class="empty-row">Network error</td></tr>'; console.error(e); }
    };

    Admin.registerPage('realm-events', {});
})();