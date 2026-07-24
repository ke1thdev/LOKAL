let selectedServerMode = 'lan';

async function renderServer() {
    const container = document.getElementById('page-content');
    container.innerHTML = `
        <div class="server-page">
            <section class="server-hero">
                <div>
                    <span class="server-eyebrow">HYBRID CLASSROOM SERVER</span>
                    <h2>Choose how learners connect</h2>
                    <p>LOKAL keeps classroom data on this computer. Select who can reach the server and which address learners should use.</p>
                </div>
                <div class="server-live-pill"><span></span> Server running</div>
            </section>
            <div id="server-loading" class="server-loading">Loading server configuration...</div>
            <div id="server-content" hidden></div>
        </div>`;

    try {
        const [data, syncStatus, relayStatus] = await Promise.all([
            api.getServerConfig(),
            api.getSyncStatus().catch(() => null),
            api.getRelayStatus().catch(() => null)
        ]);
        drawServerConfiguration(data.config, data.status, syncStatus, relayStatus);
    } catch (error) {
        container.querySelector('#server-loading').innerHTML = `<div class="empty-state"><h3>Server status unavailable</h3><p>${escapeServerHTML(error.message)}</p></div>`;
    }
}

function drawServerConfiguration(config, status, syncStatus = null, relayStatus = null) {
    selectedServerMode = config.mode;
    const loading = document.getElementById('server-loading');
    const content = document.getElementById('server-content');
    loading.hidden = true;
    content.hidden = false;

    const lanURL = status.lan_urls && status.lan_urls.length
        ? `${status.lan_urls[0]}/student/`
        : 'No LAN address detected';

    content.innerHTML = `
        ${status.restart_required ? `
            <div class="server-restart-notice">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 11a8.1 8.1 0 1 0 .5 4"/><polyline points="20 4 20 11 13 11"/></svg>
                <div><strong>Restart LOKAL to apply the saved server mode.</strong><span>The server continues using the current active configuration until restart.</span></div>
            </div>` : ''}
        <section class="server-status-grid">
            <article class="server-status-card server-status-primary">
                <div class="server-status-icon"><span class="status-dot"></span></div>
                <div><span>Active operating mode</span><strong>${escapeServerHTML(status.mode_label)}</strong></div>
            </article>
            <article class="server-status-card">
                <span>Teacher dashboard</span>
                <button type="button" class="server-url" onclick="copyServerURL('${escapeServerAttribute(status.teacher_url)}')">${escapeServerHTML(status.teacher_url)}</button>
            </article>
            <article class="server-status-card">
                <span>Student join address</span>
                <button type="button" class="server-url" onclick="copyServerURL('${escapeServerAttribute(status.student_url)}')">${escapeServerHTML(status.student_url)}</button>
            </article>
        </section>

        <form id="server-config-form" class="server-config-panel" onsubmit="saveServerConfiguration(event)">
            <div class="server-section-heading">
                <div><h3>Operating mode</h3><p>Changing modes saves the configuration and requires a LOKAL restart.</p></div>
            </div>
            <div class="server-mode-grid">
                ${serverModeCard('offline', 'Offline', 'This computer only', 'Use LOKAL without allowing phones or other computers to connect.', config.mode)}
                ${serverModeCard('lan', 'Local Network', 'Same Wi-Fi or LAN', 'Learners connect directly to this computer without Internet access.', config.mode, true)}
                ${serverModeCard('online', 'Online', 'Public HTTPS address', 'Learners connect through a configured public proxy, tunnel, or hosted endpoint.', config.mode)}
            </div>

            <div class="server-fields">
                <label class="server-field">
                    <span>Server port</span>
                    <input id="server-port" type="number" min="1" max="65535" value="${config.port}" required>
                    <small>Default: 8080. Windows Firewall may ask for access when this changes.</small>
                </label>
                <label class="server-field">
                    <span>Bind address</span>
                    <input id="server-bind" type="text" value="${escapeServerAttribute(config.bind_address)}" readonly>
                    <small>Selected automatically for the chosen operating mode.</small>
                </label>
                <label class="server-field server-public-field" id="server-public-field" ${config.mode === 'online' ? '' : 'hidden'}>
                    <span>Public server URL</span>
                    <input id="server-public-url" type="url" value="${escapeServerAttribute(config.public_url || '')}" placeholder="https://class.your-domain.com">
                    <small>HTTPS is recommended. This must point to the LOKAL server through your reverse proxy or tunnel.</small>
                </label>
            </div>

            <div class="server-address-preview">
                <div><span>Detected LAN student URL</span><strong>${escapeServerHTML(lanURL)}</strong></div>
                <div class="server-privacy-note"><strong>Reachability and synchronization are separate.</strong><span>Records leave this computer only when a cloud sync URL and synchronization secret are configured.</span></div>
            </div>

            ${renderSyncStatus(syncStatus)}
            ${renderRelayStatus(relayStatus)}

            <div class="server-actions">
                <button class="btn btn-primary" id="save-server-button" type="submit">Save server configuration</button>
                <span id="server-save-note">Active listener: ${escapeServerHTML(status.listen_address)}</span>
            </div>
        </form>`;
}

