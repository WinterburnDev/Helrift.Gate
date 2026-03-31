(function () {
    const { $, esc, fmtDate, api, linkAccount, linkCharByName, linkGuild, pillSide, onlineBadge } = Admin;

    const SIDE_NAMES = { 0: 'None', 1: 'Aresden', 2: 'Elvine', 3: 'Traveller' };
    const EQUIP_SLOT_NAMES = { 0: 'None', 1: 'Head', 2: 'Body', 3: 'Arms', 4: 'Legs', 5: 'Back', 6: 'Neck', 7: 'Ring1', 8: 'Ring2', 9: 'Weapon', 10: 'Offhand' };

    // Stored so drawer callbacks can reference the currently loaded character
    let _currentAccountId = null;
    let _currentCharId = null;
    let _currentCharName = null;

    function kv(label, value) {
        return `<tr><td>${esc(label)}</td><td>${value === null || value === undefined ? '-' : esc(String(value))}</td></tr>`;
    }

    function kvHtml(label, html) {
        return `<tr><td>${esc(label)}</td><td>${html}</td></tr>`;
    }

    function section(title, content, collapsed) {
        const id = 'cdsec_' + title.replace(/\W/g, '_');
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

    function renderItemTable(items, label) {
        if (!items || !items.length) return `<p style="color:var(--dim); font-size:12px;">No ${label.toLowerCase()}.</p>`;

        const equipped = items.filter(i => i.isEquipped);
        const unequipped = items.filter(i => !i.isEquipped);
        let html = '';

        if (equipped.length) {
            html += `<h3 style="font-size:13px; margin:8px 0 4px; color:var(--muted);">Equipped</h3>`;
            html += itemTable(equipped);
        }
        if (unequipped.length) {
            html += `<h3 style="font-size:13px; margin:12px 0 4px; color:var(--muted);">Bag (${unequipped.length})</h3>`;
            html += itemTable(unequipped);
        }
        return html;
    }

    function itemTable(items) {
        return `<div class="scroll-x"><table>
            <thead><tr><th>Item ID</th><th>Slot</th><th>Qty</th><th>Quality</th><th>Endurance</th><th>Upgrade</th><th>Crafted</th><th>Stats</th></tr></thead>
            <tbody>${items.map(i => {
                const stats = (i.stats || []).map(s => `${s.type}:${s.value}`).join(', ');
                return `<tr>
                    <td class="mono" style="font-size:11px;">${esc(i.itemId || '')}</td>
                    <td>${esc(EQUIP_SLOT_NAMES[i.equipmentSlot] || String(i.equipmentSlot || ''))}</td>
                    <td>${i.quantity ?? 1}</td>
                    <td>${i.quality ?? '-'}</td>
                    <td>${i.endurance ?? '-'}</td>
                    <td>${i.upgradeLevel || '-'}</td>
                    <td>${i.isCrafted ? '✔' : ''}</td>
                    <td style="font-size:11px; max-width:200px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;">${esc(stats || '-')}</td>
                </tr>`;
            }).join('')}</tbody></table></div>`;
    }

    function renderSkills(skills) {
        if (!skills || !Object.keys(skills).length) return '<p style="color:var(--dim); font-size:12px;">No skills.</p>';
        return `<div class="scroll-x"><table>
            <thead><tr><th>Skill</th><th>Total XP</th><th>Attributes</th></tr></thead>
            <tbody>${Object.entries(skills).map(([k, v]) => {
                const attrs = v.attributes ? Object.entries(v.attributes).map(([ak, av]) => `${ak}: ${av}`).join(', ') : '-';
                return `<tr><td>${esc(k)}</td><td>${v.totalXp != null ? Math.floor(v.totalXp).toLocaleString() : '-'}</td><td style="font-size:11px;">${esc(attrs)}</td></tr>`;
            }).join('')}</tbody></table></div>`;
    }

    function renderQuests(quests) {
        if (!quests) return '<p style="color:var(--dim); font-size:12px;">No quest data.</p>';
        let html = '';
        const active = quests.activeQuests || [];
        const completed = quests.completedQuests || [];
        if (active.length) {
            html += `<h3 style="font-size:13px; margin:4px 0; color:var(--muted);">Active (${active.length})</h3>`;
            html += `<div class="scroll-x"><table><thead><tr><th>Quest ID</th><th>Tracked</th><th>Progress</th></tr></thead><tbody>`;
            html += active.map(q => `<tr><td class="mono" style="font-size:11px;">${esc(q.questId || '')}</td><td>${q.isTracked ? '✔' : ''}</td><td style="font-size:11px;">${(q.stepProgress || []).join(', ') || '-'}</td></tr>`).join('');
            html += '</tbody></table></div>';
        }
        if (completed.length) {
            html += `<h3 style="font-size:13px; margin:8px 0 4px; color:var(--muted);">Completed (${completed.length})</h3>`;
            html += `<p style="font-size:11px; color:var(--dim); word-break:break-all;">${completed.map(c => esc(c)).join(', ')}</p>`;
        }
        return html || '<p style="color:var(--dim); font-size:12px;">No quests.</p>';
    }

    function renderFriends(friends) {
        if (!friends || !Object.keys(friends).length) return '<p style="color:var(--dim); font-size:12px;">No friends.</p>';
        return `<div class="scroll-x"><table><thead><tr><th>Character ID</th><th>Name</th><th>Since</th><th>Note</th></tr></thead><tbody>` +
            Object.entries(friends).map(([id, f]) =>
                `<tr><td class="mono" style="font-size:11px;">${esc(id)}</td><td>${f.name ? linkCharByName(f.name) : '-'}</td><td>${esc(f.since || '-')}</td><td>${esc(f.note || '-')}</td></tr>`
            ).join('') + '</tbody></table></div>';
    }

    function renderTitles(titles) {
        if (!titles) return '<p style="color:var(--dim); font-size:12px;">No title data.</p>';
        let html = `<p style="font-size:12px; color:var(--muted);">Active: <strong>${esc(titles.activeTitle || 'None')}</strong></p>`;
        const list = titles.titles || [];
        if (list.length) {
            html += `<div class="scroll-x"><table><thead><tr><th>Title ID</th><th>Amount</th><th>Last Level</th><th>Updated</th></tr></thead><tbody>`;
            html += list.map(t => `<tr><td class="mono" style="font-size:11px;">${esc(t.titleId || '')}</td><td>${t.amount ?? '-'}</td><td>${t.lastLevel ?? '-'}</td><td>${t.lastUpdated ? fmtDate(t.lastUpdated) : '-'}</td></tr>`).join('');
            html += '</tbody></table></div>';
        }
        return html;
    }

    function renderStats(stats) {
        if (!stats || !Object.keys(stats).length) return '<p style="color:var(--dim); font-size:12px;">No lifetime stats.</p>';
        return `<div class="scroll-x"><table><thead><tr><th>Stat</th><th>Value</th></tr></thead><tbody>` +
            Object.entries(stats).sort((a, b) => a[0].localeCompare(b[0])).map(([k, v]) =>
                `<tr><td>${esc(k)}</td><td><strong>${Number(v).toLocaleString()}</strong></td></tr>`
            ).join('') + '</tbody></table></div>';
    }

    function renderProgression(prog) {
        if (!prog) return '<p style="color:var(--dim); font-size:12px;">No progression data.</p>';
        return `<table class="kv-table"><tbody>
            ${kv('Level', prog.level)}
            ${kv('XP', prog.xp?.toLocaleString())}
            ${kv('Lifetime XP', prog.lifetimeXp?.toLocaleString())}
            ${kv('Rebirths', prog.rebirths)}
            ${kv('Mastery Points', prog.masteryPoints)}
            ${kv('Majestic Points', prog.majesticPoints)}
            ${kv('Divine Favour', prog.divineFavour)}
            ${kv('Weekly Crusade Favour', prog.weeklyCrusadeFavourEarned)}
            ${kv('Weekly EK Favour', prog.weeklyEkFavourEarned)}
        </tbody></table>`;
    }

    function renderCosmetics(cos) {
        if (!cos) return '<p style="color:var(--dim); font-size:12px;">No cosmetic data.</p>';
        let html = `<table class="kv-table"><tbody>${kv('Active Cast Effect', cos.activeCastEffect)}</tbody></table>`;
        if (cos.unlockedCastEffects?.length) html += `<p style="font-size:11px; margin-top:6px;"><strong>Unlocked Effects:</strong> ${cos.unlockedCastEffects.map(e => esc(e)).join(', ')}</p>`;
        if (cos.unlockedSkins?.length) html += `<p style="font-size:11px; margin-top:4px;"><strong>Unlocked Skins:</strong> ${cos.unlockedSkins.map(s => esc(s)).join(', ')}</p>`;
        return html;
    }

    // ── Drawer: Kick ─────────────────────────────────────────────────────────

    window.openDrawer_kickCharacter = function () {
        const badge = onlineBadge(_currentCharName, true);
        Admin.openDrawer(
            'Kick Player',
            `<div style="display:flex; align-items:center; gap:8px; margin-bottom:16px;">
                <span style="font-size:14px; font-weight:500;">${esc(_currentCharName || _currentCharId)}</span>
                ${badge}
            </div>
            <p style="font-size:13px; color:var(--muted); margin:0 0 16px;">Sends a kick command to the game server. The player must be currently online.</p>
            <div class="form-grid">
                <div class="field" style="grid-column:1/-1">
                    <label>Reason</label>
                    <input id="dKickReason" type="text" value="Kicked by admin" />
                </div>
            </div>`,
            'Kick Player',
            doKickCharacter,
            'btn-warning'
        );
    };

    async function doKickCharacter() {
        const reason = $('dKickReason').value.trim() || 'Kicked by admin';
        const st = $('drawerStatus');
        st.textContent = '';

        try {
            const res = await api(`/admin/api/characters/${encodeURIComponent(_currentAccountId)}/${encodeURIComponent(_currentCharId)}/kick`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ reason, initiatedBy: 'admin' })
            });
            if (res.ok) {
                const data = await res.json();
                Admin.closeDrawer();
                const prev = $('pageTitle').textContent;
                $('pageTitle').textContent = '✅ Kick sent — ' + prev;
                setTimeout(() => { $('pageTitle').textContent = prev; }, 3000);
            } else {
                const txt = await res.text();
                st.textContent = `Error ${res.status}: ${txt}`;
            }
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    // ── Drawer: Ban ──────────────────────────────────────────────────────────

    window.openDrawer_banCharacter = function () {
        const badge = onlineBadge(_currentCharName, true);
        Admin.openDrawer(
            'Ban Player',
            `<div style="display:flex; align-items:center; gap:8px; margin-bottom:16px;">
                <span style="font-size:14px; font-weight:500;">${esc(_currentCharName || _currentCharId)}</span>
                ${badge}
            </div>
            <p style="font-size:13px; color:var(--muted); margin:0 0 16px;">Creates a ban record and kicks the player if currently online. Requires Steam ID or IP address.</p>
            <div class="form-grid" style="grid-template-columns:1fr 1fr;">
                <div class="field"><label>Steam ID</label><input id="dBanCharSteam" type="text" placeholder="76561198…" /></div>
                <div class="field"><label>IP Address</label><input id="dBanCharIp" type="text" placeholder="192.168.x.x" /></div>
                <div class="field"><label>Duration (minutes)</label><input id="dBanCharDuration" type="number" min="1" /><small>Empty = permanent ban</small></div>
                <div class="field"><label>Initiated By</label><input id="dBanCharBy" type="text" value="admin" /></div>
                <div class="field" style="grid-column:1/-1"><label>Reason</label><input id="dBanCharReason" type="text" placeholder="e.g. Cheating, exploiting…" /></div>
            </div>`,
            'Ban Player',
            doBanCharacter,
            'btn-danger'
        );
    };

    async function doBanCharacter() {
        const steamId = $('dBanCharSteam').value.trim();
        const ipAddress = $('dBanCharIp').value.trim();
        const durationRaw = $('dBanCharDuration').value.trim();
        const reason = $('dBanCharReason').value.trim();
        const createdBy = $('dBanCharBy').value.trim() || 'admin';
        const st = $('drawerStatus');
        st.textContent = '';

        if (!steamId && !ipAddress) { st.textContent = 'Steam ID or IP Address is required.'; return; }
        if (!reason) { st.textContent = 'Reason is required.'; return; }

        try {
            const res = await api(`/admin/api/characters/${encodeURIComponent(_currentAccountId)}/${encodeURIComponent(_currentCharId)}/ban`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    realmId: 'default',
                    steamId: steamId || null,
                    ipAddress: ipAddress || null,
                    reason,
                    durationMinutes: durationRaw ? parseInt(durationRaw, 10) : null,
                    createdBy
                })
            });
            if (res.ok) {
                Admin.closeDrawer();
                const prev = $('pageTitle').textContent;
                $('pageTitle').textContent = '✅ Ban created — ' + prev;
                setTimeout(() => { $('pageTitle').textContent = prev; }, 3000);
            } else {
                const txt = await res.text();
                st.textContent = `Error ${res.status}: ${txt}`;
            }
        } catch (e) { st.textContent = 'Network error.'; console.error(e); }
    }

    async function loadCharacter(args) {
        const container = $('charDetailContent');
        if (!args || !args.accountId || !args.charId) {
            container.innerHTML = '<div class="empty-row">No character specified.</div>';
            return;
        }

        _currentAccountId = args.accountId;
        _currentCharId = args.charId;
        _currentCharName = null;

        container.innerHTML = '<div class="empty-row">Loading…</div>';

        // Ensure presence cache is fresh so the badge is accurate
        await Admin.fetchPlayers();

        try {
            const res = await api(`/admin/api/characters/${encodeURIComponent(args.accountId)}/${encodeURIComponent(args.charId)}`);
            if (res.status === 404) { container.innerHTML = '<div class="empty-row">Character not found.</div>'; return; }
            if (!res.ok) { container.innerHTML = `<div class="empty-row">Error ${res.status}</div>`; return; }
            const c = await res.json();

            _currentCharName = c.characterName || null;
            const sideName = SIDE_NAMES[c.side] || String(c.side || '-');

            // ── Action bar ────────────────────────────────────────────────────
            let html = `
                <div class="action-bar">
                    <button class="btn" onclick="history.back()">← Back</button>
                    ${onlineBadge(c.characterName, true)}
                    <div class="action-bar-spacer"></div>
                    <button class="btn btn-warning" onclick="openDrawer_kickCharacter()">
                        <span class="material-symbols-outlined" style="font-size:15px;vertical-align:-3px;">logout</span> Kick
                    </button>
                    <button class="btn btn-danger" onclick="openDrawer_banCharacter()">
                        <span class="material-symbols-outlined" style="font-size:15px;vertical-align:-3px;">gavel</span> Ban
                    </button>
                    <button class="btn btn-primary" onclick="openDrawer_sendSystemDeliveryToCharacter('${esc(c.username || '')}','${esc(c.id || '')}','${esc(c.characterName || '')}')">
                        <span class="material-symbols-outlined" style="font-size:15px;vertical-align:-3px;">send</span> Send Delivery
                    </button>
                </div>`;

            // ── Identity ──
            html += section('Identity', `<table class="kv-table"><tbody>
                ${kv('Character Name', c.characterName)}
                ${kv('Character ID', c.id)}
                ${kvHtml('Account (Username)', linkAccount(c.username))}
                ${kvHtml('Side', pillSide(sideName))}
                ${kv('Side Status', c.sideStatus)}
                ${kv('Gender', c.gender)}
                ${kv('Map', c.mapId)}
                ${kv('Position', c.position ? `${c.position.x.toFixed(1)}, ${c.position.y.toFixed(1)}, ${c.position.z.toFixed(1)}` : '-')}
                ${kv('Is Admin', c.isAdmin ? 'Yes' : 'No')}
                ${kv('Last Logged In', c.lastLoggedIn ? fmtDate(c.lastLoggedIn) : '-')}
            </tbody></table>`);

            // ── Combat Stats ──
            html += section('Combat Stats', `<table class="kv-table"><tbody>
                ${kv('Level', c.level)}
                ${kv('HP', c.hp)}
                ${kv('MP', c.mp)}
                ${kv('SP', c.sp)}
                ${kv('Strength', c.strength)}
                ${kv('Dexterity', c.dexterity)}
                ${kv('Vitality', c.vitality)}
                ${kv('Intelligence', c.intelligence)}
                ${kv('Magic', c.magic)}
                ${kv('Finesse', c.finesse)}
                ${kv('Experience', c.experience?.toLocaleString())}
                ${kv('Criticals', c.criticals)}
                ${kv('Enemy Kill Points', c.enemyKillPoints)}
                ${kv('Majestic Points', c.majesticPoints)}
            </tbody></table>`);

            // ── Progression ──
            html += section('Progression', renderProgression(c.progression));

            // ── Guild ──
            html += section('Guild', c.guild
                ? `<table class="kv-table"><tbody>${kvHtml('Guild ID', linkGuild(c.guild.guildId))}${kv('Guild Name', c.guild.guildName)}</tbody></table>`
                : '<p style="color:var(--dim); font-size:12px;">Not in a guild.</p>');

            // ── Inventory ──
            html += section('Inventory (' + (c.inventory?.length || 0) + ')', renderItemTable(c.inventory, 'Inventory'), true);

            // ── Warehouse ──
            html += section('Warehouse (' + (c.warehouse?.length || 0) + ')', renderItemTable(c.warehouse, 'Warehouse'), true);

            // ── Skills ──
            html += section('Skills (' + (c.skills ? Object.keys(c.skills).length : 0) + ')', renderSkills(c.skills), true);

            // ── Quests ──
            html += section('Quests', renderQuests(c.quests), true);

            // ── Titles ──
            html += section('Titles', renderTitles(c.titles), true);

            // ── Friends ──
            html += section('Friends (' + (c.friends ? Object.keys(c.friends).length : 0) + ')', renderFriends(c.friends), true);

            // ── Lifetime Stats ──
            html += section('Lifetime Stats', renderStats(c.stats), true);

            // ── Cosmetics ──
            html += section('Cosmetics', renderCosmetics(c.cosmetics), true);

            // ── Beastiary ──
            const beastCount = c.beastiary?.entries?.length || 0;
            html += section('Beastiary (' + beastCount + ')', beastCount
                ? `<div class="scroll-x"><table><thead><tr><th>NPC ID</th><th>Encountered</th><th>Progress</th></tr></thead><tbody>` +
                  c.beastiary.entries.map(b => `<tr><td class="mono" style="font-size:11px;">${esc(b.npcId || '')}</td><td>${b.encountered ? '✔' : ''}</td><td style="font-size:11px;">${(b.progress || []).map(p => `${esc(p.targetId)}:${p.quantity}${p.fulfilled ? '✔' : ''}`).join(', ') || '-'}</td></tr>`).join('') +
                  '</tbody></table></div>'
                : '<p style="color:var(--dim); font-size:12px;">No beastiary entries.</p>', true);

            // ── Research ──
            html += section('Research', c.research
                ? `<div class="json-box"><pre class="json-block">${esc(JSON.stringify(c.research, null, 2))}</pre></div>`
                : '<p style="color:var(--dim); font-size:12px;">No research data.</p>', true);

            // ── Spells ──
            html += section('Spells', c.spells?.spells
                ? `<p style="font-size:12px;">Spell data (base64): <code style="font-size:11px; word-break:break-all;">${esc(c.spells.spells)}</code></p>`
                : '<p style="color:var(--dim); font-size:12px;">No spell data.</p>', true);

            // ── Tutorials ──
            html += section('Tutorials', c.tutorials
                ? `<p style="font-size:12px;">Disabled: ${c.tutorials.tutorialsDisabled ? 'Yes' : 'No'}</p>` +
                  (c.tutorials.completedIds?.length ? `<p style="font-size:11px; color:var(--dim);">Completed: ${c.tutorials.completedIds.map(t => esc(t)).join(', ')}</p>` : '')
                : '<p style="color:var(--dim); font-size:12px;">No tutorial data.</p>', true);

            // ── Deliveries ──
            const recentDeliveries = await fetchRecentDeliveries(c.id);
            html += section('Deliveries', renderRecentDeliveries(recentDeliveries, c.id), true);

            // ── Raw JSON ──
            html += section('Raw JSON', `<div class="json-box"><pre class="json-block">${esc(JSON.stringify(c, null, 2))}</pre></div>`, true);

            container.innerHTML = html;

            $('pageTitle').textContent = 'Character: ' + (c.characterName || c.id);

        } catch (e) {
            container.innerHTML = '<div class="empty-row">Network error.</div>';
            console.error(e);
        }
    }

    async function fetchRecentDeliveries(characterId) {
        try {
            const q = new URLSearchParams({
                realmId: 'default',
                characterId: characterId,
                page: '1',
                pageSize: '5'
            });
            const res = await api('/admin/api/deliveries/search?' + q.toString());
            if (!res.ok) return [];
            const data = await res.json();
            return data.items || [];
        } catch {
            return [];
        }
    }

    function renderRecentDeliveries(items, characterId) {
        if (!items.length) {
            return `<div class="action-bar">
                <button class="btn" onclick="Admin.switchPage('deliveries',{recipientCharacterId:'${esc(characterId)}'})">Open Deliveries</button>
                <button class="btn btn-primary" onclick="openDrawer_sendSystemDeliveryToCharacter('${esc(_currentAccountId || '')}','${esc(characterId)}','${esc(_currentCharName || '')}')">Send Delivery</button>
            </div><p style="font-size:12px;color:var(--dim);">No recent deliveries for this character.</p>`;
        }

        return `<div class="action-bar">
                <button class="btn" onclick="Admin.switchPage('deliveries',{recipientCharacterId:'${esc(characterId)}'})">Open Deliveries</button>
                <button class="btn btn-primary" onclick="openDrawer_sendSystemDeliveryToCharacter('${esc(_currentAccountId || '')}','${esc(characterId)}','${esc(_currentCharName || '')}')">Send Delivery</button>
            </div>
            <div class="scroll-x"><table>
                <thead><tr><th>ID</th><th>Type</th><th>State</th><th>Created</th><th>Escrow</th><th></th></tr></thead>
                <tbody>
                    ${items.map(d => `
                        <tr>
                            <td class="mono" style="font-size:11px;">${esc(d.id || '')}</td>
                            <td>${esc(String(d.type))}</td>
                            <td>${esc(String(d.state))}</td>
                            <td>${d.createdUtc ? fmtDate(d.createdUtc) : '-'}</td>
                            <td>${d.escrowContainerId ? '<span class="pill pill-green">Yes</span>' : '<span class="pill pill-neutral">No</span>'}</td>
                            <td><button class="btn" onclick="openDrawer_deliveryDetailById('${esc(d.id || '')}','default')">View</button></td>
                        </tr>
                    `).join('')}
                </tbody>
            </table></div>`;
    }

    Admin.registerPage('character', { onEnter: loadCharacter });
})();