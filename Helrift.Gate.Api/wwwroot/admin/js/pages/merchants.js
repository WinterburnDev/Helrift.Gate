(function () {
    const { $, esc, fmtUnix, api } = Admin;

    const ItemType_Names = { 0: 'Weapon', 1: 'Armour', 2: 'Offhand', 3: 'Consumable', 4: 'Gatherable', 5: 'Necklace', 6: 'Ring', 7: 'Fish', 8: 'Coating' };

    let _currentNpcId = null;

    // ── NPC list ──────────────────────────────────────

    async function loadNpcList() {
        const listEl = $('merchantNpcList');
        listEl.innerHTML = '<p style="color:var(--dim); font-size:12px;">Loading…</p>';

        try {
            const res = await api('/admin/api/merchants');
            if (!res.ok) { listEl.innerHTML = `<p style="color:var(--red);">Error ${res.status}</p>`; return; }
            const ids = await res.json();

            if (!ids.length) { listEl.innerHTML = '<p style="color:var(--dim); font-size:12px;">No merchants found.</p>'; return; }

            listEl.innerHTML = `<div style="display:flex; flex-wrap:wrap; gap:8px;">` +
                ids.map(id => `<button class="btn btn-primary" onclick="openMerchant('${esc(id)}')">${esc(id)}</button>`).join('') +
                `</div>`;
        } catch (e) { listEl.innerHTML = `<p style="color:var(--red);">Network error</p>`; console.error(e); }
    }

    // ── Drilldown ─────────────────────────────────────

    window.openMerchant = function (npcId) {
        _currentNpcId = npcId;
        $('merchantPage').value = 1;
        $('merchantDrilldownTitle').textContent = npcId;
        $('merchantNpcList').style.display = 'none';
        $('merchantListingsSection').style.display = '';
        loadListings();
    };

    window.closeMerchantDrilldown = function () {
        _currentNpcId = null;
        $('merchantListingsSection').style.display = 'none';
        $('merchantNpcList').style.display = '';
    };

    async function loadListings() {
        if (!_currentNpcId) return;
        const sort = parseInt($('merchantSort').value, 10);
        const page = parseInt($('merchantPage').value, 10) || 1;
        const st = $('merchantStatus');
        const tb = $('merchantBody');
        st.textContent = '';
        tb.innerHTML = '<tr><td colspan="9" class="empty-row">Loading…</td></tr>';

        try {
            const res = await api(`/api/v1/merchants/${encodeURIComponent(_currentNpcId)}/query`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ page, pageSize: 30, sort, includeExpired: false })
            });
            if (!res.ok) { tb.innerHTML = `<tr><td colspan="9" class="empty-row">Error ${res.status}</td></tr>`; return; }
            const data = await res.json();
            $('merchantTotal').textContent = data.total || 0;
            const items = data.items || [];
            if (!items.length) { tb.innerHTML = '<tr><td colspan="9" class="empty-row">No listings found.</td></tr>'; return; }

            tb.innerHTML = items.map(i => `
                <tr>
                    <td class="mono" style="font-size:11px;">${esc(i.listingId || '')}</td>
                    <td>${esc(i.itemName || i.itemId || '')}</td>
                    <td>${esc(i.itemType != null ? ItemType_Names[i.itemType] || String(i.itemType) : '-')}</td>
                    <td>${esc(i.quality != null ? String(i.quality) : '-')}</td>
                    <td>${i.quantity ?? '-'}</td>
                    <td>${i.finalBuyPrice != null ? i.finalBuyPrice.toLocaleString() : '-'}</td>
                    <td class="mono" style="font-size:11px;">${esc(i.ownerCharacterId || '')}</td>
                    <td>${fmtUnix(i.listedAtUnix)}</td>
                    <td>${fmtUnix(i.expireAtUnix)}</td>
                </tr>`).join('');
        } catch (e) { tb.innerHTML = '<tr><td colspan="9" class="empty-row">Network error</td></tr>'; console.error(e); }
    }

    window.merchantRefresh = loadListings;
    window.merchantPrev = function () { const p = parseInt($('merchantPage').value, 10) || 1; if (p > 1) { $('merchantPage').value = p - 1; loadListings(); } };
    window.merchantNext = function () { $('merchantPage').value = (parseInt($('merchantPage').value, 10) || 1) + 1; loadListings(); };

    function onEnter(args) {
        $('merchantListingsSection').style.display = 'none';
        $('merchantNpcList').style.display = '';
        _currentNpcId = null;
        loadNpcList();

        if (args && args.npcId) {
            openMerchant(args.npcId);
        }
    }

    Admin.registerPage('merchants', { onEnter: onEnter });
})();