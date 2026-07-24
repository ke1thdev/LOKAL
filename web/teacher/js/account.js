// Account Page

async function renderAccount() {
    const content = document.getElementById('page-content');

    try {
        const [teacher, devices] = await Promise.all([
            api.getProfile(),
            api.getRegisteredDevices()
        ]);
        const initial = (teacher.display_name || teacher.username || 'T').charAt(0).toUpperCase();
        const currentDeviceUID = getTeacherDeviceRegistration().id;

        content.innerHTML = `
            <div class="account-layout">
                <div class="profile-card">
                    <div class="profile-header">
                        <div class="profile-avatar">${teacher.avatar_url ? `<img src="${escapeHtml(teacher.avatar_url)}" alt="">` : initial}</div>
                        <div class="profile-title">
                            <h2>${escapeHtml(teacher.display_name || teacher.username)}</h2>
                            <p>@${escapeHtml(teacher.username)}${teacher.email ? ' · ' + escapeHtml(teacher.email) : ''}</p>
                        </div>
                    </div>

                    <form class="profile-edit-form" id="profile-edit-form">
                        <label>Display name<input name="display_name" value="${escapeAttribute(teacher.display_name || '')}" required maxlength="80"></label>
                        <label>Username<input value="${escapeAttribute(teacher.username)}" disabled></label>
                        <label>Email address<input name="email" type="email" value="${escapeAttribute(teacher.email || '')}"></label>
                        <label>Organization<input name="organization" value="${escapeAttribute(teacher.organization || '')}" maxlength="100" placeholder="School or organization"></label>
                        <label>Profession<input name="profession" value="${escapeAttribute(teacher.profession || '')}" maxlength="100" placeholder="Teacher, instructor…"></label>
                        <label class="profile-wide">Avatar image URL<input name="avatar_url" value="${escapeAttribute(teacher.avatar_url || '')}" placeholder="https://…"></label>
                        <div class="profile-form-footer">
                            <span>Member since ${formatDate(teacher.created_at)}</span>
                            <button class="btn btn-primary" type="submit">Save profile</button>
                        </div>
                    </form>
                </div>

                <section class="devices-card" aria-labelledby="devices-heading">
                    <div class="devices-header">
                        <div>
                            <h2 id="devices-heading">Signed-in devices</h2>
                            <p>Review browsers and PowerPoint installations that can access your LOKAL account.</p>
                        </div>
                        <span class="devices-count">${devices.filter(device => device.active).length} active</span>
                    </div>
                    <div class="devices-list">
                        ${renderDeviceRows(devices, currentDeviceUID)}
                    </div>
                </section>
            </div>
        `;

        document.getElementById('profile-edit-form').addEventListener('submit', async (event) => {
            event.preventDefault();
            const button = event.currentTarget.querySelector('button[type="submit"]');
            button.disabled = true;
            button.textContent = 'Saving…';
            try {
                const form = new FormData(event.currentTarget);
                const updated = await api.updateProfile(Object.fromEntries(form.entries()));
                document.querySelector('.profile-title h2').textContent = updated.display_name;
                const userInitial = document.getElementById('user-initial');
                if (userInitial) userInitial.textContent = updated.display_name.charAt(0).toUpperCase();
                if (window.showToast) showToast('Profile updated');
            } catch (err) {
                if (window.showToast) showToast(err.message, 'error');
            } finally {
                button.disabled = false;
                button.textContent = 'Save profile';
            }
        });

        document.querySelector('.devices-list').addEventListener('click', async (event) => {
            const button = event.target.closest('[data-revoke-device]');
            if (!button) return;

            const deviceId = button.dataset.revokeDevice;
            const deviceName = button.dataset.deviceName || 'this device';
            if (!window.confirm(`Sign out ${deviceName}? It will need to sign in again.`)) return;

            button.disabled = true;
            button.textContent = 'Signing out…';
            try {
                await api.revokeRegisteredDevice(deviceId);
                const refreshedDevices = await api.getRegisteredDevices();
                document.querySelector('.devices-list').innerHTML = renderDeviceRows(refreshedDevices, currentDeviceUID);
                document.querySelector('.devices-count').textContent =
                    `${refreshedDevices.filter(device => device.active).length} active`;
                if (window.showToast) showToast(`${deviceName} signed out`);
            } catch (err) {
                button.disabled = false;
                button.textContent = 'Sign out';
                if (window.showToast) showToast(err.message, 'error');
            }
        });
    } catch (err) {
        content.innerHTML = `<div class="empty-state"><h3>Error</h3><p>${escapeHtml(err.message)}</p></div>`;
    }
}

function renderDeviceRows(devices, currentDeviceUID) {
    if (!devices.length) {
        return '<div class="devices-empty">No registered devices yet.</div>';
    }

    return devices.map((device) => {
        const isCurrent = device.device_uid === currentDeviceUID;
        const platform = device.platform || 'Unknown platform';
        const deviceName = device.name || platform;
        const status = device.active ? 'Active' : 'Signed out';
        return `
            <article class="device-row${device.active ? '' : ' is-revoked'}">
                <div class="device-icon" aria-hidden="true">
                    ${platform.toLowerCase().includes('powerpoint') || device.device_uid.startsWith('ppt-')
                        ? '<span>P</span>'
                        : '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="3" y="4" width="18" height="14" rx="2"/><path d="M8 21h8M12 18v3"/></svg>'}
                </div>
                <div class="device-details">
                    <div class="device-name">
                        ${escapeHtml(deviceName)}
                        ${isCurrent ? '<span class="current-device-badge">This device</span>' : ''}
                    </div>
                    <div class="device-meta">${escapeHtml(platform)} · Last active ${formatDeviceDate(device.last_seen_at)}</div>
                </div>
                <span class="device-status${device.active ? ' active' : ''}">${status}</span>
                ${device.active && !isCurrent
                    ? `<button class="device-revoke" type="button" data-revoke-device="${device.id}" data-device-name="${escapeAttribute(deviceName)}">Sign out</button>`
                    : ''}
            </article>
        `;
    }).join('');
}

function formatDeviceDate(value) {
    if (!value) return 'unknown';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return 'unknown';
    return new Intl.DateTimeFormat(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short'
    }).format(date);
}

function escapeAttribute(value) {
    return String(value)
        .replaceAll('&', '&amp;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;');
}
