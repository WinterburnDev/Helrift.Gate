// Shared API helpers and cached data fetchers.
// All page modules import from here.

const Admin = window.Admin || (window.Admin = {});

Admin.api = async function (url, opts) {
    return await fetch(url, { cache: 'no-store', ...opts });
};

// ---- Cached data stores ----

Admin._cachedServers = [];
Admin._cachedPlayers = [];
Admin._cachedRealmState = null;

Admin.fetchServers = async function () {
    try {
        const res = await Admin.api('/admin/api/gameservers');
        if (res.ok) Admin._cachedServers = await res.json();
    } catch (e) { console.error('fetchServers', e); }
    return Admin._cachedServers;
};

Admin.fetchPlayers = async function () {
    try {
        const res = await Admin.api('/api/v1/presence/online');
        if (res.ok) Admin._cachedPlayers = await res.json();
    } catch (e) { console.error('fetchPlayers', e); }
    return Admin._cachedPlayers;
};

Admin.fetchRealmState = async function () {
    try {
        const res = await Admin.api('/api/v1/realm/state');
        if (res.ok) Admin._cachedRealmState = await res.json();
    } catch (e) { console.error('fetchRealmState', e); }
    return Admin._cachedRealmState;
};

// ---- DOM / formatting helpers ----

Admin.$ = function (id) { return document.getElementById(id); };

Admin.esc = function (s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
};

Admin.fmtDate = function (d) {
    if (!d) return '-';
    return new Date(d).toISOString().replace('T', ' ').replace('Z', ' UTC');
};

Admin.fmtUnix = function (u) {
    if (!u) return '-';
    return Admin.fmtDate(u * 1000);
};

Admin.pillState = function (state) {
    const s = (state || '').toLowerCase();
    if (s === 'online') return `<span class="pill pill-green">Online</span>`;
    if (s === 'closing') return `<span class="pill pill-yellow">Closing</span>`;
    return `<span class="pill pill-red">${Admin.esc(state || 'Offline')}</span>`;
};

Admin.pillSide = function (side) {
    const raw = (side ?? '').toString().trim();
    const s = raw.toLowerCase();

    const normalized =
        s === '1' ? 'aresden' :
        s === '2' ? 'elvine' :
        (s === '3' || s === '0') ? 'traveller' :
        s;

    if (normalized === 'aresden') {
        return `<span class="pill pill-side pill-side-aresden"><img class="pill-side-logo" src="/admin/img/aresden.png" alt="Aresden logo" />Aresden</span>`;
    }

    if (normalized === 'elvine') {
        return `<span class="pill pill-side pill-side-elvine"><img class="pill-side-logo" src="/admin/img/elvine.png" alt="Elvine logo" />Elvine</span>`;
    }

    if (normalized === 'traveller' || normalized === 'traveler' || normalized === 'travlleller' || normalized === 'neutral' || normalized === 'none') {
        return `<span class="pill pill-side pill-side-neutral">Traveller</span>`;
    }

    if (!raw) {
        return `<span class="pill pill-side pill-side-neutral">-</span>`;
    }

    return `<span class="pill pill-side pill-side-neutral">${Admin.esc(raw)}</span>`;
};

// ---- Presence helpers ----

// Look up a player in the cached presence list by character name (case-insensitive).
// Returns the player object { characterId, characterName, gameServerId, side, lastSeenUtc } or null.
Admin.findOnlinePlayer = function (characterName) {
    if (!characterName) return null;
    const lower = characterName.toLowerCase();
    return Admin._cachedPlayers.find(p =>
        (p.characterName || '').toLowerCase() === lower
    ) || null;
};

// Returns an HTML badge showing online/offline status with a tooltip.
// Options:
//   showLabel (bool) – show "Online"/"Offline" text next to the dot (default false)
Admin.onlineBadge = function (characterName, showLabel) {
    const player = Admin.findOnlinePlayer(characterName);
    const isOnline = !!player;
    const dotClass = isOnline ? 'status-dot-online' : 'status-dot-offline';
    const badgeClass = isOnline ? 'online-badge-online' : 'online-badge-offline';
    const tooltip = isOnline
        ? `Online — ${Admin.esc(player.gameServerId || 'unknown server')}`
        : 'Offline';
    const label = showLabel
        ? `<span class="online-badge-label">${isOnline ? 'Online' : 'Offline'}</span>`
        : '';
    return `<span class="online-badge ${badgeClass}" data-tooltip="${tooltip}"><span class="status-dot ${dotClass}"></span>${label}</span>`;
};

// ---- Cross-link helpers ----
// Returns an <a> tag styled as a link that navigates to a page with args.
// Use these everywhere an accountId, characterName, guildId, etc. appears.

Admin.linkAccount = function (accountId) {
    if (!accountId) return '-';
    return `<a class="admin-link" onclick="Admin.goAccount('${Admin.esc(accountId)}')">${Admin.esc(accountId)}</a>`;
};

Admin.linkCharByName = function (name) {
    if (!name) return '-';
    return `${Admin.onlineBadge(name)} <a class="admin-link" onclick="Admin.goCharSearch('${Admin.esc(name)}')">${Admin.esc(name)}</a>`;
};

Admin.linkCharDirect = function (accountId, charId, label) {
    if (!accountId || !charId) return Admin.esc(label || '-');
    return `${Admin.onlineBadge(label)} <a class="admin-link" onclick="Admin.switchPage('character',{accountId:'${Admin.esc(accountId)}',charId:'${Admin.esc(charId)}'})">${Admin.esc(label || charId)}</a>`;
};

Admin.linkGuild = function (guildId, label) {
    if (!guildId) return '-';
    return `<a class="admin-link" onclick="Admin.switchPage('guild',{guildId:'${Admin.esc(guildId)}'})">${Admin.esc(label || guildId)}</a>`;
};

// ---- Navigation helpers (called by links) ----

Admin.goAccount = function (accountId) {
    Admin.switchPage('accounts', { accountId: accountId });
};

Admin.goCharSearch = function (name) {
    Admin.switchPage('characters', { searchName: name });
};

Admin.goGuildSearch = function (query) {
    Admin.switchPage('guilds', { searchQuery: query });
};

// ---- Drawer ----
// Usage:
//   Admin.openDrawer('Create Ban', '<form html>', 'Submit', () => mySubmitFn(), 'btn-danger');
//   Admin.closeDrawer();

Admin._drawerCallback = null;

Admin.openDrawer = function (title, bodyHtml, confirmLabel, confirmFn, confirmClass) {
    const { $ } = Admin;
    $('drawerTitle').textContent = title;
    $('drawerBody').innerHTML = bodyHtml;
    $('drawerConfirmBtn').textContent = confirmLabel || 'Confirm';
    $('drawerConfirmBtn').className = 'btn ' + (confirmClass || 'btn-primary');
    Admin._drawerCallback = confirmFn || null;

    $('drawerBackdrop').classList.add('open');
    // Defer adding open to drawer so CSS transition fires
    requestAnimationFrame(() => $('drawer').classList.add('open'));

    // Focus first input
    const first = $('drawerBody').querySelector('input, select, textarea');
    if (first) setTimeout(() => first.focus(), 230);
};

Admin.closeDrawer = function () {
    const { $ } = Admin;
    $('drawer').classList.remove('open');
    $('drawerBackdrop').classList.remove('open');
    Admin._drawerCallback = null;
};

Admin._drawerConfirm = function () {
    if (typeof Admin._drawerCallback === 'function') Admin._drawerCallback();
};