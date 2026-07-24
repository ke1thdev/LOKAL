// Classes Page — Matches ClassPoint exactly

async function renderClasses() {
    const content = document.getElementById('page-content');
    try {
        const classes = await api.getClasses();
        content.innerHTML = `
            <div class="page-subtitle">
                <p>View all your classes or add new ones.</p>
                <button class="btn btn-primary" onclick="openModal('create-class-modal')">Create new class</button>
            </div>
            ${classes.length === 0 ? `
                <div class="empty-state">
                    <div class="empty-icon">👥</div>
                    <h3>No classes yet</h3>
                    <p>Create your first class to get started with LOKAL!</p>
                    <button class="btn btn-primary" onclick="openModal('create-class-modal')">Create new class</button>
                </div>
            ` : `
                <div class="classes-grid">
                    ${classes.map(c => `
                        <div class="card class-card" onclick="location.hash='#/classes/${c.id}'">
                            <div class="class-avatar" style="background-color: ${c.avatar_color}">
                                ${c.name.charAt(0).toUpperCase()}
                            </div>
                            <div class="class-info">
                                <h3>${escapeHtml(c.name)}</h3>
                                <div class="class-meta">${c.participant_count || 0} participant · ${c.group_count || 0} group</div>
                                <div class="class-code">Class code: ${c.code}</div>
                            </div>
                        </div>
                    `).join('')}
                </div>
            `}
        `;
    } catch (err) {
        content.innerHTML = `<div class="empty-state"><h3>Error loading classes</h3><p>${err.message}</p></div>`;
    }
}

// Class Detail — 5 tabs matching ClassPoint
async function renderClassDetail(classId, activeTab) {
    const content = document.getElementById('page-content');

    try {
        const classData = await api.getClass(classId);
        
        // Show class name and code in header
        document.getElementById('header-title').innerHTML = `
            <div style="display: flex; align-items: center; gap: 8px;">
                ${escapeHtml(classData.name)}
                <span class="class-code-badge" style="background: var(--primary);">${classData.code}</span>
            </div>
        `;
        
        // Ensure back button is shown if not already
        const backBtn = document.getElementById('back-btn');
        if (backBtn) backBtn.style.display = 'flex';
        
        // Set context for adding participant
        window.currentClassId = classId;

        const tabs = ['participants', 'groups', 'reports', 'leaderboard', 'settings'];

        content.innerHTML = `
            <div class="tabs">
                ${tabs.map(tab => `
                    <button class="tab ${activeTab === tab ? 'active' : ''}"
                        onclick="location.hash='#/classes/${classId}/${tab}'">
                        ${tab.charAt(0).toUpperCase() + tab.slice(1)}
                    </button>
                `).join('')}
            </div>
            <div id="class-tab-content"></div>
        `;

        // Render active tab content
        const tabContent = document.getElementById('class-tab-content');
        switch (activeTab) {
            case 'participants': await renderParticipantsTab(tabContent, classId); break;
            case 'groups': await renderGroupsTab(tabContent, classId); break;
            case 'reports': await renderClassReportsTab(tabContent, classId); break;
            case 'leaderboard': await renderLeaderboardTab(tabContent, classId); break;
            case 'settings': renderClassSettingsTab(tabContent, classData); break;
        }
    } catch (err) {
        content.innerHTML = `<div class="empty-state"><h3>Error</h3><p>${err.message}</p></div>`;
    }
}

window.participantSortCol = window.participantSortCol || 'name';
window.participantSortDir = window.participantSortDir || 'asc';

