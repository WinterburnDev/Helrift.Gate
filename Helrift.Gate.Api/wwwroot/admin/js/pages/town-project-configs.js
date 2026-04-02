(function () {
    const { $, esc, fmtDate, api } = Admin;

    let _versions = [];
    let _realmSelections = [];
    let _inspectedConfig = null;

    function setPageStatus(message) {
        $('tpPageStatus').textContent = message || '';
    }

    function setEditorStatus(message) {
        $('tpEditorStatus').textContent = message || '';
    }

    function optionHtml(value, label, selected) {
        return `<option value="${esc(value)}"${selected ? ' selected' : ''}>${esc(label)}</option>`;
    }

    function prettyJson(value) {
        return JSON.stringify(value, null, 2);
    }

    function readEditorConfig() {
        const raw = $('tpEditorJson').value.trim();
        if (!raw) throw new Error('Config JSON is required.');
        return JSON.parse(raw);
    }

    function renderRuntimeMeta(config) {
        const body = $('tpRuntimeMetaBody');

        if (!config) {
            body.innerHTML = '<tr><td>Runtime</td><td>Unavailable</td></tr>';
            return;
        }

        const definitions = Array.isArray(config.definitions) ? config.definitions : [];
        const definitionCount = typeof config.definitionCount === 'number'
            ? config.definitionCount
            : definitions.length;
        body.innerHTML = `
            <tr><td>Loaded Version</td><td class="mono">${esc(config.version || '-')}</td></tr>
            <tr><td>Updated At</td><td>${config.updatedAt ? fmtDate(config.updatedAt) : '-'}</td></tr>
            <tr><td>Updated By</td><td>${esc(config.updatedBy || '-')}</td></tr>
            <tr><td>Published At</td><td>${config.publishedAt ? fmtDate(config.publishedAt) : '-'}</td></tr>
            <tr><td>Published By</td><td>${esc(config.publishedBy || '-')}</td></tr>
            <tr><td>Definitions</td><td>${definitionCount}</td></tr>`;
    }

    function renderVersionsTable() {
        const body = $('tpVersionsBody');
        const inspectSelect = $('tpInspectVersion');
        const compareLeft = $('tpCompareLeft');
        const compareRight = $('tpCompareRight');

        if (!_versions.length) {
            body.innerHTML = '<tr><td colspan="6" class="empty-row">No config versions found.</td></tr>';
            inspectSelect.innerHTML = '';
            compareLeft.innerHTML = '';
            compareRight.innerHTML = '';
            return;
        }

        body.innerHTML = _versions.map(v => `
            <tr>
                <td class="mono">${esc(v.version || '-')}</td>
                <td>${v.updatedAt ? fmtDate(v.updatedAt) : '-'}</td>
                <td>${esc(v.updatedBy || '-')}</td>
                <td>${v.definitionCount ?? 0}</td>
                <td><button class="btn" onclick="tpInspectVersion('${esc(v.version || '')}')">Inspect</button></td>
                <td><button class="btn" onclick="tpLoadVersionIntoEditor('${esc(v.version || '')}')">Load Into Editor</button></td>
            </tr>`).join('');

        inspectSelect.innerHTML = _versions.map((v, index) => optionHtml(v.version, `${v.version} · ${v.definitionCount} defs`, index === 0)).join('');
        compareLeft.innerHTML = _versions.map((v, index) => optionHtml(v.version, v.version, index === 0)).join('');
        compareRight.innerHTML = _versions.map((v, index) => optionHtml(v.version, v.version, index === Math.min(1, _versions.length - 1))).join('');
    }

    function renderRealmSelections() {
        const body = $('tpRealmsBody');

        if (!_realmSelections.length) {
            body.innerHTML = '<tr><td colspan="4" class="empty-row">No realm selections found.</td></tr>';
            return;
        }

        const versionOptions = _versions.map(v => optionHtml(v.version, v.version, false)).join('');

        body.innerHTML = _realmSelections.map(realm => `
            <tr>
                <td class="mono">${esc(realm.realmId || '-')}</td>
                <td class="mono">${esc(realm.version || '-')}</td>
                <td>
                    <select id="tpRealmSelect_${esc(realm.realmId)}">
                        ${_versions.map(v => optionHtml(v.version, v.version, v.version === realm.version)).join('')}
                    </select>
                </td>
                <td><button class="btn" onclick="tpSwitchRealmVersion('${esc(realm.realmId || '')}')">Switch</button></td>
            </tr>`).join('');

        if (!_realmSelections.find(r => (r.realmId || '').toLowerCase() === 'default') && _versions.length) {
            body.innerHTML += `
                <tr>
                    <td class="mono">default</td>
                    <td class="mono">-</td>
                    <td><select id="tpRealmSelect_default">${versionOptions}</select></td>
                    <td><button class="btn" onclick="tpSwitchRealmVersion('default')">Create</button></td>
                </tr>`;
        }
    }

    function renderInspectMeta(config) {
        const host = $('tpInspectMeta');
        if (!config) {
            host.innerHTML = '';
            return;
        }

        host.innerHTML = `
            <div class="card">
                <table class="kv-table"><tbody>
                    <tr><td>Version</td><td class="mono">${esc(config.version || '-')}</td></tr>
                    <tr><td>Updated At</td><td>${config.updatedAt ? fmtDate(config.updatedAt) : '-'}</td></tr>
                    <tr><td>Updated By</td><td>${esc(config.updatedBy || '-')}</td></tr>
                    <tr><td>Published At</td><td>${config.publishedAt ? fmtDate(config.publishedAt) : '-'}</td></tr>
                    <tr><td>Published By</td><td>${esc(config.publishedBy || '-')}</td></tr>
                    <tr><td>Definitions</td><td>${Object.keys(config.definitions || {}).length}</td></tr>
                </tbody></table>
            </div>`;
    }

    function renderInspectDefinitions(config) {
        const body = $('tpInspectDefinitionsBody');
        const definitions = Object.values(config?.definitions || {});

        if (!definitions.length) {
            body.innerHTML = '<tr><td colspan="6" class="empty-row">This version has no definitions.</td></tr>';
            return;
        }

        definitions.sort((a, b) => (a.id || '').localeCompare(b.id || ''));
        body.innerHTML = definitions.map(def => `
            <tr>
                <td class="mono">${esc(def.id || '-')}</td>
                <td>${esc(def.name || '-')}</td>
                <td>${esc(String(def.category ?? '-'))}</td>
                <td>${esc(String(def.contributionType ?? '-'))}</td>
                <td>${esc(String(def.rewardType ?? '-'))} · ${esc(String(def.rewardScope ?? '-'))}</td>
                <td>${def.isEnabled ? '<span class="pill pill-green">Enabled</span>' : '<span class="pill pill-neutral">Disabled</span>'}</td>
            </tr>`).join('');
    }

    function renderValidationResult(validation) {
        const host = $('tpValidationResult');

        if (!validation) {
            host.innerHTML = '';
            return;
        }

        const issues = Array.isArray(validation.issues) ? validation.issues : [];
        if (!issues.length) {
            host.innerHTML = '<div class="card"><span class="pill pill-green">Valid</span> No validation issues.</div>';
            return;
        }

        host.innerHTML = `
            <div class="card">
                <div class="card-title">Validation</div>
                <table>
                    <thead><tr><th>Severity</th><th>Code</th><th>Message</th></tr></thead>
                    <tbody>${issues.map(issue => `
                        <tr>
                            <td>${String(issue.severity).toLowerCase() === 'error' || issue.severity === 0 ? '<span class="pill pill-red">Error</span>' : '<span class="pill pill-yellow">Warning</span>'}</td>
                            <td class="mono">${esc(issue.code || '-')}</td>
                            <td>${esc(issue.message || '-')}</td>
                        </tr>`).join('')}</tbody>
                </table>
            </div>`;
    }

    function renderCompareResult(result) {
        const host = $('tpCompareResult');
        if (!result) {
            host.innerHTML = '';
            return;
        }

        const renderList = (title, items, pillClass) => `
            <div class="card">
                <div class="card-title">${esc(title)} <span class="pill ${pillClass}">${items.length}</span></div>
                ${items.length
                    ? `<div class="json-box"><pre class="json-block">${esc(items.join('\n'))}</pre></div>`
                    : '<div class="section-desc">None</div>'}
            </div>`;

        host.innerHTML = `
            <div class="inline-grid-2">
                ${renderList('Added', result.addedDefinitionIds || [], 'pill-green')}
                ${renderList('Removed', result.removedDefinitionIds || [], 'pill-red')}
            </div>
            <div style="margin-top:16px;">
                ${renderList('Changed', result.changedDefinitionIds || [], 'pill-yellow')}
            </div>`;
    }

    async function fetchJson(url, opts) {
        const res = await api(url, opts);
        const text = await res.text();
        let payload = null;

        try { payload = text ? JSON.parse(text) : null; } catch { payload = text; }

        if (!res.ok) {
            const message = payload?.error || payload?.message || `${res.status} ${res.statusText}`;
            throw new Error(message);
        }

        return payload;
    }

    async function loadRuntimeMeta() {
        const config = await fetchJson('/admin/api/config/town-projects/runtime-metadata');
        renderRuntimeMeta(config);
    }

    async function loadVersions() {
        const data = await fetchJson('/admin/api/config/town-projects/versions');
        _versions = Array.isArray(data?.versions) ? data.versions : [];
        renderVersionsTable();
    }

    async function loadRealmSelections() {
        const data = await fetchJson('/admin/api/config/town-projects/realms');
        _realmSelections = Array.isArray(data?.realms) ? data.realms : [];
        renderRealmSelections();
    }

    async function inspectVersion(version) {
        const config = await fetchJson(`/admin/api/config/town-projects/versions/${encodeURIComponent(version)}`);
        _inspectedConfig = config;
        $('tpInspectVersion').value = version;
        renderInspectMeta(config);
        renderInspectDefinitions(config);
        $('tpInspectStatus').textContent = `Loaded ${version}.`;
    }

    async function refresh() {
        setPageStatus('Loading…');

        try {
            await Promise.all([loadRuntimeMeta(), loadVersions(), loadRealmSelections()]);
            setPageStatus('Ready.');
        } catch (err) {
            console.error(err);
            setPageStatus(err.message || 'Failed to load Town Project config admin data.');
        }
    }

    window.refreshTownProjectConfigsPage = refresh;

    window.tpInspectSelectedVersion = async function () {
        const version = $('tpInspectVersion').value;
        if (!version) return;

        try {
            $('tpInspectStatus').textContent = 'Loading…';
            await inspectVersion(version);
        } catch (err) {
            console.error(err);
            $('tpInspectStatus').textContent = err.message || 'Failed to inspect version.';
        }
    };

    window.tpInspectVersion = async function (version) {
        try {
            $('tpInspectStatus').textContent = 'Loading…';
            await inspectVersion(version);
        } catch (err) {
            console.error(err);
            $('tpInspectStatus').textContent = err.message || 'Failed to inspect version.';
        }
    };

    window.tpLoadVersionIntoEditor = async function (version) {
        try {
            const config = await fetchJson(`/admin/api/config/town-projects/versions/${encodeURIComponent(version)}`);
            $('tpEditorVersion').value = config.version || version;
            $('tpEditorJson').value = prettyJson(config);
            setEditorStatus(`Loaded ${version} into editor.`);
            renderValidationResult(null);
        } catch (err) {
            console.error(err);
            setEditorStatus(err.message || 'Failed to load version into editor.');
        }
    };

    window.tpLoadSelectedVersionIntoEditor = async function () {
        const version = $('tpInspectVersion').value;
        if (!version) {
            setEditorStatus('Select a version first.');
            return;
        }

        await window.tpLoadVersionIntoEditor(version);
    };

    window.tpCompareVersions = async function () {
        const leftVersion = $('tpCompareLeft').value;
        const rightVersion = $('tpCompareRight').value;

        if (!leftVersion || !rightVersion) {
            $('tpCompareStatus').textContent = 'Pick two versions to compare.';
            return;
        }

        $('tpCompareStatus').textContent = 'Comparing…';

        try {
            const result = await fetchJson('/admin/api/config/town-projects/compare', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ leftVersion, rightVersion })
            });

            renderCompareResult(result);
            $('tpCompareStatus').textContent = `Compared ${leftVersion} → ${rightVersion}.`;
        } catch (err) {
            console.error(err);
            $('tpCompareStatus').textContent = err.message || 'Compare failed.';
        }
    };

    window.tpSwitchRealmVersion = async function (realmId) {
        const select = $(`tpRealmSelect_${realmId}`);
        const version = select?.value;
        if (!version) {
            setPageStatus('Select a version first.');
            return;
        }

        setPageStatus(`Persisting ${realmId} → ${version}…`);

        try {
            await fetchJson(`/admin/api/config/town-projects/realms/${encodeURIComponent(realmId)}/version/${encodeURIComponent(version)}`, {
                method: 'PUT'
            });

            await loadRealmSelections();
            setPageStatus(`Persisted ${realmId} → ${version}. Restart/redeploy required to apply.`);
        } catch (err) {
            console.error(err);
            setPageStatus(err.message || 'Failed to switch realm version.');
        }
    };

    window.tpValidateEditorConfig = async function () {
        setEditorStatus('Validating…');

        try {
            const config = readEditorConfig();
            const validation = await fetchJson('/admin/api/config/town-projects/validate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });

            renderValidationResult(validation);
            setEditorStatus(validation?.isValid ? 'Validation passed.' : 'Validation reported issues.');
        } catch (err) {
            console.error(err);
            setEditorStatus(err.message || 'Validation failed.');
        }
    };

    window.tpSaveEditorConfig = async function () {
        setEditorStatus('Saving…');

        try {
            const config = readEditorConfig();
            const version = ($('tpEditorVersion').value || config.version || '').trim();
            if (!version) throw new Error('Version is required before saving.');

            config.version = version;

            const save = await fetchJson(`/admin/api/config/town-projects/versions/${encodeURIComponent(version)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(config)
            });

            renderValidationResult(save?.validation || null);
            await loadVersions();
            await loadRealmSelections();
            setEditorStatus(`Saved version ${version}.`);
        } catch (err) {
            console.error(err);
            setEditorStatus(err.message || 'Save failed.');
        }
    };

    window.tpCreateBlankEditor = function () {
        const now = new Date().toISOString();
        const seed = {
            version: '',
            updatedAt: now,
            updatedBy: 'admin-ui',
            publishedAt: null,
            publishedBy: null,
            definitions: {}
        };

        $('tpEditorVersion').value = '';
        $('tpEditorJson').value = prettyJson(seed);
        renderValidationResult(null);
        setEditorStatus('Blank Town Project config draft created.');
    };

    Admin.registerPage('town-project-configs', {
        onEnter: refresh
    });
})();