// Navigation, clock, auto-refresh orchestration.

(function () {
    const { $ } = Admin;

    const NAV_TITLES = {
        dashboard: 'Dashboard', servers: 'Game Servers', realm: 'Realm State',
        'realm-events': 'Realm Events', presence: 'Online Players', characters: 'Characters',
        character: 'Character Detail', accounts: 'Accounts', parties: 'Parties',
        guilds: 'Guilds', guild: 'Guild Detail', chat: 'Chat Broadcast', merchants: 'Merchants',
        leaderboards: 'Leaderboards', bans: 'Bans', server: 'Game Server Detail',
        deliveries: 'Deliveries', configs: 'Configs', 'town-project-configs': 'Town Project Configs'
    };

    let currentPage = 'dashboard';

    function buildHash(page, args) {
        const params = new URLSearchParams();
        if (args && typeof args === 'object') {
            Object.entries(args).forEach(([k, v]) => {
                if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
            });
        }

        const qs = params.toString();
        return qs ? `#${page}?${qs}` : `#${page}`;
    }

    function parseHash() {
        const raw = (location.hash || '').replace(/^#/, '');
        if (!raw) return { page: 'dashboard', args: undefined };

        const [pagePart, queryPart] = raw.split('?');
        const page = pagePart || 'dashboard';

        const args = {};
        if (queryPart) {
            const q = new URLSearchParams(queryPart);
            q.forEach((v, k) => { args[k] = v; });
        }

        return { page, args: Object.keys(args).length ? args : undefined };
    }

    function isKnownPage(page) {
        return !!document.getElementById('page-' + page);
    }

    function switchPage(page, args, skipHistory) {
        if (!isKnownPage(page)) page = 'dashboard';

        document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
        document.querySelectorAll('.nav-link').forEach(n => n.classList.remove('active'));

        const el = document.getElementById('page-' + page);
        if (el) el.classList.add('active');

        const nav = document.querySelector(`[data-page="${page}"]`);
        if (nav) nav.classList.add('active');

        $('pageTitle').textContent = NAV_TITLES[page] || page;
        currentPage = page;

        if (!skipHistory) {
            history.pushState({ page, args: args || null }, '', buildHash(page, args));
        }

        const handler = Admin.pageHandlers[page];
        if (handler && handler.onEnter) handler.onEnter(args);
    }

    window.addEventListener('popstate', function (e) {
        if (e.state && e.state.page) {
            switchPage(e.state.page, e.state.args || undefined, true);
            return;
        }

        const parsed = parseHash();
        switchPage(parsed.page, parsed.args, true);
    });

    Admin.switchPage = switchPage;

    document.querySelectorAll('.nav-link[data-page]').forEach(link => {
        link.addEventListener('click', () => switchPage(link.dataset.page));
    });

    setInterval(() => {
        $('headerClock').textContent = new Date().toISOString().replace('T', ' ').replace('Z', ' UTC');
    }, 1000);

    async function tickRefresh() {
        const handler = Admin.pageHandlers[currentPage];
        if (handler && handler.onTick) await handler.onTick();
    }

    Admin.pageHandlers = {};

    Admin.registerPage = function (name, handler) {
        Admin.pageHandlers[name] = handler;
    };

    Admin._boot = function () {
        const fromHash = parseHash();
        const initialPage = isKnownPage(fromHash.page) ? fromHash.page : 'dashboard';
        const initialArgs = isKnownPage(fromHash.page) ? fromHash.args : undefined;

        history.replaceState(
            { page: initialPage, args: initialArgs || null },
            '',
            buildHash(initialPage, initialArgs)
        );

        switchPage(initialPage, initialArgs, true);
        setInterval(tickRefresh, 5000);
    };
})();