async function renderParticipantsTab(container, classId) {
    try {
        const [participantData, groups] = await Promise.all([
            api.getParticipants(classId),
            api.getGroups(classId)
        ]);
        let participants = participantData;

        // Sorting logic
        participants.sort((a, b) => {
            let valA = a[window.participantSortCol];
            let valB = b[window.participantSortCol];
            
            if (window.participantSortCol === 'stars') {
                valA = a.total_stars; valB = b.total_stars;
            } else if (window.participantSortCol === 'group') {
                valA = a.group_name || "Ungrouped"; valB = b.group_name || "Ungrouped";
            } else if (window.participantSortCol === 'name') {
                valA = valA.toLowerCase(); valB = valB.toLowerCase();
            }

            if (valA < valB) return window.participantSortDir === 'asc' ? -1 : 1;
            if (valA > valB) return window.participantSortDir === 'asc' ? 1 : -1;
            return 0;
        });

        let headerHtml = `
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px;">
                <div style="font-weight: 600; font-size: 16px; color: var(--text-primary);">
                    There are ${participants.length} participant${participants.length !== 1 ? 's' : ''} in your class.
                </div>
                <button class="btn btn-primary" onclick="openAddParticipantModal()" style="background-color: var(--primary); border-radius: 6px; padding: 10px 20px;">
                    Add participants
                </button>
            </div>
        `;

        if (participants.length === 0) {
            container.innerHTML = headerHtml + `
                <div class="empty-state">
                    <div class="empty-icon">👫</div>
                    <h3>No participants yet</h3>
                    <p>Add your students to get started!</p>
                </div>
            `;
        } else {
            let getLevelHtml = (level) => {
                if (window.getLevelIconHtml) {
                    return window.getLevelIconHtml(level, 36);
                }
                if (typeof levelIcons !== 'undefined' && level > 0 && level <= levelIcons.length) {
                    let svg = levelIcons[level - 1];
                    svg = svg.replace('<svg ', `<svg style="width:36px; height:36px;" `);
                    return `<div style="display:inline-flex; align-items:center; justify-content:center;" title="Level ${level}">${svg}</div>`;
                }
                return `<span style="font-weight:bold;">${level}</span>`;
            };
            
            if (!window.getLevelIconHtml) {
                window.getLevelIconHtml = function(l, size) {
                    if (typeof levelIcons !== 'undefined' && l > 0 && l <= levelIcons.length) {
                        let svg = levelIcons[l - 1];
                        svg = svg.replace('<svg ', `<svg style="width:${size}px; height:${size}px;" `);
                        return `<div style="display:inline-flex; align-items:center; justify-content:center;" title="Level ${l}">${svg}</div>`;
                    }
                    return `<span style="font-weight:bold;">${l}</span>`;
                };
            }

            const starSvg = `<svg class="participant-star-icon" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5" aria-hidden="true"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>`;
            const getSortIcon = (col) => {
                const dir = window.participantSortDir;
                if (window.participantSortCol !== col) {
                    return `<svg class="sort-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="opacity:0.3"><polyline points="6 9 12 15 18 9"/></svg>`;
                }
                if (dir === 'asc') return `<svg class="sort-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="18 15 12 9 6 15"/></svg>`;
                return `<svg class="sort-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"/></svg>`;
            };

            container.innerHTML = headerHtml + `
                <div class="cp-table-container">
                    <div class="cp-table-header" style="display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 40px;">
                        <div class="participants-col sortable-col" onclick="window.sortParticipants('name')">Name ${getSortIcon('name')}</div>
                        <div class="participants-col sortable-col" onclick="window.sortParticipants('group')">Group ${getSortIcon('group')}</div>
                        <div class="participants-col sortable-col" onclick="window.sortParticipants('stars')">Stars ${getSortIcon('stars')}</div>
                        <div class="participants-col sortable-col" onclick="window.sortParticipants('level')">Level ${getSortIcon('level')}</div>
                        <div></div>
                    </div>
                    ${participants.map(p => `
                        <div class="cp-table-row" style="display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 40px; position: relative;">
                            <div class="participants-col" style="gap: 12px;">
                                <div class="participant-avatar" style="width: 32px; height: 32px; font-size: 14px; border-radius: 50%; background: #a0aec0; color: white; display: flex; align-items: center; justify-content: center; font-weight: bold;">
                                    ${p.name.charAt(0).toUpperCase()}
                                </div>
                                <span style="font-weight: 600; text-transform: uppercase;">${escapeHtml(p.name)}</span>
                            </div>
                            <div class="participants-col">
                                <select class="participant-group-select" aria-label="Group for ${escapeHtml(p.name)}"
                                    onchange="window.assignParticipantGroup(${p.id}, this.value)"
                                    style="${p.group_color ? `border-color:${p.group_color};` : ''}">
                                    <option value="0" ${!p.group_id ? 'selected' : ''}>Ungrouped</option>
                                    ${groups.map(g => `<option value="${g.id}" ${p.group_id === g.id ? 'selected' : ''}>${escapeHtml(g.name)}</option>`).join('')}
                                </select>
                            </div>
                            <div class="participants-col" style="color: #27272A; font-weight: 600; font-size: 16px;">
                                <div class="star-display-only" id="star-display-${p.id}">
                                    ${starSvg} ${p.total_stars}
                                </div>
                                <div class="star-controls" id="star-controls-${p.id}">
                                    <button class="star-btn minus" onclick="event.stopPropagation(); window.adjustParticipantStars(${p.id}, -1)">-</button>
                                    <span style="display: flex; gap: 6px; align-items: center; min-width: 30px; justify-content: center;">${starSvg} ${p.total_stars}</span>
                                    <button class="star-btn plus" onclick="event.stopPropagation(); window.adjustParticipantStars(${p.id}, 1)">+</button>
                                </div>
                            </div>
                            <div class="participants-col" id="level-display-${p.id}">
                                ${getLevelHtml(p.level)}
                            </div>
                            <div class="participants-col" style="justify-content: flex-end;">
                                <div class="dropdown-container">
                                    <button class="participant-menu-btn" onclick="event.stopPropagation(); window.toggleParticipantMenu(${p.id})">⋮</button>
                                    <div class="dropdown-menu" id="participant-menu-${p.id}">
                                        <button class="dropdown-item" onclick="window.editParticipantPrompt(${p.id}, '${escapeHtml(p.name).replace(/'/g, "\\'")}', ${p.total_stars}, '${p.avatar_url || ''}')">
                                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-right: 8px; vertical-align: middle; margin-top: -2px;"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path></svg> Edit participant
                                        </button>
                                        <button class="dropdown-item text-danger" onclick="window.deleteParticipantPrompt(${p.id})">
                                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-right: 8px; vertical-align: middle; margin-top: -2px;"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg> Delete participant
                                        </button>
                                    </div>
                                </div>
                            </div>
                        </div>
                    `).join('')}
                </div>
                <div style="text-align: center; margin-top: 16px;">
                    <button class="btn btn-outline" style="border-radius: 6px; display: inline-flex; align-items: center; gap: 8px; font-weight: 600;" onclick="downloadParticipantsCSV(${classId})">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                        Download Participants (csv)
                    </button>
                </div>
            `;

            // Click outside to close menus
            document.addEventListener('click', function closeMenus(e) {
                if (!e.target.closest('.dropdown-container')) {
                    document.querySelectorAll('.participant-menu-btn + .dropdown-menu').forEach(m => m.classList.remove('show'));
                }
            });
        }
    } catch (err) {
        container.innerHTML = `<p>Error: ${err.message}</p>`;
    }
}

