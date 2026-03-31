(function () {
    const { $, esc, fmtDate, api } = Admin;

    const DELIVERY_TYPES = {
        1: 'PlayerMessage',
        2: 'Parcel',
        3: 'GuildBroadcast',
        4: 'SystemDelivery'
    };

    const DELIVERY_STATES = {
        1: 'PendingValidation',
        2: 'Accepted',
        3: 'Delivered',
        4: 'Read',
        5: 'ClaimedPartial',
        6: 'ClaimedComplete',
        7: 'Expired',
        8: 'Returned',
        9: 'Failed'
    };

    const ESCROW_ASSET_STATES = {
        1: 'Reserved',
        2: 'Escrowed',
        3: 'Claimable',
        4: 'Claimed',
        5: 'Returned',
        6: 'Forfeited',
        7: 'Expired'
    };

    let _sendBusy = false;
    let _sourceInventoryRows = [];

    function statePill(state) {
        const raw = typeof state === 'number' ? (DELIVERY_STATES[state] || String(state)) : (state || '');
        const s = raw.toLowerCase();
        if (s.includes('failed')) return `<span class="pill pill-red">${esc(raw)}</span>`;
        if (s.includes('expired') || s.includes('returned')) return `<span class="pill pill-orange">${esc(raw)}</span>`;
        if (s.includes('claimed')) return `<span class="pill pill-green">${esc(raw)}</span>`;
        if (s.includes('pending')) return `<span class="pill pill-yellow">${esc(raw)}</span>`;
        return `<span class="pill pill-neutral">${esc(raw || '-')}</span>`;
    }

    function typeLabel(type) {
        if (typeof type === 'number') return DELIVERY_TYPES[type] || String(type);
        return type || '-';
    }

    function val(o, ...keys) {
        for (const k of keys) if (o && o[k] !== undefined && o[k] !== null) return o[k];
        return null;
    }

    function nowKey() {
        return `${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
    }

    function parseRecipients(raw) {
        const tokens = (raw || '')
            .split(/[\n,;]+/g)
            .map(x => x.trim())
            .filter(Boolean);

        const dedupe = new Set();
        const recipients = [];

        for (const t of tokens) {
            const [characterId, accountId] = t.includes('|')
                ? t.split('|').map(x => x.trim())
                : [t, ''];

            if (!characterId) continue;
            if (dedupe.has(characterId)) continue;
            dedupe.add(characterId);

            recipients.push({
                recipientCharacterId: characterId,
                recipientAccountId: accountId || '',
                recipientInventory: 'inventory'
            });
        }

        return recipients;
    }

    // strict parser: line format => itemInstanceId,quantity,itemId(optional)
    function parseAttachments(raw) {
        const lines = (raw || '')
            .split('\n')
            .map(x => x.trim())
            .filter(Boolean);

        const rows = [];
        const errors = [];

        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            const parts = line.split(',').map(x => x.trim());

            if (parts.length < 2) {
                errors.push(`Line ${i + 1}: expected "itemInstanceId,quantity[,itemId]"`);
                continue;
            }

            const itemInstanceId = parts[0] || '';
            if (!itemInstanceId) {
                errors.push(`Line ${i + 1}: itemInstanceId is required.`);
                continue;
            }

            const qty = Number.parseInt(parts[1], 10);
            if (!Number.isFinite(qty) || qty <= 0) {
                errors.push(`Line ${i + 1}: quantity must be a positive integer.`);
                continue;
            }

            const itemId = parts[2] || null;

            rows.push({
                itemInstanceId,
                quantity: qty,
                itemId
            });
        }

        return { rows, errors, lineCount: lines.length };
    }

    function attachmentToLine(a) {
        return `${a.itemInstanceId},${a.quantity}${a.itemId ? `,${a.itemId}` : ''}`;
    }

    function appendAttachmentLine(itemInstanceId, quantity, itemId) {
        const ta = $('dDelAttachments');
        const existing = ta.value.trim();
        const line = attachmentToLine({ itemInstanceId, quantity, itemId });
        ta.value = existing ? `${existing}\n${line}` : line;
    }

    function renderSourceInventoryList() {
        const host = $('dDelSourceInventoryList');
        if (!host) return;

        if (!_sourceInventoryRows.length) {
            host.innerHTML = '<p style="font-size:12px; color:var(--dim); margin:8px 0 0;">No source inventory loaded.</p>';
            return;
        }

        host.innerHTML = `<div class="scroll-x" style="margin-top:8px;"><table>
            <thead><tr><th>Unique ID</th><th>Item</th><th>Qty</th><th>Equipped</th><th></th></tr></thead>
            <tbody>${_sourceInventoryRows.map(r => `
                <tr>
                    <td class="mono" style="font-size:11px;">${esc(r.uniqueId || '')}</td>
                    <td>${esc(r.itemId || '-')}</td>
                    <td>${esc(String(r.quantity ?? 1))}</td>
                    <td>${r.isEquipped ? 'Yes' : 'No'}</td>
                    <td>
                        <button class="btn" onclick="deliveryAddAttachmentFromInventory('${esc(r.uniqueId || '')}','${esc(r.itemId || '')}',${Math.max(1, Number(r.quantity || 1))})">
                            Add
                        </button>
                    </td>
                </tr>
            `).join('')}</tbody>
        </table></div>`;
    }

    window.deliveryAddAttachmentFromInventory = function (uniqueId, itemId, maxQty) {
        const qtyRaw = prompt(`Quantity for ${itemId || uniqueId} (max ${maxQty}):`, '1');
        if (qtyRaw === null) return;

        const qty = Number.parseInt(qtyRaw, 10);
        if (!Number.isFinite(qty) || qty <= 0 || qty > maxQty) {
            $('drawerStatus').textContent = `Invalid quantity. Must be 1..${maxQty}.`;
            return;
        }

        appendAttachmentLine(uniqueId, qty, itemId || null);
        $('drawerStatus').textContent = `Added ${itemId || uniqueId} x${qty}.`;
    };

    window.deliveryLoadSourceInventory = async function () {
        const accountId = $('dDelSourceAccountId').value.trim();
        const charId = $('dDelSourceCharacterId').value.trim();
        const st = $('drawerStatus');
        st.textContent = '';

        if (!accountId || !charId) {
            st.textContent = 'Source account and character are required to load inventory.';
            return;
        }

        try {
            const res = await api(`/admin/api/characters/${encodeURIComponent(accountId)}/${encodeURIComponent(charId)}`);
            if (!res.ok) {
                st.textContent = `Could not load source character: ${res.status}`;
                return;
            }

            const c = await res.json();
            _sourceInventoryRows = (c.inventory || []).map(x => ({
                uniqueId: x.uniqueId || '',
                itemId: x.itemId || '',
                quantity: Number(x.quantity || 1),
                isEquipped: !!x.isEquipped
            }));

            renderSourceInventoryList();
            st.textContent = `Loaded ${_sourceInventoryRows.length} inventory item(s).`;
        } catch (e) {
            console.error(e);
            st.textContent = 'Network error while loading source inventory.';
        }
    };

    async function loadDeliveries(args) {
        const realmId = ($('delRealmId').value || 'default').trim() || 'default';
        const recipient = $('delFilterRecipient').value.trim();
        const sender = $('delFilterSender').value.trim();
        const type = $('delFilterType').value.trim();
        const state = $('delFilterState').value.trim();
        const createdFromUtc = $('delFromUtc').value.trim();
        const createdToUtc = $('delToUtc').value.trim();
        const page = Math.max(1, parseInt($('delPage').value || '1', 10) || 1);
        const pageSize = Math.min(200, Math.max(1, parseInt($('delPageSize').value || '50', 10) || 50));

        const tb = $('deliveriesBody');
        const st = $('deliveriesStatus');
        st.textContent = '';
        tb.innerHTML = '<tr><td colspan="9" class="empty-row">Loading…</td></tr>';

        const q = new URLSearchParams();
        q.set('realmId', realmId);
        q.set('page', String(page));
        q.set('pageSize', String(pageSize));
        if (recipient) q.set('characterId', recipient);
        if (sender) q.set('sender', sender);
        if (type) q.set('type', type);
        if (createdFromUtc) q.set('createdFromUtc', new Date(createdFromUtc).toISOString());
        if (createdToUtc) q.set('createdToUtc', new Date(createdToUtc).toISOString());

        try {
            const res = await api(`/admin/api/deliveries/search?${q.toString()}`);
            if (!res.ok) {
                tb.innerHTML = `<tr><td colspan="9" class="empty-row">Error ${res.status}</td></tr>`;
                return;
            }

            const data = await res.json();
            let items = data.items || [];

            if (state) {
                const wanted = state.toLowerCase();
                items = items.filter(x => (DELIVERY_STATES[x.state] || String(x.state)).toLowerCase() === wanted);
            }

            $('deliveriesTotal').textContent = `${data.total ?? items.length}`;

            if (!items.length) {
                tb.innerHTML = '<tr><td colspan="9" class="empty-row">No deliveries found.</td></tr>';
                return;
            }

            tb.innerHTML = items.map(d => {
                const did = val(d, 'id', 'Id') || '';
                const senderRef = val(d, 'sender', 'Sender') || {};
                const recipientRef = val(d, 'recipient', 'Recipient') || {};
                const senderLabel = val(senderRef, 'displayName', 'DisplayName') || val(senderRef, 'id', 'Id') || '-';
                const recipientId = val(recipientRef, 'id', 'Id') || '-';
                const hasEscrow = !!val(d, 'escrowContainerId', 'EscrowContainerId');
                const stateRaw = val(d, 'state', 'State');
                const typeRaw = val(d, 'type', 'Type');
                const channelRaw = val(d, 'channel', 'Channel');
                const createdUtc = val(d, 'createdUtc', 'CreatedUtc');

                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(did)}</td>
                    <td>${esc(typeLabel(typeRaw))}</td>
                    <td>${esc(String(channelRaw ?? '-'))}</td>
                    <td>${statePill(stateRaw)}</td>
                    <td>${esc(String(senderLabel))}</td>
                    <td class="mono" style="font-size:11px;">${esc(String(recipientId))}</td>
                    <td>${createdUtc ? fmtDate(createdUtc) : '-'}</td>
                    <td>${hasEscrow ? '<span class="pill pill-green">Escrow</span>' : '<span class="pill pill-neutral">Message</span>'}</td>
                    <td><button class="btn" onclick="openDrawer_deliveryDetailById('${esc(did)}','${esc(realmId)}')">View</button></td>
                </tr>`;
            }).join('');
        } catch (e) {
            console.error(e);
            tb.innerHTML = '<tr><td colspan="9" class="empty-row">Network error.</td></tr>';
        }
    }

    function renderEscrowAssets(detail) {
        const container = detail.escrowContainer || detail.EscrowContainer;
        if (!container) return '<p style="font-size:12px; color:var(--dim);">No linked escrow container.</p>';

        const assets = val(container, 'assets', 'Assets') || [];
        if (!assets.length) return '<p style="font-size:12px; color:var(--dim);">Escrow container has no assets.</p>';

        return `<div class="scroll-x"><table>
            <thead><tr><th>Asset ID</th><th>Type</th><th>Item/Key</th><th>Qty</th><th>State</th><th>Claimed</th></tr></thead>
            <tbody>${assets.map(a => {
                const aid = val(a, 'id', 'Id') || '-';
                const at = val(a, 'assetType', 'AssetType');
                const typeLabel = at === 1 ? 'ItemInstance' : (at === 2 ? 'Currency' : (at === 3 ? 'PointBalance' : String(at)));
                const subtype = val(a, 'subtypeKey', 'SubtypeKey') || val(a, 'balanceKey', 'BalanceKey') || val(a, 'itemInstanceId', 'ItemInstanceId') || '-';
                const qty = val(a, 'quantityValue', 'QuantityValue') ?? '-';
                const st = val(a, 'state', 'State');
                const stateName = ESCROW_ASSET_STATES[st] || String(st ?? '-');
                const claimed = val(a, 'claimedUtc', 'ClaimedUtc');
                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(String(aid))}</td>
                    <td>${esc(typeLabel)}</td>
                    <td>${esc(String(subtype))}</td>
                    <td>${esc(String(qty))}</td>
                    <td>${esc(stateName)}</td>
                    <td>${claimed ? fmtDate(claimed) : '-'}</td>
                </tr>`;
            }).join('')}</tbody>
        </table></div>`;
    }

    function renderDetail(detail) {
        const d = detail.delivery || detail.Delivery || {};
        const deliveryId = val(d, 'id', 'Id') || '';
        const realmId = val(d, 'realmId', 'RealmId') || 'default';

        const sender = val(d, 'sender', 'Sender') || {};
        const recipient = val(d, 'recipient', 'Recipient') || {};
        const summary = detail.escrowSummary || detail.EscrowSummary || null;
        const escrowContainerId = val(d, 'escrowContainerId', 'EscrowContainerId');

        const deliveryHtml = `
            <div class="card" style="margin-bottom:12px;">
                <div class="card-title">Delivery</div>
                <table class="kv-table"><tbody>
                    <tr><td>ID</td><td class="mono">${esc(String(val(d, 'id', 'Id') || '-'))}</td></tr>
                    <tr><td>Type</td><td>${esc(typeLabel(val(d, 'type', 'Type')))}</td></tr>
                    <tr><td>Channel</td><td>${esc(String(val(d, 'channel', 'Channel') ?? '-'))}</td></tr>
                    <tr><td>State</td><td>${statePill(val(d, 'state', 'State'))}</td></tr>
                    <tr><td>Sender</td><td>${esc(String(val(sender, 'displayName', 'DisplayName') || val(sender, 'id', 'Id') || '-'))}</td></tr>
                    <tr><td>Recipient</td><td class="mono">${esc(String(val(recipient, 'id', 'Id') || '-'))}</td></tr>
                    <tr><td>Subject</td><td>${esc(String(val(d, 'subject', 'Subject') || '-'))}</td></tr>
                    <tr><td>Body</td><td style="white-space:pre-wrap;">${esc(String(val(d, 'body', 'Body') || '-'))}</td></tr>
                    <tr><td>Created</td><td>${val(d, 'createdUtc', 'CreatedUtc') ? fmtDate(val(d, 'createdUtc', 'CreatedUtc')) : '-'}</td></tr>
                    <tr><td>Updated</td><td>${val(d, 'updatedUtc', 'UpdatedUtc') ? fmtDate(val(d, 'updatedUtc', 'UpdatedUtc')) : '-'}</td></tr>
                    <tr><td>Expiry</td><td>${val(d, 'expiresUtc', 'ExpiresUtc') ? fmtDate(val(d, 'expiresUtc', 'ExpiresUtc')) : '-'}</td></tr>
                    <tr><td>Escrow Container</td><td class="mono">${esc(String(escrowContainerId || '-'))}</td></tr>
                </tbody></table>
            </div>`;

        const summaryHtml = summary
            ? `<div class="card" style="margin-bottom:12px;">
                <div class="card-title">Escrow Summary</div>
                <table class="kv-table"><tbody>
                    <tr><td>Total</td><td>${esc(String(val(summary, 'totalAssets', 'TotalAssets') ?? '-'))}</td></tr>
                    <tr><td>Claimable</td><td>${esc(String(val(summary, 'claimableAssets', 'ClaimableAssets') ?? '-'))}</td></tr>
                    <tr><td>Claimed</td><td>${esc(String(val(summary, 'claimedAssets', 'ClaimedAssets') ?? '-'))}</td></tr>
                    <tr><td>Returned</td><td>${esc(String(val(summary, 'returnedAssets', 'ReturnedAssets') ?? '-'))}</td></tr>
                    <tr><td>Expired</td><td>${esc(String(val(summary, 'expiredAssets', 'ExpiredAssets') ?? '-'))}</td></tr>
                </tbody></table>
            </div>`
            : '';

        const assetsHtml = `
            <div class="card">
                <div class="card-title">Escrow Assets</div>
                ${renderEscrowAssets(detail)}
            </div>`;

        const actionsHtml = `
            <div class="action-bar" style="margin-top:12px;">
                <button class="btn btn-danger" onclick="deleteDeliveryAdmin('${esc(String(deliveryId))}','${esc(String(realmId))}')">
                    Delete Delivery
                </button>
            </div>`;

        return deliveryHtml + summaryHtml + assetsHtml + actionsHtml;
    }

    window.openDrawer_deliveryDetailById = async function (deliveryId, realmId) {
        try {
            const res = await api(`/admin/api/deliveries/detail/${encodeURIComponent(deliveryId)}?realmId=${encodeURIComponent(realmId || 'default')}`);
            if (!res.ok) {
                Admin.openDrawer('Delivery Detail', `<p class="empty-row">Error ${res.status}</p>`, 'Close', () => Admin.closeDrawer(), 'btn');
                return;
            }

            const detail = await res.json();
            Admin.openDrawer(
                'Delivery Detail',
                renderDetail(detail),
                'Close',
                () => Admin.closeDrawer(),
                'btn'
            );
        } catch (e) {
            console.error(e);
            Admin.openDrawer('Delivery Detail', '<p class="empty-row">Network error.</p>', 'Close', () => Admin.closeDrawer(), 'btn');
        }
    };

    window.deleteDeliveryAdmin = async function (deliveryId, realmId) {
        if (!deliveryId) return;

        const force = confirm(
            `Delete delivery '${deliveryId}'?\n\n` +
            `OK = force full cleanup (includes escrow best-effort)\n` +
            `Cancel = normal strict cleanup`
        );

        try {
            const url = `/admin/api/deliveries/${encodeURIComponent(deliveryId)}?realmId=${encodeURIComponent(realmId || 'default')}&cleanupEscrow=true&force=${force ? 'true' : 'false'}`;
            const res = await api(url, { method: 'DELETE' });

            if (res.status === 204) {
                Admin.closeDrawer();
                await loadDeliveries();
                $('deliveriesStatus').textContent = `Deleted delivery ${deliveryId}.`;
                return;
            }

            if (res.status === 404) {
                $('deliveriesStatus').textContent = `Delivery not found: ${deliveryId}`;
                return;
            }

            if (res.status === 409) {
                $('deliveriesStatus').textContent = `Strict cleanup blocked: ${await res.text()}`;
                return;
            }

            $('deliveriesStatus').textContent = `Delete failed (${res.status}): ${await res.text()}`;
        } catch (e) {
            console.error(e);
            $('deliveriesStatus').textContent = 'Network error during delete.';
        }
    };

    function toggleSendMode() {
        const mode = $('dDelMode').value;
        const guild = mode === 'guild';
        $('dDelGuildRow').style.display = guild ? '' : 'none';
        $('dDelRecipientsHelp').textContent = guild
            ? 'Enter character IDs (comma or newline).'
            : 'Enter recipients as characterId or characterId|accountId (comma/newline).';

        // attachments/source are only meaningful for system mode
        $('dDelAttachments').disabled = guild;
        $('dDelSourceAccountId').disabled = guild;
        $('dDelSourceCharacterId').disabled = guild;
        $('dDelLoadSourceInventoryBtn').disabled = guild;
        $('dDelSourceInventoryList').style.opacity = guild ? '0.5' : '1';
    }

    window.openDrawer_sendSystemDelivery = function (prefill) {
        const defaults = prefill || {};
        _sourceInventoryRows = [];

        Admin.openDrawer(
            'Send System Delivery',
            `<div class="form-grid">
                <div class="field"><label>Mode</label>
                    <select id="dDelMode">
                        <option value="system">System Delivery</option>
                        <option value="guild">Guild Broadcast</option>
                    </select>
                </div>
                <div class="field"><label>Sender Label</label>
                    <select id="dDelSenderLabel">
                        <option>System</option>
                        <option>Game Masters</option>
                        <option>Kingdom</option>
                    </select>
                </div>
                <div class="field"><label>Created By</label><input id="dDelCreatedBy" type="text" value="admin" /></div>
                <div class="field"><label>Expiry (UTC)</label><input id="dDelExpiryUtc" type="datetime-local" /></div>
                <div id="dDelGuildRow" class="field" style="display:none;"><label>Guild ID</label><input id="dDelGuildId" type="text" placeholder="guild-id" /></div>
                <div class="field" style="grid-column:1/-1">
                    <label>Recipients</label>
                    <textarea id="dDelRecipients" rows="4" placeholder="charA|accountA&#10;charB|accountB"></textarea>
                    <small id="dDelRecipientsHelp">Enter recipients as characterId or characterId|accountId (comma/newline).</small>
                </div>
                <div class="field"><label>Subject</label><input id="dDelSubject" type="text" /></div>
                <div class="field"><label>Return On Expiry</label>
                    <select id="dDelReturn"><option value="false" selected>No</option><option value="true">Yes</option></select>
                </div>
                <div class="field" style="grid-column:1/-1"><label>Message</label><textarea id="dDelBody" rows="4"></textarea></div>

                <div class="field" style="grid-column:1/-1"><label>Attachments (optional)</label>
                    <textarea id="dDelAttachments" rows="3" placeholder="itemInstanceId,quantity,itemId(optional)"></textarea>
                    <small>One attachment per line. Example: 3d5f...,500,gold</small>
                </div>

                <div class="field"><label>Attachment Source Account</label><input id="dDelSourceAccountId" type="text" /></div>
                <div class="field"><label>Attachment Source Character</label><input id="dDelSourceCharacterId" type="text" /></div>
                <div class="field" style="grid-column:1/-1">
                    <button type="button" class="btn" id="dDelLoadSourceInventoryBtn" onclick="deliveryLoadSourceInventory()">Load Source Inventory</button>
                    <small>Click Add on an item row to append attachment lines automatically.</small>
                    <div id="dDelSourceInventoryList"></div>
                </div>
            </div>`,
            'Send',
            submitSystemDelivery,
            'btn-primary'
        );

        $('dDelMode').addEventListener('change', toggleSendMode);
        toggleSendMode();

        if (defaults.recipientCharacterId) {
            const accountPart = defaults.recipientAccountId ? `|${defaults.recipientAccountId}` : '';
            $('dDelRecipients').value = `${defaults.recipientCharacterId}${accountPart}`;
        }

        if (defaults.subject) $('dDelSubject').value = defaults.subject;
        if (defaults.sourceAccountId) $('dDelSourceAccountId').value = defaults.sourceAccountId;
        if (defaults.sourceCharacterId) $('dDelSourceCharacterId').value = defaults.sourceCharacterId;
    };

    window.openDrawer_sendSystemDeliveryToCharacter = function (accountId, characterId, characterName) {
        openDrawer_sendSystemDelivery({
            recipientAccountId: accountId,
            recipientCharacterId: characterId,
            subject: characterName ? `Delivery for ${characterName}` : '',
            sourceAccountId: accountId,
            sourceCharacterId: characterId
        });
    };

    async function tryReadJson(res) {
        const txt = await res.text();
        if (!txt) return null;
        try { return JSON.parse(txt); } catch { return null; }
    }

    async function refreshDeliveriesIfVisible() {
        // Only refresh list when list elements exist (i.e., deliveries page is mounted).
        const body = $('deliveriesBody');
        if (!body) return;
        await loadDeliveries();
    }

    function setDeliveriesStatus(msg) {
        const el = $('deliveriesStatus');
        if (el) el.textContent = msg;
    }

    async function submitSystemDelivery() {
        if (_sendBusy) return;

        const mode = $('dDelMode').value;
        const senderLabel = $('dDelSenderLabel').value.trim() || 'System';
        const createdBy = $('dDelCreatedBy').value.trim() || 'admin';
        const recipients = parseRecipients($('dDelRecipients').value);
        const subject = $('dDelSubject').value.trim();
        const body = $('dDelBody').value.trim();
        const expiryRaw = $('dDelExpiryUtc').value.trim();
        const expiresUtc = expiryRaw ? new Date(expiryRaw).toISOString() : null;
        const rawAttachments = $('dDelAttachments').value || '';
        const parsed = parseAttachments(rawAttachments);
        const attachments = parsed.rows;
        const returnToSenderOnExpiry = $('dDelReturn').value === 'true';
        const realmId = ($('delRealmId')?.value || 'default').trim() || 'default';
        const st = $('drawerStatus');
        st.textContent = '';

        if (!subject && !body) { st.textContent = 'Subject or message body is required.'; return; }

        if (mode === 'guild') {
            if (rawAttachments.trim().length > 0) {
                st.textContent = 'Guild broadcast does not send attachments. Switch to System mode for asset-backed delivery.';
                return;
            }

            const guildId = $('dDelGuildId').value.trim();
            if (!guildId) { st.textContent = 'Guild ID is required for guild mode.'; return; }
            if (!recipients.length) { st.textContent = 'At least one recipient character ID is required.'; return; }

            _sendBusy = true;
            $('drawerConfirmBtn').disabled = true;
            st.textContent = 'Sending…';

            try {
                // guild send:
                const res = await apiWithTimeout('/admin/api/deliveries/guild-broadcast', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        realmId,
                        idempotencyKey: `admin_guild_${nowKey()}`,
                        guildId,
                        senderId: createdBy,
                        senderDisplayName: senderLabel,
                        subject,
                        body,
                        expiresUtc,
                        recipientCharacterIds: recipients.map(x => x.recipientCharacterId)
                    })
                }, 20000);

                if (!res.ok) {
                    st.textContent = `Error ${res.status}: ${await res.text()}`;
                    return;
                }

                Admin.closeDrawer();
                setDeliveriesStatus(`Sent ${recipients.length} guild delivery record(s).`);
                void refreshDeliveriesIfVisible().catch(err => console.error('refreshDeliveriesIfVisible', err));
                return;
            } catch (e) {
                console.error(e);
                st.textContent = 'Network error.';
                return;
            } finally {
                _sendBusy = false;
                $('drawerConfirmBtn').disabled = false;
            }
        }

        if (!recipients.length) { st.textContent = 'At least one recipient is required.'; return; }
        if (parsed.errors.length > 0) { st.textContent = parsed.errors.join(' '); return; }
        if (rawAttachments.trim().length > 0 && attachments.length === 0) {
            st.textContent = 'Attachment input could not be parsed. Use one line: itemInstanceId,quantity,itemId(optional).';
            return;
        }

        const sourceAccountId = $('dDelSourceAccountId').value.trim();
        const sourceCharacterId = $('dDelSourceCharacterId').value.trim();
        if (attachments.length > 0 && (!sourceAccountId || !sourceCharacterId)) {
            st.textContent = 'Attachment source account and character are required when attachments are included.';
            return;
        }

        _sendBusy = true;
        $('drawerConfirmBtn').disabled = true;
        st.textContent = 'Sending…';

        try {
            const first = recipients[0];
            const payload = {
                realmId,
                idempotencyKey: `admin_sys_${nowKey()}`,
                senderId: createdBy,
                senderDisplayName: senderLabel,
                recipientAccountId: first.recipientAccountId || '',
                recipientCharacterId: first.recipientCharacterId,
                recipientInventory: first.recipientInventory || 'inventory',
                recipients,
                sourceAccountId: sourceAccountId || null,
                sourceCharacterId: sourceCharacterId || null,
                sourceInventory: 'inventory',
                subject,
                body,
                returnToSenderOnExpiry,
                expiresUtc,
                createdByActorType: 'admin',
                createdByActorId: createdBy,
                attachments
            };

            // system send:
            const res = await apiWithTimeout('/admin/api/deliveries/system', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            }, 30000);

            if (!res.ok) {
                st.textContent = `Error ${res.status}: ${await res.text()}`;
                return;
            }

            Admin.closeDrawer();
            setDeliveriesStatus(`Sent ${recipients.length} system delivery record(s).`);
            void refreshDeliveriesIfVisible().catch(err => console.error('refreshDeliveriesIfVisible', err));
            return;
        } catch (e) {
            console.error(e);
            st.textContent = 'Network error.';
        } finally {
            _sendBusy = false;
            $('drawerConfirmBtn').disabled = false;
        }
    }

    window.refreshDeliveries = () => loadDeliveries();

    window.clearDeliveriesFilters = function () {
        $('delFilterRecipient').value = '';
        $('delFilterSender').value = '';
        $('delFilterType').value = '';
        $('delFilterState').value = '';
        $('delFromUtc').value = '';
        $('delToUtc').value = '';
        $('delPage').value = '1';
        loadDeliveries();
    };

    function onEnter(args) {
        if (args && args.recipientCharacterId) $('delFilterRecipient').value = args.recipientCharacterId;
        if (args && args.sender) $('delFilterSender').value = args.sender;
        loadDeliveries(args);
    }

    Admin.registerPage('deliveries', { onEnter: onEnter });

    async function apiWithTimeout(url, options, timeoutMs) {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), timeoutMs);

        try {
            return await api(url, { ...options, signal: controller.signal });
        } finally {
            clearTimeout(timeout);
        }
    }
})();