function serverModeCard(mode, title, scope, description, active, recommended = false) {
    return `<button type="button" class="server-mode-card ${mode === active ? 'selected' : ''}" data-mode="${mode}" onclick="selectServerMode('${mode}')">
        <span class="server-mode-check"><span></span></span>
        <div class="server-mode-title"><strong>${title}</strong>${recommended ? '<em>Recommended</em>' : ''}</div>
        <span class="server-mode-scope">${scope}</span>
        <p>${description}</p>
    </button>`;
}

function selectServerMode(mode) {
    selectedServerMode = mode;
    document.querySelectorAll('.server-mode-card').forEach(card => card.classList.toggle('selected', card.dataset.mode === mode));
    const publicField = document.getElementById('server-public-field');
    publicField.hidden = mode !== 'online';
    const bind = document.getElementById('server-bind');
    bind.value = mode === 'offline' ? '127.0.0.1' : '0.0.0.0';
}

async function saveServerConfiguration(event) {
    event.preventDefault();
    const button = document.getElementById('save-server-button');
    const publicURL = document.getElementById('server-public-url').value.trim();
    if (selectedServerMode === 'online' && !publicURL) {
        showToast('Enter a public URL before enabling Online mode.', 'error');
        document.getElementById('server-public-url').focus();
        return;
    }
    button.disabled = true;
    button.textContent = 'Saving...';
    try {
        const result = await api.updateServerConfig({
            mode: selectedServerMode,
            bind_address: document.getElementById('server-bind').value,
            port: Number(document.getElementById('server-port').value),
            public_url: publicURL
        });
        showToast(result.restart_required ? 'Configuration saved. Restart LOKAL to apply it.' : 'Server configuration saved.');
        const [syncStatus, relayStatus] = await Promise.all([
            api.getSyncStatus().catch(() => null),
            api.getRelayStatus().catch(() => null)
        ]);
        drawServerConfiguration(result.config, result.status, syncStatus, relayStatus);
    } catch (error) {
        showToast(error.message, 'error');
        button.disabled = false;
        button.textContent = 'Save server configuration';
    }
}

function renderSyncStatus(status) {
    if (!status) {
        return `<section class="server-sync-panel"><div><span class="server-sync-kicker">LOCAL OUTBOX</span><h3>Synchronization status unavailable</h3><p>Restart the current LOKAL server build to enable outbox status reporting.</p></div></section>`;
    }
    const labels = {
        disabled: 'Not configured',
        syncing: 'Synchronizing',
        attention: 'Needs attention',
        pending: 'Waiting to sync',
        up_to_date: 'Up to date',
        cloud_receiver: 'Cloud receiver ready'
    };
    const state = labels[status.state] || status.state;
    const lastSuccess = status.last_success_at
        ? new Date(status.last_success_at).toLocaleString()
        : 'Not yet synchronized';
    const destination = status.cloud_url || (status.provider === 'postgres' ? 'Hosted PostgreSQL' : 'No destination configured');
    const canRun = status.provider === 'sqlite' && status.enabled;
    return `
        <section class="server-sync-panel server-sync-${escapeServerAttribute(status.state)}">
            <div class="server-sync-heading">
                <div>
                    <span class="server-sync-kicker">DURABLE LOCAL OUTBOX</span>
                    <h3>Cloud synchronization</h3>
                    <p>${status.enabled
                        ? `Destination: ${escapeServerHTML(destination)}`
                        : 'Configure LOKAL_CLOUD_SYNC_URL and LOKAL_SYNC_SECRET to upload queued local changes.'}</p>
                </div>
                <span class="server-sync-state"><i></i>${escapeServerHTML(state)}</span>
            </div>
            <div class="server-sync-metrics">
                <div><span>Queued</span><strong>${Number(status.outbox?.pending || 0)}</strong></div>
                <div><span>Retrying</span><strong>${Number(status.outbox?.failed || 0)}</strong></div>
                <div><span>Delivered</span><strong>${Number(status.outbox?.synced || 0)}</strong></div>
                <div><span>Last successful sync</span><strong>${escapeServerHTML(lastSuccess)}</strong></div>
            </div>
            ${status.last_error ? `<div class="server-sync-error">${escapeServerHTML(status.last_error)}</div>` : ''}
            ${canRun ? `<button type="button" id="run-sync-button" class="btn btn-secondary server-sync-button" onclick="runOutboxSync()">Sync now</button>` : ''}
        </section>`;
}

