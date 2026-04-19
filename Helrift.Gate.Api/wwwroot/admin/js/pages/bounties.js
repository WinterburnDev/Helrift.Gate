(function () {
    const { $, esc, fmtDate, api } = Admin;

    const STATUS = {
        1: 'Active',
        2: 'Fulfilled',
        3: 'Cancelled',
        4: 'Expired'
    };

    function mapStatus(v) {
        if (typeof v === 'number') return STATUS[v] || String(v);
        return v || '-';
    }

    function statePill(status) {
        const text = mapStatus(status);
        const s = text.toLowerCase();

        if (s.includes('active')) return `<span class="pill pill-yellow">${esc(text)}</span>`;
        if (s.includes('fulfilled')) return `<span class="pill pill-green">${esc(text)}</span>`;
        if (s.includes('cancel') || s.includes('expired')) return `<span class="pill pill-orange">${esc(text)}</span>`;
        return `<span class="pill pill-neutral">${esc(text)}</span>`;
    }

    function val(o, ...keys) {
        for (const k of keys) if (o && o[k] !== undefined && o[k] !== null) return o[k];
        return null;
    }

    async function loadBounties() {
        const realmId = ($('bountyRealmId').value || 'default').trim() || 'default';
        const status = $('bountyFilterStatus').value.trim();
        const target = $('bountyFilterTarget').value.trim();
        const issuer = $('bountyFilterIssuer').value.trim();
        const fulfilledBy = $('bountyFilterFulfilledBy').value.trim();
        const page = Math.max(1, parseInt($('bountyPage').value || '1', 10) || 1);
        const pageSize = Math.min(250, Math.max(1, parseInt($('bountyPageSize').value || '100', 10) || 100));

        const body = $('bountyBody');
        const st = $('bountyStatus');
        st.textContent = '';
        body.innerHTML = '<tr><td colspan="12" class="empty-row">Loading...</td></tr>';

        const q = new URLSearchParams();
        q.set('realmId', realmId);
        q.set('page', String(page));
        q.set('pageSize', String(pageSize));
        if (status) q.set('status', status);
        if (target) q.set('targetCharacterId', target);
        if (issuer) q.set('issuerCharacterId', issuer);
        if (fulfilledBy) q.set('fulfilledByCharacterId', fulfilledBy);

        try {
            const res = await api(`/admin/api/bounties/search?${q.toString()}`);
            if (!res.ok) {
                body.innerHTML = `<tr><td colspan="12" class="empty-row">Error ${res.status}</td></tr>`;
                return;
            }

            const data = await res.json();
            const items = data.items || [];
            $('bountyTotal').textContent = `${data.total ?? items.length}`;

            if (!items.length) {
                body.innerHTML = '<tr><td colspan="12" class="empty-row">No bounties found.</td></tr>';
                return;
            }

            body.innerHTML = items.map(row => {
                const snap = val(row, 'bounty', 'Bounty') || row;
                const bounty = val(snap, 'bounty', 'Bounty') || snap;

                const id = val(bounty, 'bountyId', 'BountyId') || '-';
                const statusRaw = val(bounty, 'status', 'Status');
                const issuerId = val(bounty, 'issuerCharacterId', 'IssuerCharacterId') || '-';
                const targetId = val(bounty, 'targetCharacterId', 'TargetCharacterId') || '-';
                const reward = val(bounty, 'rewardGold', 'RewardGold') ?? '-';
                const listingFee = val(bounty, 'listingFeePaid', 'ListingFeePaid') ?? '-';
                const createdAt = val(bounty, 'createdAtUtc', 'CreatedAtUtc');
                const expiresAt = val(bounty, 'expiresAtUtc', 'ExpiresAtUtc');
                const fulfilledAt = val(bounty, 'fulfilledAtUtc', 'FulfilledAtUtc');
                const cancelledAt = val(bounty, 'cancelledAtUtc', 'CancelledAtUtc');
                const fulfilledById = val(bounty, 'fulfilledByCharacterId', 'FulfilledByCharacterId') || '-';

                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(String(id))}</td>
                    <td>${statePill(statusRaw)}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(issuerId))}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(targetId))}</td>
                    <td>${esc(String(reward))}</td>
                    <td>${esc(String(listingFee))}</td>
                    <td>${createdAt ? fmtDate(createdAt) : '-'}</td>
                    <td>${expiresAt ? fmtDate(expiresAt) : '-'}</td>
                    <td>${fulfilledAt ? fmtDate(fulfilledAt) : '-'}</td>
                    <td>${cancelledAt ? fmtDate(cancelledAt) : '-'}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(fulfilledById))}</td>
                    <td><button class="btn" onclick="openDrawer_bountyDetail('${esc(String(id))}','${esc(String(realmId))}')">View</button></td>
                </tr>`;
            }).join('');
        } catch (e) {
            console.error(e);
            body.innerHTML = '<tr><td colspan="12" class="empty-row">Network error.</td></tr>';
        }
    }

    function renderDetail(detail) {
        const snap = val(detail, 'bounty', 'Bounty') || {};
        const bounty = val(snap, 'bounty', 'Bounty') || snap;
        const escrowSummary = val(detail, 'escrowSummary', 'EscrowSummary') || null;

        const htmlBounty = `
            <div class="card" style="margin-bottom:12px;">
                <div class="card-title">Bounty Contract</div>
                <table class="kv-table"><tbody>
                    <tr><td>BountyId</td><td class="mono">${esc(String(val(bounty, 'bountyId', 'BountyId') || '-'))}</td></tr>
                    <tr><td>Status</td><td>${statePill(val(bounty, 'status', 'Status'))}</td></tr>
                    <tr><td>IssuerCharacterId</td><td class="mono">${esc(String(val(bounty, 'issuerCharacterId', 'IssuerCharacterId') || '-'))}</td></tr>
                    <tr><td>TargetCharacterId</td><td class="mono">${esc(String(val(bounty, 'targetCharacterId', 'TargetCharacterId') || '-'))}</td></tr>
                    <tr><td>RewardGold</td><td>${esc(String(val(bounty, 'rewardGold', 'RewardGold') ?? '-'))}</td></tr>
                    <tr><td>ListingFeePaid</td><td>${esc(String(val(bounty, 'listingFeePaid', 'ListingFeePaid') ?? '-'))}</td></tr>
                    <tr><td>CreatedAt</td><td>${val(bounty, 'createdAtUtc', 'CreatedAtUtc') ? fmtDate(val(bounty, 'createdAtUtc', 'CreatedAtUtc')) : '-'}</td></tr>
                    <tr><td>ExpiresAt</td><td>${val(bounty, 'expiresAtUtc', 'ExpiresAtUtc') ? fmtDate(val(bounty, 'expiresAtUtc', 'ExpiresAtUtc')) : '-'}</td></tr>
                    <tr><td>FulfilledAt</td><td>${val(bounty, 'fulfilledAtUtc', 'FulfilledAtUtc') ? fmtDate(val(bounty, 'fulfilledAtUtc', 'FulfilledAtUtc')) : '-'}</td></tr>
                    <tr><td>CancelledAt</td><td>${val(bounty, 'cancelledAtUtc', 'CancelledAtUtc') ? fmtDate(val(bounty, 'cancelledAtUtc', 'CancelledAtUtc')) : '-'}</td></tr>
                    <tr><td>FulfilledByCharacterId</td><td class="mono">${esc(String(val(bounty, 'fulfilledByCharacterId', 'FulfilledByCharacterId') || '-'))}</td></tr>
                </tbody></table>
            </div>`;

        const htmlEscrow = escrowSummary
            ? `<div class="card" style="margin-bottom:12px;">
                <div class="card-title">Escrow Summary</div>
                <table class="kv-table"><tbody>
                    <tr><td>Total</td><td>${esc(String(val(escrowSummary, 'totalAssets', 'TotalAssets') ?? '-'))}</td></tr>
                    <tr><td>Claimable</td><td>${esc(String(val(escrowSummary, 'claimableAssets', 'ClaimableAssets') ?? '-'))}</td></tr>
                    <tr><td>Claimed</td><td>${esc(String(val(escrowSummary, 'claimedAssets', 'ClaimedAssets') ?? '-'))}</td></tr>
                    <tr><td>Returned</td><td>${esc(String(val(escrowSummary, 'returnedAssets', 'ReturnedAssets') ?? '-'))}</td></tr>
                    <tr><td>Expired</td><td>${esc(String(val(escrowSummary, 'expiredAssets', 'ExpiredAssets') ?? '-'))}</td></tr>
                </tbody></table>
            </div>`
            : '';

        return htmlBounty + htmlEscrow;
    }

    window.openDrawer_bountyDetail = async function (bountyId, realmId) {
        try {
            const res = await api(`/admin/api/bounties/detail/${encodeURIComponent(bountyId)}?realmId=${encodeURIComponent(realmId || 'default')}`);
            if (!res.ok) {
                Admin.openDrawer('Bounty Detail', `<p class="empty-row">Error ${res.status}</p>`, 'Close', () => Admin.closeDrawer(), 'btn');
                return;
            }

            const detail = await res.json();
            Admin.openDrawer('Bounty Detail', renderDetail(detail), 'Close', () => Admin.closeDrawer(), 'btn');
        } catch (e) {
            console.error(e);
            Admin.openDrawer('Bounty Detail', '<p class="empty-row">Network error.</p>', 'Close', () => Admin.closeDrawer(), 'btn');
        }
    };

    window.refreshBountyAdmin = () => loadBounties();

    window.clearBountyFilters = function () {
        $('bountyFilterStatus').value = '';
        $('bountyFilterTarget').value = '';
        $('bountyFilterIssuer').value = '';
        $('bountyFilterFulfilledBy').value = '';
        $('bountyPage').value = '1';
        $('bountyPageSize').value = '100';
        loadBounties();
    };

    async function onEnter() {
        await loadBounties();
    }

    Admin.registerPage('bounties', { onEnter });
})();
