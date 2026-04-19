(function () {
    const { $, esc, fmtDate, api } = Admin;

    const ORDER_TYPE = {
        1: 'Buy',
        2: 'Sell'
    };

    const ORDER_STATUS = {
        1: 'Active',
        2: 'PartiallyFilled',
        3: 'Fulfilled',
        4: 'Cancelled',
        5: 'Expired'
    };

    const OFFER_STATUS = {
        1: 'Pending',
        2: 'Accepted',
        3: 'Rejected',
        4: 'Cancelled',
        5: 'Expired'
    };

    function mapType(v) {
        if (typeof v === 'number') return ORDER_TYPE[v] || String(v);
        return v || '-';
    }

    function mapStatus(v) {
        if (typeof v === 'number') return ORDER_STATUS[v] || String(v);
        return v || '-';
    }

    function mapOfferStatus(v) {
        if (typeof v === 'number') return OFFER_STATUS[v] || String(v);
        return v || '-';
    }

    function statePill(status) {
        const text = mapStatus(status);
        const s = text.toLowerCase();

        if (s.includes('active') || s.includes('partial')) return `<span class="pill pill-yellow">${esc(text)}</span>`;
        if (s.includes('fulfilled')) return `<span class="pill pill-green">${esc(text)}</span>`;
        if (s.includes('cancel') || s.includes('expired')) return `<span class="pill pill-orange">${esc(text)}</span>`;
        return `<span class="pill pill-neutral">${esc(text)}</span>`;
    }

    function offerStatePill(status) {
        const text = mapOfferStatus(status);
        const s = text.toLowerCase();

        if (s.includes('pending')) return `<span class="pill pill-yellow">${esc(text)}</span>`;
        if (s.includes('accepted')) return `<span class="pill pill-green">${esc(text)}</span>`;
        if (s.includes('reject') || s.includes('cancel') || s.includes('expire')) return `<span class="pill pill-orange">${esc(text)}</span>`;
        return `<span class="pill pill-neutral">${esc(text)}</span>`;
    }

    function val(o, ...keys) {
        for (const k of keys) if (o && o[k] !== undefined && o[k] !== null) return o[k];
        return null;
    }

    async function loadMarketplace(args) {
        const realmId = ($('mktRealmId').value || 'default').trim() || 'default';
        const type = $('mktFilterType').value.trim();
        const status = $('mktFilterStatus').value.trim();
        const characterId = $('mktFilterCharacter').value.trim();
        const itemDefinitionId = $('mktFilterItem').value.trim();
        const page = Math.max(1, parseInt($('mktPage').value || '1', 10) || 1);
        const pageSize = Math.min(250, Math.max(1, parseInt($('mktPageSize').value || '100', 10) || 100));

        if (args && args.characterId && !characterId) $('mktFilterCharacter').value = args.characterId;
        if (args && args.itemDefinitionId && !itemDefinitionId) $('mktFilterItem').value = args.itemDefinitionId;

        const body = $('marketplaceBody');
        const st = $('marketplaceStatus');
        st.textContent = '';
        body.innerHTML = '<tr><td colspan="14" class="empty-row">Loading…</td></tr>';

        const q = new URLSearchParams();
        q.set('realmId', realmId);
        q.set('page', String(page));
        q.set('pageSize', String(pageSize));
        if (type) q.set('orderType', type);
        if (status) q.set('status', status);
        if (characterId) q.set('characterId', characterId);
        if (itemDefinitionId) q.set('itemDefinitionId', itemDefinitionId);

        try {
            const res = await api(`/admin/api/marketplace/search?${q.toString()}`);
            if (!res.ok) {
                body.innerHTML = `<tr><td colspan="14" class="empty-row">Error ${res.status}</td></tr>`;
                return;
            }

            const data = await res.json();
            const items = data.items || [];
            $('marketplaceTotal').textContent = `${data.total ?? items.length}`;

            if (!items.length) {
                body.innerHTML = '<tr><td colspan="14" class="empty-row">No marketplace orders found.</td></tr>';
                return;
            }

            body.innerHTML = items.map(row => {
                const snapOrder = val(row, 'order', 'Order') || row;
                const snapTax = val(row, 'tax', 'Tax') || {};

                const orderId = val(snapOrder, 'orderId', 'OrderId') || '-';
                const orderType = val(snapOrder, 'orderType', 'OrderType');
                const orderStatus = val(snapOrder, 'status', 'Status');
                const owner = val(snapOrder, 'ownerCharacterId', 'OwnerCharacterId') || '-';
                const item = val(snapOrder, 'itemDefinitionId', 'ItemDefinitionId') || '-';
                const qtyTotal = val(snapOrder, 'quantityTotal', 'QuantityTotal') ?? '-';
                const qtyRemaining = val(snapOrder, 'quantityRemaining', 'QuantityRemaining') ?? '-';
                const unitPrice = val(snapOrder, 'unitPriceGold', 'UnitPriceGold') ?? '-';
                const listingFeePaid = val(snapOrder, 'listingFeePaid', 'ListingFeePaid') ?? '-';
                const taxAmount = val(snapTax, 'completionTaxGold', 'CompletionTaxGold') ?? val(snapOrder, 'completionTaxReserved', 'CompletionTaxReserved') ?? '-';

                const createdAt = val(snapOrder, 'createdAtUtc', 'CreatedAtUtc');
                const expiresAt = val(snapOrder, 'expiresAtUtc', 'ExpiresAtUtc');
                const fulfilledAt = val(snapOrder, 'fulfilledAtUtc', 'FulfilledAtUtc');
                const cancelledAt = val(snapOrder, 'cancelledAtUtc', 'CancelledAtUtc');

                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(String(orderId))}</td>
                    <td>${esc(mapType(orderType))}</td>
                    <td>${statePill(orderStatus)}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(owner))}</td>
                    <td>${esc(String(item))}</td>
                    <td>${esc(String(qtyRemaining))} / ${esc(String(qtyTotal))}</td>
                    <td>${esc(String(unitPrice))}</td>
                    <td>${esc(String(listingFeePaid))}</td>
                    <td>${esc(String(taxAmount))}</td>
                    <td>${createdAt ? fmtDate(createdAt) : '-'}</td>
                    <td>${expiresAt ? fmtDate(expiresAt) : '-'}</td>
                    <td>${fulfilledAt ? fmtDate(fulfilledAt) : '-'}</td>
                    <td>${cancelledAt ? fmtDate(cancelledAt) : '-'}</td>
                    <td><button class="btn" onclick="openDrawer_marketplaceDetail('${esc(String(orderId))}','${esc(String(realmId))}')">View</button></td>
                </tr>`;
            }).join('');
        } catch (e) {
            console.error(e);
            body.innerHTML = '<tr><td colspan="14" class="empty-row">Network error.</td></tr>';
        }
    }

    async function loadMarketplaceOffers() {
        const realmId = ($('mktRealmId').value || 'default').trim() || 'default';
        const status = $('mktOfferFilterStatus').value.trim();
        const marketplaceOrderId = $('mktOfferFilterOrder').value.trim();
        const characterId = $('mktOfferFilterCharacter').value.trim();
        const itemDefinitionId = $('mktOfferFilterItem').value.trim();
        const page = Math.max(1, parseInt($('mktOfferPage').value || '1', 10) || 1);
        const pageSize = Math.min(250, Math.max(1, parseInt($('mktOfferPageSize').value || '100', 10) || 100));

        const body = $('marketplaceOfferBody');
        const st = $('marketplaceOfferStatus');
        st.textContent = '';
        body.innerHTML = '<tr><td colspan="14" class="empty-row">Loading…</td></tr>';

        const q = new URLSearchParams();
        q.set('realmId', realmId);
        q.set('page', String(page));
        q.set('pageSize', String(pageSize));
        if (status) q.set('status', status);
        if (marketplaceOrderId) q.set('marketplaceOrderId', marketplaceOrderId);
        if (characterId) q.set('characterId', characterId);
        if (itemDefinitionId) q.set('itemDefinitionId', itemDefinitionId);

        try {
            const res = await api(`/admin/api/marketplace/offers/search?${q.toString()}`);
            if (!res.ok) {
                body.innerHTML = `<tr><td colspan="14" class="empty-row">Error ${res.status}</td></tr>`;
                return;
            }

            const data = await res.json();
            const items = data.items || [];
            $('marketplaceOfferTotal').textContent = `${data.total ?? items.length}`;

            if (!items.length) {
                body.innerHTML = '<tr><td colspan="14" class="empty-row">No marketplace offers found.</td></tr>';
                return;
            }

            body.innerHTML = items.map(row => {
                const snap = val(row, 'offer', 'Offer') || row;
                const offer = val(snap, 'offer', 'Offer') || snap;

                const offerId = val(offer, 'offerId', 'OfferId') || '-';
                const orderId = val(offer, 'marketplaceOrderId', 'MarketplaceOrderId') || '-';
                const statusVal = val(offer, 'status', 'Status');
                const creator = val(offer, 'offerCreatorCharacterId', 'OfferCreatorCharacterId') || '-';
                const owner = val(offer, 'orderOwnerCharacterId', 'OrderOwnerCharacterId') || '-';
                const offeredGold = val(offer, 'offeredGold', 'OfferedGold') ?? 0;
                const assets = val(offer, 'offeredAssets', 'OfferedAssets') || [];
                const assetSummary = Array.isArray(assets)
                    ? assets.map(a => `${val(a, 'itemDefinitionId', 'ItemDefinitionId') || '?'} x${val(a, 'quantity', 'Quantity') ?? 1}`).join(', ')
                    : '-';

                const createdAt = val(offer, 'createdAtUtc', 'CreatedAtUtc');
                const expiresAt = val(offer, 'expiresAtUtc', 'ExpiresAtUtc');
                const respondedAt = val(offer, 'respondedAtUtc', 'RespondedAtUtc');
                const acceptedAt = val(offer, 'acceptedAtUtc', 'AcceptedAtUtc');
                const rejectedAt = val(offer, 'rejectedAtUtc', 'RejectedAtUtc');
                const cancelledAt = val(offer, 'cancelledAtUtc', 'CancelledAtUtc');

                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(String(offerId))}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(orderId))}</td>
                    <td>${offerStatePill(statusVal)}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(creator))}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(owner))}</td>
                    <td>${esc(String(offeredGold))}</td>
                    <td>${esc(assetSummary || '-')}</td>
                    <td>${createdAt ? fmtDate(createdAt) : '-'}</td>
                    <td>${expiresAt ? fmtDate(expiresAt) : '-'}</td>
                    <td>${respondedAt ? fmtDate(respondedAt) : '-'}</td>
                    <td>${acceptedAt ? fmtDate(acceptedAt) : '-'}</td>
                    <td>${rejectedAt ? fmtDate(rejectedAt) : '-'}</td>
                    <td>${cancelledAt ? fmtDate(cancelledAt) : '-'}</td>
                    <td><button class="btn" onclick="openDrawer_marketplaceOfferDetail('${esc(String(offerId))}','${esc(String(realmId))}')">View</button></td>
                </tr>`;
            }).join('');
        } catch (e) {
            console.error(e);
            body.innerHTML = '<tr><td colspan="14" class="empty-row">Network error.</td></tr>';
        }
    }

    function renderDetail(detail) {
        const snap = val(detail, 'order', 'Order') || {};
        const order = val(snap, 'order', 'Order') || snap;
        const tax = val(snap, 'tax', 'Tax') || {};
        const escrowSummary = val(detail, 'escrowSummary', 'EscrowSummary') || null;
        const tx = val(detail, 'transactions', 'Transactions') || [];

        const htmlOrder = `
            <div class="card" style="margin-bottom:12px;">
                <div class="card-title">Marketplace Order</div>
                <table class="kv-table"><tbody>
                    <tr><td>OrderId</td><td class="mono">${esc(String(val(order, 'orderId', 'OrderId') || '-'))}</td></tr>
                    <tr><td>OrderType</td><td>${esc(mapType(val(order, 'orderType', 'OrderType')))}</td></tr>
                    <tr><td>Status</td><td>${statePill(val(order, 'status', 'Status'))}</td></tr>
                    <tr><td>OwnerCharacterId</td><td class="mono">${esc(String(val(order, 'ownerCharacterId', 'OwnerCharacterId') || '-'))}</td></tr>
                    <tr><td>OwnerAccountId</td><td class="mono">${esc(String(val(order, 'ownerAccountId', 'OwnerAccountId') || '-'))}</td></tr>
                    <tr><td>ItemDefinitionId</td><td>${esc(String(val(order, 'itemDefinitionId', 'ItemDefinitionId') || '-'))}</td></tr>
                    <tr><td>Quantity</td><td>${esc(String(val(order, 'quantityRemaining', 'QuantityRemaining') ?? '-'))} / ${esc(String(val(order, 'quantityTotal', 'QuantityTotal') ?? '-'))}</td></tr>
                    <tr><td>UnitPriceGold</td><td>${esc(String(val(order, 'unitPriceGold', 'UnitPriceGold') ?? '-'))}</td></tr>
                    <tr><td>ListingFeePaid</td><td>${esc(String(val(order, 'listingFeePaid', 'ListingFeePaid') ?? '-'))}</td></tr>
                    <tr><td>TaxAmount</td><td>${esc(String(val(tax, 'completionTaxGold', 'CompletionTaxGold') ?? val(order, 'completionTaxReserved', 'CompletionTaxReserved') ?? '-'))}</td></tr>
                    <tr><td>CreatedAt</td><td>${val(order, 'createdAtUtc', 'CreatedAtUtc') ? fmtDate(val(order, 'createdAtUtc', 'CreatedAtUtc')) : '-'}</td></tr>
                    <tr><td>ExpiresAt</td><td>${val(order, 'expiresAtUtc', 'ExpiresAtUtc') ? fmtDate(val(order, 'expiresAtUtc', 'ExpiresAtUtc')) : '-'}</td></tr>
                    <tr><td>FulfilledAt</td><td>${val(order, 'fulfilledAtUtc', 'FulfilledAtUtc') ? fmtDate(val(order, 'fulfilledAtUtc', 'FulfilledAtUtc')) : '-'}</td></tr>
                    <tr><td>CancelledAt</td><td>${val(order, 'cancelledAtUtc', 'CancelledAtUtc') ? fmtDate(val(order, 'cancelledAtUtc', 'CancelledAtUtc')) : '-'}</td></tr>
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

        const htmlTx = `
            <div class="card">
                <div class="card-title">Transactions</div>
                ${!tx.length
                    ? '<p style="font-size:12px;color:var(--dim);">No transactions recorded for this order.</p>'
                    : `<div class="scroll-x"><table>
                        <thead><tr><th>TransactionId</th><th>Buyer</th><th>Seller</th><th>Qty</th><th>Unit Gold</th><th>Gross</th><th>Tax</th><th>Net</th><th>Created</th></tr></thead>
                        <tbody>${tx.map(t => `
                            <tr>
                                <td class="mono" style="font-size:11px;">${esc(String(val(t, 'transactionId', 'TransactionId') || '-'))}</td>
                                <td class="mono" style="font-size:11px;">${esc(String(val(t, 'buyerCharacterId', 'BuyerCharacterId') || '-'))}</td>
                                <td class="mono" style="font-size:11px;">${esc(String(val(t, 'sellerCharacterId', 'SellerCharacterId') || '-'))}</td>
                                <td>${esc(String(val(t, 'quantity', 'Quantity') ?? '-'))}</td>
                                <td>${esc(String(val(t, 'unitPriceGold', 'UnitPriceGold') ?? '-'))}</td>
                                <td>${esc(String(val(t, 'grossGold', 'GrossGold') ?? '-'))}</td>
                                <td>${esc(String(val(t, 'taxGold', 'TaxGold') ?? '-'))}</td>
                                <td>${esc(String(val(t, 'netSettlementGold', 'NetSettlementGold') ?? '-'))}</td>
                                <td>${val(t, 'createdAtUtc', 'CreatedAtUtc') ? fmtDate(val(t, 'createdAtUtc', 'CreatedAtUtc')) : '-'}</td>
                            </tr>
                        `).join('')}</tbody>
                    </table></div>`
                }
            </div>`;

        return htmlOrder + htmlEscrow + htmlTx;
    }

    window.openDrawer_marketplaceDetail = async function (orderId, realmId) {
        try {
            const res = await api(`/admin/api/marketplace/detail/${encodeURIComponent(orderId)}?realmId=${encodeURIComponent(realmId || 'default')}`);
            if (!res.ok) {
                Admin.openDrawer('Marketplace Detail', `<p class="empty-row">Error ${res.status}</p>`, 'Close', () => Admin.closeDrawer(), 'btn');
                return;
            }

            const detail = await res.json();
            Admin.openDrawer('Marketplace Detail', renderDetail(detail), 'Close', () => Admin.closeDrawer(), 'btn');
        } catch (e) {
            console.error(e);
            Admin.openDrawer('Marketplace Detail', '<p class="empty-row">Network error.</p>', 'Close', () => Admin.closeDrawer(), 'btn');
        }
    };

    window.refreshMarketplaceAdmin = () => loadMarketplace();
    window.refreshMarketplaceOffersAdmin = () => loadMarketplaceOffers();

    window.clearMarketplaceFilters = function () {
        $('mktFilterType').value = '';
        $('mktFilterStatus').value = '';
        $('mktFilterCharacter').value = '';
        $('mktFilterItem').value = '';
        $('mktPage').value = '1';
        $('mktPageSize').value = '100';
        loadMarketplace();
    };

    window.clearMarketplaceOfferFilters = function () {
        $('mktOfferFilterStatus').value = '';
        $('mktOfferFilterOrder').value = '';
        $('mktOfferFilterCharacter').value = '';
        $('mktOfferFilterItem').value = '';
        $('mktOfferPage').value = '1';
        $('mktOfferPageSize').value = '100';
        loadMarketplaceOffers();
    };

    window.openDrawer_marketplaceOfferDetail = async function (offerId, realmId) {
        try {
            const res = await api(`/admin/api/marketplace/offers/detail/${encodeURIComponent(offerId)}?realmId=${encodeURIComponent(realmId || 'default')}`);
            if (!res.ok) {
                Admin.openDrawer('Marketplace Offer Detail', `<p class="empty-row">Error ${res.status}</p>`, 'Close', () => Admin.closeDrawer(), 'btn');
                return;
            }

            const detail = await res.json();
            const snap = val(detail, 'offer', 'Offer') || {};
            const offer = val(snap, 'offer', 'Offer') || snap;
            const order = val(detail, 'order', 'Order');
            const escrowSummary = val(detail, 'escrowSummary', 'EscrowSummary');
            const assets = val(offer, 'offeredAssets', 'OfferedAssets') || [];

            const html = `
                <div class="card" style="margin-bottom:12px;">
                    <div class="card-title">Marketplace Offer</div>
                    <table class="kv-table"><tbody>
                        <tr><td>OfferId</td><td class="mono">${esc(String(val(offer, 'offerId', 'OfferId') || '-'))}</td></tr>
                        <tr><td>OrderId</td><td class="mono">${esc(String(val(offer, 'marketplaceOrderId', 'MarketplaceOrderId') || '-'))}</td></tr>
                        <tr><td>Status</td><td>${offerStatePill(val(offer, 'status', 'Status'))}</td></tr>
                        <tr><td>Creator</td><td class="mono">${esc(String(val(offer, 'offerCreatorCharacterId', 'OfferCreatorCharacterId') || '-'))}</td></tr>
                        <tr><td>Owner</td><td class="mono">${esc(String(val(offer, 'orderOwnerCharacterId', 'OrderOwnerCharacterId') || '-'))}</td></tr>
                        <tr><td>OfferedGold</td><td>${esc(String(val(offer, 'offeredGold', 'OfferedGold') ?? 0))}</td></tr>
                        <tr><td>SubmissionFee</td><td>${esc(String(val(offer, 'submissionFeeGold', 'SubmissionFeeGold') ?? 0))}</td></tr>
                        <tr><td>CreatedAt</td><td>${val(offer, 'createdAtUtc', 'CreatedAtUtc') ? fmtDate(val(offer, 'createdAtUtc', 'CreatedAtUtc')) : '-'}</td></tr>
                        <tr><td>ExpiresAt</td><td>${val(offer, 'expiresAtUtc', 'ExpiresAtUtc') ? fmtDate(val(offer, 'expiresAtUtc', 'ExpiresAtUtc')) : '-'}</td></tr>
                        <tr><td>RespondedAt</td><td>${val(offer, 'respondedAtUtc', 'RespondedAtUtc') ? fmtDate(val(offer, 'respondedAtUtc', 'RespondedAtUtc')) : '-'}</td></tr>
                    </tbody></table>
                </div>
                <div class="card" style="margin-bottom:12px;">
                    <div class="card-title">Offered Items</div>
                    ${!assets.length
                        ? '<p style="font-size:12px;color:var(--dim);">No item assets.</p>'
                        : `<div class="scroll-x"><table><thead><tr><th>Item Definition</th><th>Item Instance</th><th>Qty</th></tr></thead><tbody>${assets.map(a => `<tr><td>${esc(String(val(a, 'itemDefinitionId', 'ItemDefinitionId') || '-'))}</td><td class="mono" style="font-size:11px;">${esc(String(val(a, 'itemInstanceId', 'ItemInstanceId') || '-'))}</td><td>${esc(String(val(a, 'quantity', 'Quantity') ?? '-'))}</td></tr>`).join('')}</tbody></table></div>`
                    }
                </div>
                ${order ? `<div class="card" style="margin-bottom:12px;"><div class="card-title">Linked Order</div><p class="mono" style="font-size:12px;">${esc(JSON.stringify(order, null, 2))}</p></div>` : ''}
                ${escrowSummary ? `<div class="card"><div class="card-title">Escrow Summary</div><p class="mono" style="font-size:12px;">${esc(JSON.stringify(escrowSummary, null, 2))}</p></div>` : ''}
            `;

            Admin.openDrawer('Marketplace Offer Detail', html, 'Close', () => Admin.closeDrawer(), 'btn');
        } catch (e) {
            console.error(e);
            Admin.openDrawer('Marketplace Offer Detail', '<p class="empty-row">Network error.</p>', 'Close', () => Admin.closeDrawer(), 'btn');
        }
    };

    async function onEnter(args) {
        await loadMarketplace(args || {});
        await loadMarketplaceOffers();
    }

    Admin.registerPage('marketplace', { onEnter });
})();
