(function () {
    const { $, esc, fmtDate, api } = Admin;

    let _versions = [];
    let _realmSelections = [];

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

    // ── Enum Name Mappings ──────────────────────────────────────────
    const enumNames = {
        category: {
            0: 'Unknown', 1: 'WeeklyGeneral', 2: 'CrusadePreparation'
        },
        contributionType: {
            0: 'Unknown', 1: 'MonsterKill', 2: 'PlayerKill', 3: 'ItemDelivery'
        },
        rewardType: {
            0: 'Unknown', 1: 'Buff', 2: 'Currency', 3: 'Item'
        },
        rewardScope: {
            0: 'Unknown', 1: 'Individual', 2: 'Town', 3: 'Realm'
        },
        selectionMode: {
            0: 'Unknown', 1: 'WeightedRandom'
        },
        individualRewardMode: {
            0: 'Unknown', 1: 'AllCitizens', 2: 'Contributors', 3: 'TopContributors'
        }
    };

    function getEnumName(type, value) {
        return (enumNames[type] && enumNames[type][value]) || String(value);
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
        const compareLeft = $('tpCompareLeft');
        const compareRight = $('tpCompareRight');

        if (!_versions.length) {
            body.innerHTML = '<tr><td colspan="5" class="empty-row">No config versions found.</td></tr>';
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
                <td><button class="btn" onclick="tpLoadVersionIntoEditor('${esc(v.version || '')}')">Load Into Editor</button></td>
            </tr>`).join('');

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

    window.tpLoadVersionIntoEditor = async function (version) {
        try {
            const config = await fetchJson(`/admin/api/config/town-projects/versions/${encodeURIComponent(version)}`);
            $('tpEditorVersion').value = config.version || version;
            $('tpEditorJson').value = prettyJson(config);
            _visualEditConfig = JSON.parse(JSON.stringify(config)); // Deep copy for visual editor
            populateVisualEditor(config);
            $('tpVisualEditorHost').style.display = 'block';
            $('tpJsonEditorSection').style.display = 'none';
            _editorMode = 'visual';
            setEditorStatus(`Loaded ${version} into editor.`);
            renderValidationResult(null);
        } catch (err) {
            console.error(err);
            setEditorStatus(err.message || 'Failed to load version into editor.');
        }
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
            // If in visual mode, sync to JSON first
            if (_editorMode === 'visual' && _visualEditConfig) {
                syncVisualToJson();
            }

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

    // ── Visual Editor State ──────────────────────────────────────────
    let _editorMode = 'json'; // 'json' or 'visual'
    let _visualEditConfig = null; // In-memory config being edited

    // ── Visual Editor Mode ──────────────────────────────────────────

    function syncJsonToVisual() {
        try {
            const config = readEditorConfig();
            _visualEditConfig = JSON.parse(JSON.stringify(config)); // Deep copy
            populateVisualEditor(config);
            setEditorStatus('Synced JSON to visual editor.');
        } catch (err) {
            setEditorStatus('Failed to sync JSON: ' + err.message);
        }
    }

    function syncVisualToJson() {
        if (!_visualEditConfig) {
            setEditorStatus('No visual config loaded.');
            return;
        }
        $('tpEditorJson').value = prettyJson(_visualEditConfig);
        setEditorStatus('Synced visual editor to JSON.');
    }

    function populateVisualEditor(config) {
        const host = $('tpVisualEditorHost');
        if (!host) return;

        const reqPoolOptions = Object.keys(config.requirementPools || {})
            .map(id => `<option value="${esc(id)}">${esc(id)}</option>`)
            .join('');

        const itemGroupOptions = Object.keys(config.itemGroups || {})
            .map(id => `<option value="${esc(id)}">${esc(id)}</option>`)
            .join('');

        host.innerHTML = `
            <div class="card">
                <div class="tabs" id="tpVisualTabs">
                    <button class="tab-btn active" data-tab="definitions">Definitions</button>
                    <button class="tab-btn" data-tab="pools">Requirement Pools</button>
                    <button class="tab-btn" data-tab="groups">Item Groups</button>
                </div>

                <!-- Definitions Tab -->
                <div id="tpTab-definitions" class="tab-content active">
                    <div style="margin-bottom: 12px;">
                        <button class="btn btn-success" onclick="tpAddDefinition()">+ Add Definition</button>
                    </div>
                    <div id="tpDefinitionsList"></div>
                </div>

                <!-- Requirement Pools Tab -->
                <div id="tpTab-pools" class="tab-content" style="display:none;">
                    <div style="margin-bottom: 12px;">
                        <button class="btn btn-success" onclick="tpAddRequirementPool()">+ Add Requirement Pool</button>
                    </div>
                    <div id="tpRequirementPoolsList"></div>
                </div>

                <!-- Item Groups Tab -->
                <div id="tpTab-groups" class="tab-content" style="display:none;">
                    <div style="margin-bottom: 12px;">
                        <button class="btn btn-success" onclick="tpAddItemGroup()">+ Add Item Group</button>
                    </div>
                    <div id="tpItemGroupsList"></div>
                </div>
            </div>`;

        // Set up tab switching
        document.querySelectorAll('#tpVisualTabs .tab-btn').forEach(btn => {
            btn.onclick = (e) => {
                const tabName = e.target.dataset.tab;
                document.querySelectorAll('#tpVisualTabs .tab-btn').forEach(b => b.classList.remove('active'));
                document.querySelectorAll('.tab-content').forEach(t => t.style.display = 'none');
                e.target.classList.add('active');
                $(`tpTab-${tabName}`).style.display = 'block';
            };
        });

        // Populate sections
        renderDefinitionsList(config.definitions || {});
        renderRequirementPoolsList(config.requirementPools || {}, reqPoolOptions);
        renderItemGroupsList(config.itemGroups || {});
    }

    function renderDefinitionsList(definitions) {
        const host = $('tpDefinitionsList');
        const ids = Object.keys(definitions).sort();

        if (!ids.length) {
            host.innerHTML = '<div class="section-desc">No definitions yet. Click "Add Definition" to create one.</div>';
            return;
        }

        host.innerHTML = ids.map(id => {
            const def = definitions[id];
            return `
                <div class="card" style="margin-bottom: 12px;">
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
                        <div>
                            <strong>${esc(def.name || id)}</strong>
                            <div class="section-desc">${esc(id)}</div>
                        </div>
                        <button class="btn btn-small" onclick="tpEditDefinition('${esc(id)}')">Edit</button>
                        <button class="btn btn-small btn-danger" onclick="tpDeleteDefinition('${esc(id)}')">Delete</button>
                    </div>
                    <table class="kv-table"><tbody>
                        <tr><td>Category</td><td>${esc(getEnumName('category', def.category))}</td></tr>
                        <tr><td>Requirement Pool</td><td class="mono">${esc(def.requirementPoolId || '-')}</td></tr>
                        <tr><td>Reward Type</td><td>${esc(getEnumName('rewardType', def.rewardType))}</td></tr>
                        <tr><td>Enabled</td><td>${def.isEnabled ? '✓' : '✗'}</td></tr>
                    </tbody></table>
                </div>`;
        }).join('');
    }

    function renderRequirementPoolsList(pools, reqPoolOptions) {
        const host = $('tpRequirementPoolsList');
        const ids = Object.keys(pools).sort();

        if (!ids.length) {
            host.innerHTML = '<div class="section-desc">No requirement pools yet. Click "Add Requirement Pool" to create one.</div>';
            return;
        }

        host.innerHTML = ids.map(id => {
            const pool = pools[id];
            const entryCount = (pool.entries || []).length;
            return `
                <div class="card" style="margin-bottom: 12px;">
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
                        <div>
                            <strong>${esc(id)}</strong>
                            <div class="section-desc">${entryCount} entries</div>
                        </div>
                        <button class="btn btn-small" onclick="tpEditRequirementPool('${esc(id)}')">Edit</button>
                        <button class="btn btn-small btn-danger" onclick="tpDeleteRequirementPool('${esc(id)}')">Delete</button>
                    </div>
                    <table class="kv-table"><tbody>
                        <tr><td>Selection Mode</td><td>${esc(getEnumName('selectionMode', pool.selectionMode) || 'WeightedRandom')}</td></tr>
                        <tr><td>Prevent Repeat</td><td>${pool.preventImmediateRepeat ? '✓' : '✗'}</td></tr>
                        <tr><td>History Size</td><td>${pool.recentHistorySize || 1}</td></tr>
                    </tbody></table>
                </div>`;
        }).join('');
    }

    function renderItemGroupsList(groups) {
        const host = $('tpItemGroupsList');
        const ids = Object.keys(groups).sort();

        if (!ids.length) {
            host.innerHTML = '<div class="section-desc">No item groups yet. Click "Add Item Group" to create one.</div>';
            return;
        }

        host.innerHTML = ids.map(id => {
            const group = groups[id];
            return `
                <div class="card" style="margin-bottom: 12px;">
                    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px;">
                        <div>
                            <strong>${esc(group.name || id)}</strong>
                            <div class="section-desc">${esc(id)}</div>
                        </div>
                        <button class="btn btn-small" onclick="tpEditItemGroup('${esc(id)}')">Edit</button>
                        <button class="btn btn-small btn-danger" onclick="tpDeleteItemGroup('${esc(id)}')">Delete</button>
                    </div>
                    <div style="padding: 8px; background: #f5f5f5; border-radius: 4px; font-size: 0.9em;">
                        Items (${(group.itemIds || []).length}): ${esc((group.itemIds || []).join(', ') || 'None')}
                    </div>
                </div>`;
        }).join('');
    }

    window.tpSwitchEditorMode = function (mode) {
        if (mode === 'visual' && !_visualEditConfig) {
            syncJsonToVisual();
        }
        _editorMode = mode;
        const visualHost = $('tpVisualEditorHost');
        const jsonEditor = $('tpJsonEditorSection');

        if (mode === 'visual') {
            visualHost.style.display = 'block';
            jsonEditor.style.display = 'none';
        } else {
            visualHost.style.display = 'none';
            jsonEditor.style.display = 'block';
        }
    };

    window.tpAddDefinition = function () {
        const id = prompt('Enter definition ID (e.g., "weekly-gather"):');
        if (!id) return;
        if (_visualEditConfig.definitions[id]) {
            alert('Definition with that ID already exists.');
            return;
        }

        _visualEditConfig.definitions[id] = {
            id: id,
            name: id,
            description: '',
            category: 0,
            requirementPoolId: '',
            contributionType: 0,
            requiredItemId: null,
            targetProgress: 100,
            progressPerContributionUnit: 1,
            reputationPerContributionUnit: 1,
            rewardType: 0,
            rewardScope: 0,
            rewardValue: '',
            rewardDurationSeconds: 0,
            eventType: 0,
            isEnabled: true,
            individualRewardMode: 0
        };

        renderDefinitionsList(_visualEditConfig.definitions);
    };

    window.tpEditDefinition = function (id) {
        const def = _visualEditConfig.definitions[id];
        if (!def) return;

        const pools = Object.keys(_visualEditConfig.requirementPools || {});

        const form = `
            <div style="border: 1px solid rgba(255,255,255,.08); padding: 12px; border-radius: 4px; background: rgba(255,255,255,.03);">
                <h3>${esc(id)}</h3>
                <div class="form-grid">
                    <div class="field">
                        <label>Name</label>
                        <input type="text" value="${esc(def.name || '')}" onchange="_visualEditConfig.definitions['${id}'].name = this.value;">
                    </div>
                    <div class="field">
                        <label>Category</label>
                        <select onchange="_visualEditConfig.definitions['${id}'].category = parseInt(this.value);">
                            <option value="0"${def.category === 0 ? ' selected' : ''}>Unknown (0)</option>
                            <option value="1"${def.category === 1 ? ' selected' : ''}>WeeklyGeneral (1)</option>
                            <option value="2"${def.category === 2 ? ' selected' : ''}>CrusadePreparation (2)</option>
                        </select>
                    </div>
                </div>

                <div class="field" style="margin-top: 12px;">
                    <label>Description</label>
                    <textarea onchange="_visualEditConfig.definitions['${id}'].description = this.value;">${esc(def.description || '')}</textarea>
                </div>

                <div class="form-grid" style="margin-top: 12px;">
                    <div class="field">
                        <label>Requirement Pool ID</label>
                        <select onchange="_visualEditConfig.definitions['${id}'].requirementPoolId = this.value;">
                            <option value=""${!def.requirementPoolId ? ' selected' : ''}>-- None --</option>
                            ${pools.map(p => `<option value="${esc(p)}"${def.requirementPoolId === p ? ' selected' : ''}>${esc(p)}</option>`).join('')}
                        </select>
                    </div>
                    <div class="field">
                        <label>Reward Type</label>
                        <select onchange="_visualEditConfig.definitions['${id}'].rewardType = parseInt(this.value);">
                            <option value="0"${def.rewardType === 0 ? ' selected' : ''}>Unknown (0)</option>
                            <option value="1"${def.rewardType === 1 ? ' selected' : ''}>Buff (1)</option>
                            <option value="2"${def.rewardType === 2 ? ' selected' : ''}>Currency (2)</option>
                            <option value="3"${def.rewardType === 3 ? ' selected' : ''}>Item (3)</option>
                        </select>
                    </div>
                </div>

                <div class="form-grid" style="margin-top: 12px;">
                    <div class="field">
                        <label>Reward Scope</label>
                        <select onchange="_visualEditConfig.definitions['${id}'].rewardScope = parseInt(this.value);">
                            <option value="0"${def.rewardScope === 0 ? ' selected' : ''}>Unknown (0)</option>
                            <option value="1"${def.rewardScope === 1 ? ' selected' : ''}>Individual (1)</option>
                            <option value="2"${def.rewardScope === 2 ? ' selected' : ''}>Town (2)</option>
                            <option value="3"${def.rewardScope === 3 ? ' selected' : ''}>Realm (3)</option>
                        </select>
                    </div>
                    <div class="field">
                        <label>Reward Value (string ID)</label>
                        <input type="text" value="${esc(def.rewardValue || '')}" onchange="_visualEditConfig.definitions['${id}'].rewardValue = this.value;">
                    </div>
                </div>

                <div class="form-grid" style="margin-top: 12px;">
                    <div class="field">
                        <label>Enabled</label>
                        <input type="checkbox" ${def.isEnabled ? 'checked' : ''} onchange="_visualEditConfig.definitions['${id}'].isEnabled = this.checked;">
                    </div>
                    <div class="field">
                        <label>Individual Reward Mode</label>
                        <select onchange="_visualEditConfig.definitions['${id}'].individualRewardMode = parseInt(this.value);">
                            <option value="0"${def.individualRewardMode === 0 ? ' selected' : ''}>Unknown (0)</option>
                            <option value="1"${def.individualRewardMode === 1 ? ' selected' : ''}>AllCitizens (1)</option>
                            <option value="2"${def.individualRewardMode === 2 ? ' selected' : ''}>Contributors (2)</option>
                            <option value="3"${def.individualRewardMode === 3 ? ' selected' : ''}>TopContributors (3)</option>
                        </select>
                    </div>
                </div>

                <div style="margin-top: 12px;">
                    <button class="btn" onclick="tpDoneEditingDefinition('${esc(id)}')">Done Editing</button>
                </div>
            </div>`;

        $('tpDefinitionsList').innerHTML = form;
    };

    window.tpDeleteDefinition = function (id) {
        if (!confirm(`Delete definition "${id}"?`)) return;
        delete _visualEditConfig.definitions[id];
        renderDefinitionsList(_visualEditConfig.definitions);
        syncVisualToJson();
    };

    window.tpAddRequirementPool = function () {
        const id = prompt('Enter requirement pool ID (e.g., "basic-pool"):');
        if (!id) return;
        if (_visualEditConfig.requirementPools[id]) {
            alert('Pool with that ID already exists.');
            return;
        }

        _visualEditConfig.requirementPools[id] = {
            id: id,
            selectionMode: 1,
            preventImmediateRepeat: true,
            recentHistorySize: 1,
            entries: []
        };

        const pools = Object.keys(_visualEditConfig.requirementPools || {})
            .map(pid => `<option value="${esc(pid)}">${esc(pid)}</option>`)
            .join('');
        renderRequirementPoolsList(_visualEditConfig.requirementPools, pools);
    };

    window.tpEditRequirementPool = function (id) {
        const pool = _visualEditConfig.requirementPools[id];
        if (!pool) return;

        const form = `
            <div style="border: 1px solid rgba(255,255,255,.08); padding: 12px; border-radius: 4px; background: rgba(255,255,255,.03);">
                <h3>Pool: ${esc(id)}</h3>
                <div class="form-grid">
                    <div class="field">
                        <label>Selection Mode</label>
                        <select onchange="_visualEditConfig.requirementPools['${id}'].selectionMode = parseInt(this.value);">
                            <option value="1"${pool.selectionMode === 1 ? ' selected' : ''}>WeightedRandom (1)</option>
                        </select>
                    </div>
                    <div class="field">
                        <label>Prevent Immediate Repeat</label>
                        <input type="checkbox" ${pool.preventImmediateRepeat ? 'checked' : ''} onchange="_visualEditConfig.requirementPools['${id}'].preventImmediateRepeat = this.checked;">
                    </div>
                    <div class="field">
                        <label>Recent History Size</label>
                        <input type="number" value="${pool.recentHistorySize || 1}" onchange="_visualEditConfig.requirementPools['${id}'].recentHistorySize = parseInt(this.value);">
                    </div>
                </div>

                <h4 style="margin-top: 12px;">Entries (${(pool.entries || []).length})</h4>
                <button class="btn btn-success" onclick="tpAddRequirementPoolEntry('${esc(id)}')">+ Add Entry</button>

                <div style="margin-top: 12px;">
                    ${(pool.entries || []).map((entry, idx) => `
                        <div style="padding: 8px; background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.08); border-radius: 3px; margin-bottom: 8px;">
                            <div style="display: flex; justify-content: space-between;">
                                <div>
                                    <strong>${esc(entry.id || 'entry-' + idx)}</strong>
                                    <div class="section-desc">${esc(getEnumName('contributionType', entry.contributionType))} · Weight: ${entry.weight || 1}</div>
                                </div>
                                <div>
                                    <button class="btn btn-small" onclick="tpEditPoolEntry('${esc(id)}', ${idx})">Edit</button>
                                    <button class="btn btn-small btn-danger" onclick="(function(){_visualEditConfig.requirementPools['${id}'].entries.splice(${idx}, 1); tpEditRequirementPool('${id}'); syncVisualToJson();})()">Delete</button>
                                </div>
                            </div>
                        </div>
                    `).join('')}
                </div>

                <div style="margin-top: 12px;">
                    <button class="btn" onclick="tpDoneEditingRequirementPool('${esc(id)}')">Done Editing</button>
                </div>
            </div>`;

        $('tpRequirementPoolsList').innerHTML = form;
    };

    window.tpAddRequirementPoolEntry = function (poolId) {
        const pool = _visualEditConfig.requirementPools[poolId];
        if (!pool) return;

        const entryId = prompt('Enter entry ID:');
        if (!entryId) return;

        pool.entries.push({
            id: entryId,
            weight: 1,
            contributionType: 1,
            targetQuantity: 10,
            progressPerUnit: 1,
            reputationPerUnit: 1,
            allowedItemIds: [],
            allowedItemGroupId: null,
            qualityRule: null,
            conditionRule: null
        });

        tpEditRequirementPool(poolId);
    };

    window.tpEditPoolEntry = function (poolId, idx) {
        const pool = _visualEditConfig.requirementPools[poolId];
        const entry = pool.entries[idx];
        if (!entry) return;

        const itemGroupIds = Object.keys(_visualEditConfig.itemGroups || {});
        
        // Expose entries for onclick handler to modify
        window._tpCurrentPoolEntries = pool.entries;

        const form = `
            <div style="border: 1px solid rgba(255,255,255,.08); padding: 12px; border-radius: 4px; background: rgba(255,255,255,.03);">
                <h4>Entry: ${esc(entry.id || 'entry-' + idx)}</h4>
                <div class="form-grid">
                    <div class="field">
                        <label>Entry ID</label>
                        <input type="text" id="tpEntryId" value="${esc(entry.id || '')}" />
                    </div>
                    <div class="field">
                        <label>Weight</label>
                        <input type="number" id="tpEntryWeight" value="${entry.weight || 1}" />
                    </div>
                </div>

                <div class="form-grid" style="margin-top: 12px;">
                    <div class="field">
                        <label>Contribution Type</label>
                        <select id="tpEntryContribType">
                            <option value="0"${entry.contributionType === 0 ? ' selected' : ''}>Unknown (0)</option>
                            <option value="1"${entry.contributionType === 1 ? ' selected' : ''}>MonsterKill (1)</option>
                            <option value="2"${entry.contributionType === 2 ? ' selected' : ''}>PlayerKill (2)</option>
                            <option value="3"${entry.contributionType === 3 ? ' selected' : ''}>ItemDelivery (3)</option>
                        </select>
                    </div>
                    <div class="field">
                        <label>Target Quantity</label>
                        <input type="number" id="tpEntryTarget" value="${entry.targetQuantity || 0}" />
                    </div>
                </div>

                <div class="form-grid" style="margin-top: 12px;">
                    <div class="field">
                        <label>Progress Per Unit</label>
                        <input type="number" id="tpEntryProgressPerUnit" value="${entry.progressPerUnit || 1}" />
                    </div>
                    <div class="field">
                        <label>Reputation Per Unit</label>
                        <input type="number" id="tpEntryRepPerUnit" value="${entry.reputationPerUnit || 1}" />
                    </div>
                </div>

                <div class="field" style="margin-top: 12px;">
                    <label>Allowed Item IDs (comma-separated)</label>
                    <textarea rows="2" id="tpEntryItemIds">${esc((entry.allowedItemIds || []).join(', '))}</textarea>
                </div>

                <div class="field" style="margin-top: 12px;">
                    <label>Allowed Item Group ID</label>
                    <select id="tpEntryItemGroup">
                        <option value=""${!entry.allowedItemGroupId ? ' selected' : ''}>-- None --</option>
                        ${itemGroupIds.map(g => `<option value="${esc(g)}"${entry.allowedItemGroupId === g ? ' selected' : ''}>${esc(g)}</option>`).join('')}
                    </select>
                </div>

                <div style="margin-top: 12px;">
                    <button class="btn" onclick="(function(){
                        const entries = window._tpCurrentPoolEntries;
                        if (entries && ${idx} < entries.length) {
                            const p = entries[${idx}];
                            p.id = document.getElementById('tpEntryId').value;
                            p.weight = parseInt(document.getElementById('tpEntryWeight').value);
                            p.contributionType = parseInt(document.getElementById('tpEntryContribType').value);
                            p.targetQuantity = parseInt(document.getElementById('tpEntryTarget').value);
                            p.progressPerUnit = parseInt(document.getElementById('tpEntryProgressPerUnit').value);
                            p.reputationPerUnit = parseInt(document.getElementById('tpEntryRepPerUnit').value);
                            p.allowedItemIds = document.getElementById('tpEntryItemIds').value.split(',').map(s => s.trim()).filter(s => s);
                            p.allowedItemGroupId = document.getElementById('tpEntryItemGroup').value || null;
                        }
                        tpDoneEditingPoolEntry('${esc(poolId)}');
                    })()">Done Editing Entry</button>
                </div>
            </div>`;

        $('tpRequirementPoolsList').innerHTML = form;
    };

    window.tpDeleteRequirementPool = function (id) {
        if (!confirm(`Delete pool "${id}"?`)) return;
        delete _visualEditConfig.requirementPools[id];
        const pools = Object.keys(_visualEditConfig.requirementPools || {})
            .map(p => `<option value="${esc(p)}">${esc(p)}</option>`)
            .join('');
        renderRequirementPoolsList(_visualEditConfig.requirementPools, pools);
        syncVisualToJson();
    };

    window.tpAddItemGroup = function () {
        const id = prompt('Enter item group ID (e.g., "quality-lumber"):');
        if (!id) return;
        if (_visualEditConfig.itemGroups[id]) {
            alert('Item group with that ID already exists.');
            return;
        }

        _visualEditConfig.itemGroups[id] = {
            id: id,
            name: id,
            itemIds: []
        };

        renderItemGroupsList(_visualEditConfig.itemGroups);
    };

    window.tpEditItemGroup = function (id) {
        const group = _visualEditConfig.itemGroups[id];
        if (!group) return;

        const form = `
            <div style="border: 1px solid rgba(255,255,255,.08); padding: 12px; border-radius: 4px; background: rgba(255,255,255,.03);">
                <h3>${esc(id)}</h3>
                <div class="form-grid">
                    <div class="field">
                        <label>Name</label>
                        <input type="text" value="${esc(group.name || '')}" onchange="_visualEditConfig.itemGroups['${id}'].name = this.value;">
                    </div>
                </div>

                <div class="field" style="margin-top: 12px;">
                    <label>Item IDs (comma-separated)</label>
                    <textarea rows="4" onchange="_visualEditConfig.itemGroups['${id}'].itemIds = this.value.split('\\n').map(s => s.trim()).filter(s => s);">${esc((group.itemIds || []).join('\n'))}</textarea>
                    <small>Enter one item ID per line. Example: <code>lumber-oak</code>, <code>stone-granite</code></small>
                </div>

                <div style="margin-top: 12px;">
                    <button class="btn" onclick="tpDoneEditingItemGroup('${esc(id)}')">Done Editing</button>
                </div>
            </div>`;

        $('tpItemGroupsList').innerHTML = form;
    };

    window.tpDeleteItemGroup = function (id) {
        if (!confirm(`Delete item group "${id}"?`)) return;
        delete _visualEditConfig.itemGroups[id];
        renderItemGroupsList(_visualEditConfig.itemGroups);
        syncVisualToJson();
    };

    // ── Drawer Handler ────────────────────────────────────────────────
    window.tpOpenAdvanced = function () {
        const html = `
            <div class="scroll-y" style="padding-right: 12px;">
                ${$('tpDrawerRuntimeMeta').innerHTML}
                <div style="margin-top: 24px;"></div>
                ${$('tpDrawerCompare').innerHTML}
                <div style="margin-top: 24px;"></div>
                ${$('tpDrawerRealms').innerHTML}
            </div>`;
        Admin.openDrawer('Advanced Town Project Config Tools', html);
        // Hide the drawer footer buttons since this is read-only/self-contained
        const confirmBtn = document.getElementById('drawerConfirmBtn');
        if (confirmBtn) confirmBtn.style.display = 'none';
        // Make sure cancel button is visible
        const drawerFooter = document.querySelector('.drawer-footer');
        if (drawerFooter) {
            drawerFooter.style.justifyContent = 'flex-end';
        }
    };

    // ── Done Editing Handlers ──────────────────────────────────────
    window.tpDoneEditingDefinition = function (id) {
        renderDefinitionsList(_visualEditConfig.definitions);
        syncVisualToJson();
    };

    window.tpDoneEditingRequirementPool = function (id) {
        const pools = Object.keys(_visualEditConfig.requirementPools || {})
            .map(p => `<option value="${esc(p)}">${esc(p)}</option>`)
            .join('');
        renderRequirementPoolsList(_visualEditConfig.requirementPools, pools);
        syncVisualToJson();
    };

    window.tpDoneEditingPoolEntry = function (poolId) {
        tpEditRequirementPool(poolId);
        syncVisualToJson();
    };

    window.tpDoneEditingItemGroup = function (id) {
        renderItemGroupsList(_visualEditConfig.itemGroups);
        syncVisualToJson();
    };

    window.tpCreateBlankEditor = function () {
        const now = new Date().toISOString();
        const seed = {
            version: '',
            updatedAt: now,
            updatedBy: 'admin-ui',
            publishedAt: null,
            publishedBy: null,
            definitions: {},
            requirementPools: {},
            itemGroups: {}
        };

        $('tpEditorVersion').value = '';
        $('tpEditorJson').value = prettyJson(seed);
        _visualEditConfig = JSON.parse(JSON.stringify(seed));
        populateVisualEditor(seed);
        $('tpVisualEditorHost').style.display = 'block';
        $('tpJsonEditorSection').style.display = 'none';
        _editorMode = 'visual';
        setEditorStatus('Blank Town Project config draft created. Switched to visual editor.');
    };

    Admin.registerPage('town-project-configs', {
        onEnter: refresh
    });
})();