window.sortParticipants = function(col) {
    if (window.participantSortCol === col) {
        window.participantSortDir = window.participantSortDir === 'asc' ? 'desc' : 'asc';
    } else {
        window.participantSortCol = col;
        window.participantSortDir = 'asc';
    }
    const tabContent = document.getElementById('class-tab-content');
    if (tabContent && window.currentClassId) {
        renderParticipantsTab(tabContent, window.currentClassId);
    }
};

window.toggleParticipantMenu = function(id) {
    document.querySelectorAll('.participant-menu-btn + .dropdown-menu').forEach(m => {
        if (m.id !== `participant-menu-${id}`) m.classList.remove('show');
    });
    document.getElementById(`participant-menu-${id}`).classList.toggle('show');
};

window.editParticipantPrompt = function(id, name, stars, avatarUrl) {
    document.getElementById('edit-participant-id').value = id;
    document.getElementById('edit-participant-name').value = name;
    document.getElementById('edit-participant-stars').value = stars || 0;
    document.getElementById('edit-participant-stars-val').textContent = stars || 0;
    document.getElementById('edit-participant-avatar-url').value = avatarUrl || '';
    
    const letter = document.getElementById('edit-participant-avatar-letter');
    const img = document.getElementById('edit-participant-avatar-img');
    
    if (avatarUrl) {
        img.src = avatarUrl;
        img.style.display = 'block';
        letter.style.display = 'none';
    } else {
        img.style.display = 'none';
        letter.style.display = 'block';
        letter.textContent = name ? name.charAt(0).toUpperCase() : '?';
    }

    openModal('edit-participant-modal');
    document.querySelectorAll('.dropdown-menu').forEach(m => m.classList.remove('show'));
};

window.adjustEditModalStars = function(amount) {
    const input = document.getElementById('edit-participant-stars');
    const valSpan = document.getElementById('edit-participant-stars-val');
    let current = parseInt(input.value) || 0;
    current += amount;
    if (current < 0) current = 0;
    input.value = current;
    valSpan.textContent = current;
};

document.getElementById('edit-participant-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    const id = document.getElementById('edit-participant-id').value;
    const name = document.getElementById('edit-participant-name').value;
    const stars = parseInt(document.getElementById('edit-participant-stars').value) || 0;
    const avatarUrl = document.getElementById('edit-participant-avatar-url').value;
    
    try {
        await api.updateParticipant(window.currentClassId, id, name, stars, avatarUrl);
        closeModal('edit-participant-modal');
        if(window.showToast) showToast('Participant updated successfully!');
        const tabContent = document.getElementById('class-tab-content');
        if (tabContent && window.currentClassId) {
            renderParticipantsTab(tabContent, window.currentClassId);
        }
    } catch (err) {
        if(window.showToast) showToast(err.message, 'error');
    }
});