function renderRelayStatus(status) {
    if (!status) {
        return `<section class="server-sync-panel"><div><span class="server-sync-kicker">HYBRID WEBSOCKET RELAY</span><h3>Relay status unavailable</h3><p>Restart the current LOKAL server build to enable relay status reporting.</p></div></section>`;
    }
    const labels = {
        disabled: 'Not configured',
        connecting: 'Connecting',
        connected: 'Connected',
        hosting: 'Hosting relay',
        attention: 'Needs attention',
        stopped: 'Stopped'
    };
    const state = labels[status.state] || status.state;
    let description = 'Direct offline and LAN classroom connections remain active.';
    if (status.edge_enabled) {
        description = status.connected
            ? `This classroom server is connected to ${status.relay_url || 'the hosted LOKAL relay'}.`
            : 'The local classroom remains available while LOKAL reconnects to the hosted relay.';
    } else if (status.host_enabled) {
        description = 'This server accepts authenticated classroom edge connections and routes their real-time events.';
    }
    return `
        <section class="server-sync-panel server-sync-${escapeServerAttribute(status.state)}">
            <div class="server-sync-heading">
                <div>
                    <span class="server-sync-kicker">HYBRID WEBSOCKET RELAY</span>
                    <h3>Real-time online transport</h3>
                    <p>${escapeServerHTML(description)}</p>
                </div>
                <span class="server-sync-state"><i></i>${escapeServerHTML(state)}</span>
            </div>
            <div class="server-sync-metrics">
                <div><span>Local rooms</span><strong>${Number(status.registered_rooms || 0)}</strong></div>
                <div><span>Hosted edges</span><strong>${Number(status.connected_edges || 0)}</strong></div>
                <div><span>Queued</span><strong>${Number(status.queued || 0)}</strong></div>
                <div><span>Relayed events</span><strong>${Number(status.relayed_inbound || 0) + Number(status.relayed_outbound || 0)}</strong></div>
            </div>
            ${status.last_error ? `<div class="server-sync-error">${escapeServerHTML(status.last_error)}</div>` : ''}
        </section>`;
}

async function runOutboxSync() {
    const button = document.getElementById('run-sync-button');
    if (button) {
        button.disabled = true;
        button.textContent = 'Synchronizing...';
    }
    try {
        await api.runSync();
        showToast('Synchronization queued.');
        await new Promise(resolve => setTimeout(resolve, 700));
        const [data, syncStatus, relayStatus] = await Promise.all([
            api.getServerConfig(),
            api.getSyncStatus(),
            api.getRelayStatus().catch(() => null)
        ]);
        drawServerConfiguration(data.config, data.status, syncStatus, relayStatus);
    } catch (error) {
        showToast(error.message, 'error');
        if (button) {
            button.disabled = false;
            button.textContent = 'Sync now';
        }
    }
}

async function copyServerURL(value) {
    try {
        await navigator.clipboard.writeText(value);
        showToast('Address copied to clipboard.');
    } catch (_) {
        showToast(value, 'info');
    }
}

function escapeServerHTML(value) {
    const element = document.createElement('div');
    element.textContent = value == null ? '' : String(value);
    return element.innerHTML;
}

function escapeServerAttribute(value) {
    return escapeServerHTML(value).replace(/'/g, '&#39;').replace(/"/g, '&quot;');
}
