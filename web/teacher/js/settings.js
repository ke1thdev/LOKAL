// Settings Page — Star Levels matching ClassPoint exactly

const levelColors = [
    '#9ca3af', '#60a5fa', '#34d399', '#0B1F1C', '#f97316',
    '#14b8a6', '#334155', '#3b82f6', '#22c55e', '#ef4444'
];

const levelBadgeNames = [
    'Beginner', 'Learner', 'Achiever', 'Scholar', 'Expert',
    'Master', 'Champion', 'Legend', 'Hero', 'Supreme'
];

async function renderSettings(activeTab = 'star-levels') {
    const content = document.getElementById('page-content');

    const tabs = [
        { id: 'star-levels', label: 'Star Levels' },
        { id: 'whiteboard', label: 'Whiteboard Backgrounds' },
        { id: 'notifications', label: 'Notifications' }
    ];

    content.innerHTML = `
        <div class="tabs settings-tabs">
            ${tabs.map(t => `
                <button class="tab ${activeTab === t.id ? 'active' : ''}"
                    onclick="location.hash='#/settings/${t.id}'">${t.label}</button>
            `).join('')}
        </div>
        <div id="settings-tab-content"></div>
    `;

    const tabContent = document.getElementById('settings-tab-content');

    switch (activeTab) {
        case 'star-levels':
            await renderStarLevels(tabContent);
            break;
        case 'whiteboard':
            tabContent.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">🎨</div>
                    <h3>Whiteboard Backgrounds</h3>
                    <p>Whiteboard background templates will be available soon.</p>
                </div>
            `;
            break;
        case 'notifications':
            tabContent.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon">🔔</div>
                    <h3>Notifications</h3>
                    <p>Notification settings will be available soon.</p>
                </div>
            `;
            break;
    }
}

let currentStarLevels = [];
let isEditingLevels = false;

async function renderStarLevels(container) {
    try {
        if (!isEditingLevels) {
            currentStarLevels = await api.getStarLevels();
        }

        container.innerHTML = `
            <p class="settings-description">
                Star levels help learners see their progress as they earn stars and unlock higher levels over time. 
                Use these settings to control how many stars are required for each level.
            </p>
            <div class="levels-grid">
                ${currentStarLevels.map((l, i) => `
                    <div class="level-card ${isEditingLevels && i === currentStarLevels.length - 1 && i > 0 ? 'deletable' : ''}">
                        <h4>Level ${l.level}</h4>
                        <div class="level-badge" style="background: transparent;">
                            ${typeof levelIcons !== 'undefined' ? levelIcons[Math.min(i, levelIcons.length - 1)] : `<div style="background: ${levelColors[i] || levelColors[0]}; width: 100%; height: 100%; border-radius: 50%; display: flex; align-items: center; justify-content: center;">${l.level}</div>`}
                        </div>
                        <div class="level-stars">
                            ${isEditingLevels ? `
                                <input type="number" class="level-stars-input" id="level-input-${i}" value="${l.stars_required}" min="0">
                            ` : `
                                <span class="star" style="display: flex; align-items: center; gap: 4px;">
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>
                                    ${l.stars_required} stars
                                </span>
                            `}
                        </div>
                        ${isEditingLevels && i === currentStarLevels.length - 1 && i > 0 ? `
                            <div class="level-delete-btn" onclick="deleteLastLevel()">✕</div>
                        ` : ''}
                    </div>
                `).join('')}
            </div>
            ${isEditingLevels ? `
                <div style="display: flex; gap: 12px; margin-top: 16px;">
                    <button class="btn btn-outline" onclick="addNewLevel()">+ Add Level</button>
                    <button class="btn btn-primary" onclick="saveStarLevels()">Save</button>
                    <button class="btn btn-secondary" onclick="cancelEditStarLevels()">Cancel</button>
                </div>
            ` : `
                <div style="margin-top: 16px;">
                    <button class="btn btn-primary" onclick="editStarLevels()">Edit levels</button>
                </div>
            `}
        `;
    } catch (err) {
        container.innerHTML = `<p>Error: ${err.message}</p>`;
    }
}

function editStarLevels() {
    isEditingLevels = true;
    renderStarLevels(document.getElementById('settings-tab-content'));
}

async function saveStarLevels() {
    try {
        // Collect new values
        const updatedLevels = currentStarLevels.map((l, i) => {
            const input = document.getElementById(`level-input-${i}`);
            return {
                ...l,
                stars_required: parseInt(input.value) || 0
            };
        });
        
        await api.updateStarLevels(updatedLevels);
        isEditingLevels = false;
        showToast('Star levels updated successfully!', 'success');
        renderStarLevels(document.getElementById('settings-tab-content'));
    } catch (err) {
        showToast(err.message, 'error');
    }
}

function cancelEditStarLevels() {
    isEditingLevels = false;
    renderStarLevels(document.getElementById('settings-tab-content'));
}

function deleteLastLevel() {
    if (currentStarLevels.length > 1) {
        currentStarLevels.pop();
        renderStarLevels(document.getElementById('settings-tab-content'));
    }
}

function addNewLevel() {
    const nextLevelNum = currentStarLevels.length + 1;
    let nextStarsRequired = 0;
    if (currentStarLevels.length > 0) {
        const lastStars = currentStarLevels[currentStarLevels.length - 1].stars_required;
        const secondLastStars = currentStarLevels.length > 1 ? currentStarLevels[currentStarLevels.length - 2].stars_required : 0;
        const diff = Math.max(10, lastStars - secondLastStars);
        nextStarsRequired = lastStars + diff;
    }
    
    currentStarLevels.push({
        level: nextLevelNum,
        stars_required: nextStarsRequired
    });
    
    renderStarLevels(document.getElementById('settings-tab-content'));
}