window.deleteParticipantPrompt = async function(id) {
    document.querySelectorAll('.dropdown-menu').forEach(m => m.classList.remove('show'));
    if (window.showConfirmModal) {
        if (await window.showConfirm('Delete Participant', 'Are you sure you want to delete this participant?', 'Delete', true)) {
            executeDelete(id);
        }
    } else if (confirm("Are you sure you want to delete this participant?")) {
        executeDelete(id);
    }
    
    async function executeDelete(participantId) {
        try {
            await api.deleteParticipant(window.currentClassId, participantId);
            if(window.showToast) showToast('Participant deleted');
            const tabContent = document.getElementById('class-tab-content');
            if (tabContent && window.currentClassId) {
                renderParticipantsTab(tabContent, window.currentClassId);
            }
        } catch (err) {
            if(window.showToast) showToast(err.message, 'error');
        }
    }
};

window.pendingStarAnimations = window.pendingStarAnimations || {};

window.adjustParticipantStars = async function(participantId, amount) {
    if (!window.pendingStarAnimations[participantId]) {
        window.pendingStarAnimations[participantId] = { amount: 0, timeout: null };
    }
    
    const animState = window.pendingStarAnimations[participantId];
    animState.amount += amount;

    if (animState.timeout) {
        clearTimeout(animState.timeout);
    }

    try {
        const classId = window.currentClassId;
        const res = await api.adjustParticipantStars(classId, participantId, amount);
        
        // Optimistically update the UI
        const starDisplay = document.getElementById(`star-display-${participantId}`);
        const starControls = document.getElementById(`star-controls-${participantId}`);
        const levelDisplay = document.getElementById(`level-display-${participantId}`);
        
        const starSvg = `<svg class="participant-star-icon" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5" aria-hidden="true"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>`;
        
        const displayAmount = animState.amount;
        const animText = displayAmount > 0 ? `<span class="star-anim-text" style="color: #0D9488;">( +${displayAmount} )</span>` : (displayAmount < 0 ? `<span class="star-anim-text" style="color: #E03530;">( ${displayAmount} )</span>` : "");

        if (starDisplay) {
            starDisplay.innerHTML = `${starSvg} ${res.total_stars}`;
        }
        if (starControls) {
            starControls.innerHTML = `
                <button class="star-btn minus" onclick="event.stopPropagation(); window.adjustParticipantStars(${participantId}, -1)">-</button>
                <span style="display: flex; gap: 6px; align-items: center; min-width: 30px; justify-content: center;">${starSvg} ${res.total_stars} ${animText}</span>
                <button class="star-btn plus" onclick="event.stopPropagation(); window.adjustParticipantStars(${participantId}, 1)">+</button>
            `;
            
            // Remove the animation text after 1.5 seconds
            animState.timeout = setTimeout(() => {
                animState.amount = 0; // reset
                const updatedControls = document.getElementById(`star-controls-${participantId}`);
                if (updatedControls) {
                    updatedControls.innerHTML = `
                        <button class="star-btn minus" onclick="event.stopPropagation(); window.adjustParticipantStars(${participantId}, -1)">-</button>
                        <span style="display: flex; gap: 6px; align-items: center; min-width: 30px; justify-content: center;">${starSvg} ${res.total_stars}</span>
                        <button class="star-btn plus" onclick="event.stopPropagation(); window.adjustParticipantStars(${participantId}, 1)">+</button>
                    `;
                }
            }, 1500);
        }
        if (levelDisplay && window.getLevelIconHtml) {
            levelDisplay.innerHTML = window.getLevelIconHtml(res.level, 36);
        }
    } catch (err) {
        if(window.showToast) {
            showToast(err.message, 'error');
        } else {
            alert(err.message);
        }
    }
};

window.openAddParticipantModal = function() {
    document.getElementById('participant-names-input').value = '';
    openModal('add-participant-modal');
};

window.submitAddParticipants = async function() {
    const input = document.getElementById('participant-names-input').value;
    const names = input.split('\n').map(n => n.trim()).filter(n => n);
    
    if (names.length === 0) {
        showToast('Please enter at least one name', 'error');
        return;
    }
    
    const classId = window.currentClassId;
    if (!classId) return;

    try {
        let addedCount = 0;
        for (const name of names) {
            await api.addParticipant(classId, name);
            addedCount++;
        }
        showToast(`Successfully added ${addedCount} participant${addedCount !== 1 ? 's' : ''}!`);
        closeModal('add-participant-modal');
        handleRoute(); // Refresh the page to show new participants
    } catch (err) {
        showToast(err.message, 'error');
    }
};

window.downloadParticipantsCSV = async function(classId) {
    try {
        const participants = await api.getParticipants(classId);
        let csv = 'Name,Group,Stars,Level\n';
        participants.forEach(p => {
            csv += `"${p.name}","${p.group_name || 'Ungrouped'}",${p.total_stars},${p.level}\n`;
        });
        const blob = new Blob([csv], { type: 'text/csv' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `participants_class_${classId}.csv`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    } catch(err) {
        if(window.showAlertModal) {
            showAlertModal('Download Failed', "Failed to download: " + err.message);
        } else {
            showToast("Failed to download: " + err.message, 'error');
        }
    }
}

async function renderGroupsTab(container, classId) {
    try {
        const [groups, participants] = await Promise.all([api.getGroups(classId), api.getParticipants(classId)]);
        const memberChip = p => `<span class="group-member-chip"><span class="member-initial">${escapeHtml(p.name.charAt(0).toUpperCase())}</span>${escapeHtml(p.name)}</span>`;
        const cards = groups.map(group => {
            const members = participants.filter(p => p.group_id === group.id);
            return `<article class="group-card" style="--group-color:${group.color}">
                <div class="group-card-accent"></div>
                <div class="group-card-header">
                    <div><h3>${escapeHtml(group.name)}</h3><p>${members.length} member${members.length === 1 ? '' : 's'}</p></div>
                    <div class="group-card-actions">
                        <button class="icon-action" title="Edit group" onclick="window.editGroup(${group.id}, '${escapeHtml(group.name).replace(/'/g, "\\'")}', '${group.color}')">✎</button>
                        <button class="icon-action danger" title="Delete group" onclick="window.removeGroup(${group.id}, '${escapeHtml(group.name).replace(/'/g, "\\'")}')">×</button>
                    </div>
                </div>
                <div class="group-members">${members.length ? members.map(memberChip).join('') : '<span class="group-empty">Assign students from the Participants tab.</span>'}</div>
            </article>`;
        }).join('');
        const ungrouped = participants.filter(p => !p.group_id);
        container.innerHTML = `
            <div class="groups-toolbar">
                <div><h2>Groups</h2><p>Organize participants for easier class management.</p></div>
                <button class="btn btn-primary" onclick="window.openCreateGroup()">Create group</button>
            </div>
            <div class="groups-grid">
                ${cards}
                <article class="group-card ungrouped-card">
                    <div class="group-card-header"><div><h3>Ungrouped</h3><p>${ungrouped.length} member${ungrouped.length === 1 ? '' : 's'}</p></div></div>
                    <div class="group-members">${ungrouped.length ? ungrouped.map(memberChip).join('') : '<span class="group-empty">Everyone is assigned to a group.</span>'}</div>
                </article>
            </div>
            ${groups.length === 0 ? '<div class="groups-first-hint">Create your first group, then assign participants from the Participants tab.</div>' : ''}`;
    } catch (err) {
        container.innerHTML = `<div class="empty-state"><h3>Unable to load groups</h3><p>${escapeHtml(err.message)}</p></div>`;
    }
}

window.openCreateGroup = async function() {
    const colors = ['#0B1F1C', '#334155', '#12B981', '#F59E0B', '#EC4899', '#0EA5E9'];
    const currentCount = (await api.getGroups(window.currentClassId)).length;
    document.getElementById('group-modal-title').textContent = 'Create group';
    document.getElementById('group-id-input').value = '';
    document.getElementById('group-name-input').value = '';
    window.selectGroupColor(colors[currentCount % colors.length]);
    openModal('group-form-modal');
    setTimeout(() => document.getElementById('group-name-input').focus(), 0);
};
window.editGroup = async function(groupId, currentName, color) {
    document.getElementById('group-modal-title').textContent = 'Edit group';
    document.getElementById('group-id-input').value = groupId;
    document.getElementById('group-name-input').value = currentName;
    window.selectGroupColor(color || '#0B1F1C');
    openModal('group-form-modal');
    setTimeout(() => document.getElementById('group-name-input').focus(), 0);
};
window.selectGroupColor = function(color) {
    document.getElementById('group-color-input').value = color;
    document.querySelectorAll('.group-color-choice').forEach(button => {
        button.classList.toggle('selected', button.dataset.color.toLowerCase() === color.toLowerCase());
    });
};
window.saveGroup = async function(event) {
    event.preventDefault();
    const groupId = document.getElementById('group-id-input').value;
    const name = document.getElementById('group-name-input').value.trim();
    const color = document.getElementById('group-color-input').value;
    if (!name) return;
    const saveButton = document.getElementById('group-save-button');
    saveButton.disabled = true;
    try {
        if (groupId) {
            await api.updateGroup(window.currentClassId, groupId, name, color);
        } else {
            await api.createGroup(window.currentClassId, name, color);
        }
        closeModal('group-form-modal');
        if (window.showToast) showToast(groupId ? 'Group updated' : 'Group created');
        await renderGroupsTab(document.getElementById('class-tab-content'), window.currentClassId);
    } catch (err) {
        if (window.showToast) showToast(err.message, 'error');
    } finally {
        saveButton.disabled = false;
    }
};
window.removeGroup = async function(groupId, groupName) {
    const confirmed = window.showConfirm
        ? await window.showConfirm('Delete group', `Delete “${groupName}”? Its members will become ungrouped.`, 'Delete', true)
        : confirm(`Delete “${groupName}”?`);
    if (!confirmed) return;
    await api.deleteGroup(window.currentClassId, groupId);
    renderGroupsTab(document.getElementById('class-tab-content'), window.currentClassId);
};
window.assignParticipantGroup = async function(participantId, groupId) {
    try {
        await api.setParticipantGroup(window.currentClassId, participantId, groupId);
        if (window.showToast) showToast(groupId === '0' ? 'Participant is now ungrouped' : 'Group updated');
    } catch (err) {
        if (window.showToast) showToast(err.message, 'error');
        renderParticipantsTab(document.getElementById('class-tab-content'), window.currentClassId);
    }
};

window._currentClassReportsClassId = null;
window.classReportsLoadedCount = 10;

window.loadMoreClassReports = function(classId) {
    window.classReportsLoadedCount += 10;
    renderClassReportsTab(document.getElementById('class-tab-content'), classId);
};

// Reports Tab (inside class)
async function renderClassReportsTab(container, classId) {
    try {
        if (window._currentClassReportsClassId !== classId) {
            window.classReportsLoadedCount = 10;
            window._currentClassReportsClassId = classId;
        }

        const allReports = await api.getClassReports(classId);
        const reports = allReports.slice(0, window.classReportsLoadedCount);

        if (allReports.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon gradient-icon">📅</div>
                    <h3>No reports yet</h3>
                    <p>There's no reports yet for this class. After you teach the class with LOKAL, the reports will appear here!</p>
                </div>
            `;
        } else {
            window._currentReports = allReports;
            window._currentClassReports = allReports;
            window.currentReportsFlat = allReports;

            const groups = {};
            reports.forEach((r, idx) => {
                const date = new Date(r.session_date);
                const monthYear = date.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
                if (!groups[monthYear]) groups[monthYear] = [];
                groups[monthYear].push({ ...r, _idx: idx });
            });

            let html = '<div class="reports-container" style="max-width: 1000px; margin: 0 auto; padding-top: 24px;">';

            for (const [month, monthReports] of Object.entries(groups)) {
                html += `
                    <div class="report-month-wrapper" style="text-align: center; margin-bottom: 24px;">
                        <span class="report-month-badge" style="background: var(--bg-light); padding: 4px 16px; border-radius: var(--radius-full); font-size: 12px; font-weight: 500; color: var(--text-secondary);">${month}</span>
                    </div>
                    <div class="report-table-wrapper" style="background: white; border-radius: var(--radius-md); border: 1px solid var(--border); overflow: hidden; margin-bottom: 32px; overflow-x: auto;">
                        <div class="report-table-inner" style="min-width: 800px;">
                            <div class="report-table-header" style="display: grid; grid-template-columns: 2fr 1fr 1fr 2fr 40px; padding: 16px 24px; background: #f9fafb; border-bottom: 1px solid var(--border); font-weight: 600; color: var(--text-secondary); font-size: 14px;">
                                <div>Class time</div>
                                <div>Activities</div>
                                <div>Stars awarded</div>
                                <div>Top player(s)</div>
                                <div></div>
                            </div>
                `;

                monthReports.forEach(r => {
                    const time = new Date(r.session_date).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
                    const day = new Date(r.session_date).toLocaleDateString('en-US', { day: 'numeric', month: 'short' });
                    
                    let topPlayersHtml = '';
                    if (r.top_players) {
                        const players = r.top_players.split(',').map(p => p.trim()).filter(p => p);
                        if (players.length > 0) {
                            const firstPlayer = players[0];
                            const initial = firstPlayer.charAt(0).toUpperCase();
                            topPlayersHtml = `
                                <div class="report-col" style="display: flex; align-items: center; gap: 8px;">
                                    <div class="top-player-avatar" style="width: 24px; height: 24px; border-radius: 50%; background: #a0aec0; color: white; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: bold;">${initial}</div>
                                    <span>${escapeHtml(firstPlayer)} ${players.length > 1 ? '+ ' + (players.length - 1) : ''}</span>
                                </div>
                            `;
                        }
                    }

                    let newBadgeHtml = '';
                    let favIconHtml = '';

                    if (r.is_favorite) {
                        favIconHtml = `<span style="color: #ef4444; margin-left: 4px; font-size: 12px;">❤️</span>`;
                    }

                    const isNew = (Date.now() - new Date(r.session_date).getTime()) < 24 * 60 * 60 * 1000;
                    if (isNew) {
                        newBadgeHtml = `<span style="color: var(--primary); font-size: 11px; margin-left: 8px; font-weight: 600;">New</span>`;
                    }

                    html += `
                        <div class="report-row" onclick="renderReportDetails(${r._idx})" style="display: grid; grid-template-columns: 2fr 1fr 1fr 2fr 40px; padding: 16px 24px; border-bottom: 1px solid var(--border); align-items: center; cursor: pointer; transition: background 0.2s;" onmouseover="this.style.background='#f3f4f6'" onmouseout="this.style.background='transparent'">
                            <div class="report-col time" style="display: flex; align-items: center; gap: 8px; font-size: 14px;">
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="opacity: 0.5"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>
                                ${day}, ${time} ${newBadgeHtml} ${favIconHtml}
                            </div>
                            <div class="report-col" style="color: var(--success); font-weight: 500; display: flex; align-items: center; gap: 8px; font-size: 14px;">
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16c0 1.1.9 2 2 2h12a2 2 0 0 0 2-2V8l-6-6z"/><path d="M14 3v5h5M16 13H8M16 17H8M10 9H8"/></svg>
                                ${r.activities_count}
                            </div>
                            <div class="report-col stars" style="color: var(--warning); font-weight: 500; display: flex; align-items: center; gap: 6px; font-size: 16px;">
                                <svg width="20" height="20" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>
                                ${r.stars_awarded || 0}
                            </div>
                            <div class="report-col" style="font-size: 14px;">
                                ${topPlayersHtml}
                            </div>
                            <div class="report-col dropdown-container" onclick="event.stopPropagation()" style="display: flex; justify-content: flex-end;">
                                <button class="participant-menu-btn" style="background: none; border: none; font-size: 20px; font-weight: bold; cursor: pointer; color: var(--text-secondary); padding: 4px 8px; border-radius: 4px;" onclick="window.toggleReportMenu(${r.session_id})">⋮</button>
                                <div class="dropdown-menu" id="report-menu-${r.session_id}">
                                    <button class="dropdown-item" onclick="window.handleFavoriteReport(${r.session_id})">
                                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-right: 8px; vertical-align: middle; margin-top: -2px;"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path><line x1="3" y1="3" x2="21" y2="21"></line></svg> ${r.is_favorite ? 'Remove from favorite' : 'Add to favorite'}
                                    </button>
                                    <button class="dropdown-item text-danger" onclick="window.handleDeleteReport(${r.session_id})">
                                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="margin-right: 8px; vertical-align: middle; margin-top: -2px;"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg> Delete report
                                    </button>
                                </div>
                            </div>
                        </div>
                    `;
                });
                html += `</div></div>`;
            }

            if (window.classReportsLoadedCount < allReports.length) {
                html += `
                    <div style="text-align: center; margin-top: 32px; margin-bottom: 32px;">
                        <button class="btn btn-secondary" onclick="window.loadMoreClassReports(${classId})" style="background: white; border: 1px solid var(--border); padding: 8px 24px; border-radius: var(--radius-md); font-weight: 500; cursor: pointer;">Load More</button>
                    </div>
                `;
            }

            container.innerHTML = html;
        }
    } catch (err) {
        container.innerHTML = `<p>Error: ${err.message}</p>`;
    }
}

// Leaderboard Tab
async function renderLeaderboardTab(container, classId) {
    try {
        const participants = await api.getLeaderboard(classId);
        if (participants.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">🏆</div>
                    <h3>No leaderboard yet</h3>
                    <p>There's no leaderboard yet for this class. After you add participants and award stars, the leaderboard will appear here!</p>
                </div>
            `;
        } else {
            container.innerHTML = `
                <div style="max-width: 800px; padding-top: 24px;">
                    <h3 style="margin-bottom: 24px; font-size: 16px;">Class Leaderboard</h3>
                    <div class="leaderboard-list">
                        ${(() => {
                            const maxStars = Math.max(...participants.map(p => p.total_stars), 1);
                            return participants.map((p, i) => {
                                let bgColor = '#F3F4F6';
                                if (i === 0) bgColor = '#FEF3C7'; // Rank 1 Gold
                                else if (i === 1) bgColor = '#E5E7EB'; // Rank 2 Silver
                                else if (i === 2) bgColor = '#FFEDD5'; // Rank 3 Bronze
                                
                                const widthPercent = Math.max(30, (p.total_stars / maxStars) * 100);
                                
                                return `
                                    <div style="background-color: ${bgColor}; width: ${widthPercent}%; min-width: 250px; display: flex; align-items: center; padding: 12px 32px 12px 16px; margin-bottom: 8px; clip-path: polygon(0 0, calc(100% - 24px) 0, 100% 50%, calc(100% - 24px) 100%, 0 100%); transition: width 0.5s ease-out;">
                                        <div style="width: 40px; display: flex; justify-content: center; align-items: center;">
                                            ${(() => {
                                                if (typeof levelIcons !== 'undefined' && p.level > 0 && p.level <= levelIcons.length) {
                                                    return `<div style="display:inline-flex; align-items:center; justify-content:center;" title="Level ${p.level}">${levelIcons[p.level - 1].replace('<svg ', '<svg style="width:36px; height:36px;" ')}</div>`;
                                                }
                                                return `<span style="font-weight:bold;">${p.level}</span>`;
                                            })()}
                                        </div>
                                        <div class="participant-avatar" style="margin: 0 16px; width: 32px; height: 32px; font-size: 14px; border-radius: 50%; background: #a0aec0; color: white; display: flex; align-items: center; justify-content: center; font-weight: bold; flex-shrink: 0;">${p.name.charAt(0).toUpperCase()}</div>
                                        <div style="font-weight: 500; text-transform: uppercase; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-size: 16px;">${escapeHtml(p.name)}</div>
                                        <div style="margin-left: auto; color: #d69e2e; font-weight: bold; display: flex; align-items: center; gap: 6px; flex-shrink: 0; font-size: 18px;">
                                            <svg width="24" height="24" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>
                                            ${p.total_stars}
                                        </div>
                                    </div>
                                `;
                            }).join('');
                        })()}
                    </div>
                    <div style="margin-top: 24px;">
                        <button class="btn btn-secondary" style="display: flex; align-items: center; gap: 8px; background: white; border: 1px solid var(--border);" onclick="downloadLeaderboardCSV(${classId})">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
                            Download Leaderboard (csv)
                        </button>
                    </div>
                </div>
            `;
        }
    } catch (err) {
        container.innerHTML = `<p>Error: ${err.message}</p>`;
    }
}

window.downloadLeaderboardCSV = async function(classId) {
    try {
        const participants = await api.getLeaderboard(classId);
        let csv = 'Rank,Name,Stars\n';
        participants.forEach((p, i) => {
            csv += `${i + 1},"${p.name}",${p.total_stars}\n`;
        });
        const blob = new Blob([csv], { type: 'text/csv' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `leaderboard_class_${classId}.csv`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    } catch(err) {
        showAlertModal('Download Failed', "Failed to download: " + err.message);
    }
}

// Settings Tab (inside class)
function renderClassSettingsTab(container, classData) {
    container.innerHTML = `
        <div class="settings-section">
            <h3>Edit class</h3>
            <p>Change your class name, code or avatar here.</p>
            <button class="btn btn-primary" onclick='openEditClassModal(${JSON.stringify(classData)})'>Edit class</button>
        </div>
        <div class="settings-section">
            <h3>Reset stars</h3>
            <p>All participants' stars will be reset to 0 for a fresh start.</p>
            <button class="btn btn-danger" onclick="confirmResetStars(${classData.id})">Reset stars</button>
        </div>
        <div class="settings-section">
            <h3>Delete class</h3>
            <p>The class will be deleted, including participants and their stars. This cannot be undone.</p>
            <button class="btn btn-danger" onclick="confirmDeleteClass(${classData.id})">Delete class</button>
        </div>
    `;
}

async function confirmResetStars(classId) {
    if (await showConfirm('Reset Stars', 'Reset all stars to 0? This cannot be undone.', 'Reset', true)) {
        try {
            await api.resetStars(classId);
            showToast('Stars reset successfully!');
            handleRoute();
        } catch (err) {
            showToast(err.message, 'error');
        }
    }
}

// HTML escape
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
