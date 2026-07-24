// Reports Page — Matches ClassPoint

let currentReportsFlat = [];
let reportsLoadedCount = 10;

async function renderReports() {
    const content = document.getElementById('page-content');
    try {
        currentReportsFlat = await api.getReports();
        window._currentReports = currentReportsFlat;
        reportsLoadedCount = 10;

        if (currentReportsFlat.length === 0) {
            content.innerHTML = `
                <div class="empty-state">
                    <div class="empty-icon gradient-icon">📅</div>
                    <h3>No reports yet</h3>
                    <p>You don't have any reports yet. After you teach with LOKAL, the class reports will appear here!</p>
                </div>
            `;
            return;
        }

        renderReportsList();

    } catch (err) {
        content.innerHTML = `<div class="empty-state"><h3>Error</h3><p>${err.message}</p></div>`;
    }
}

function renderReportsList() {
    const content = document.getElementById('page-content');
    if (!content) return;

    const toShow = currentReportsFlat.slice(0, reportsLoadedCount);

    // Group reports by month
    const groups = {};
    toShow.forEach((r, idx) => {
        const date = new Date(r.session_date);
        const monthYear = date.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
        if (!groups[monthYear]) {
            groups[monthYear] = [];
        }
        // Store index from the original array to retrieve later
        groups[monthYear].push({ ...r, _idx: currentReportsFlat.indexOf(r) });
    });

    let html = '<div class="reports-container">';

    for (const [month, monthReports] of Object.entries(groups)) {
        html += `
            <div class="report-month-wrapper">
                <div class="report-month-badge">${month}</div>
            </div>
            <div style="overflow-x: auto; -webkit-overflow-scrolling: touch; width: 100%;">
                <div class="cp-table-container" style="min-width: 800px;">
                    <div class="cp-table-header" style="display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 2fr 40px;">
                        <div>Class time</div>
                        <div>Class code</div>
                        <div>Activities</div>
                        <div>Stars awarded</div>
                        <div>Top player(s)</div>
                        <div></div>
                    </div>
        `;

        monthReports.forEach(r => {
            const time = new Date(r.session_date).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
            const day = new Date(r.session_date).toLocaleDateString('en-US', { day: 'numeric', month: 'short' });
            
            // Parse top players if any
            let topPlayersHtml = '';
            if (r.top_players) {
                const players = r.top_players.split(',').map(p => p.trim()).filter(p => p);
                if (players.length > 0) {
                    const firstPlayer = players[0];
                    const initial = firstPlayer.charAt(0).toUpperCase();
                    topPlayersHtml = `
                        <div class="report-col">
                            <div class="top-player-avatar">${initial}</div>
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
                <div class="cp-table-row" onclick="renderReportDetails(${r._idx})" style="display: grid; grid-template-columns: 2fr 1fr 1fr 1fr 2fr 40px; cursor: pointer;">
                    <div class="report-col time" style="display: flex; align-items: center; font-size: 14px;">
                        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="opacity: 0.7; margin-right: 8px;"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>
                        ${day}, ${time} ${newBadgeHtml} ${favIconHtml}
                    </div>
                    <div class="report-col">
                        <span class="class-code" style="color: var(--accent-teal); font-size: 12px; padding: 2px 8px; background: #e6fffa; border-radius: 4px;">${r.class_code}</span>
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
                    <div class="report-col" style="justify-content: flex-end;">
                        <div class="dropdown-container" onclick="event.stopPropagation()">
                            <button class="participant-menu-btn" onclick="window.toggleReportMenu(${r.session_id})">⋮</button>
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
                </div>
            `;
        });

        html += `</div></div>`;
    }

    html += `</div>`;

    if (reportsLoadedCount < currentReportsFlat.length) {
        html += `
            <div style="text-align: center; margin-top: 32px; margin-bottom: 32px;">
                <button class="btn btn-secondary" onclick="loadMoreReports()">Load More</button>
            </div>
        `;
    }

    content.innerHTML = html;
}

function loadMoreReports() {
    reportsLoadedCount += 10;
    renderReportsList();
}

window.toggleReportMenu = function(id) {
    document.querySelectorAll('.participant-menu-btn + .dropdown-menu').forEach(m => {
        if (m.id !== `report-menu-${id}`) m.classList.remove('show');
    });
    const menu = document.getElementById(`report-menu-${id}`);
    if (menu) menu.classList.toggle('show');
};

window.deleteActivity = async function(id) {
    if (!confirm('Are you sure you want to delete this activity? This cannot be undone.')) return;
    try {
        await api.deleteActivity(id);
        window.history.back();
    } catch (err) {
        alert("Error deleting activity: " + err.message);
    }
};

// Global click handler to close menus
document.addEventListener('click', function closeMenus(e) {
    if (!e.target.closest('.dropdown-container')) {
        document.querySelectorAll('.participant-menu-btn + .dropdown-menu').forEach(m => m.classList.remove('show'));
    }
});;

window.handleFavoriteReport = async function(sessionId) {
    document.getElementById('report-dropdown-menu')?.remove();
    try {
        const res = await api.toggleFavoriteReport(sessionId);
        const report = currentReportsFlat.find(r => r.session_id === sessionId);
        if (report) {
            report.is_favorite = res.is_favorite;
        }
        if (window._currentClassReports) {
            const cr = window._currentClassReports.find(r => r.session_id === sessionId);
            if (cr) cr.is_favorite = res.is_favorite;
        }
        showToast(res.is_favorite ? "Report added to favorite" : "Report removed from favorite", "success");
        if (window.location.hash.includes('reports')) {
            renderReportsList(); 
        } else {
            // inside class reports
            handleRoute();
        }
    } catch(err) {
        showToast("Error: " + err.message, "error");
    }
};

window.handleDeleteReport = async function(sessionId) {
    document.getElementById('report-dropdown-menu')?.remove();
    showConfirmModal(
        "Delete Report", 
        "Are you sure you want to delete this report? This will permanently delete all activities and responses in this session.",
        async () => {
            try {
                await api.deleteReport(sessionId);
                showToast("Report deleted successfully", "success");
                // Reload the whole reports list from server
                if (window.location.hash === '#/reports') {
                    renderReports(); 
                } else {
                    handleRoute();
                }
            } catch(err) {
                showToast("Error: " + err.message, "error");
            }
        }
    );
};

async function renderReportDetails(index) {
    const report = window._currentReports[index];
    const content = document.getElementById('page-content');
    
    // Show loading state
    content.innerHTML = `
        <div style="margin-bottom: 24px;">
            <button class="btn btn-secondary" onclick="renderReports()" style="display: flex; align-items: center; gap: 8px;">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15,18 9,12 15,6"/></svg>
                Back to Reports
            </button>
        </div>
        <div class="empty-state">Loading participants...</div>
    `;

    try {
        const participants = await api.getReportDetails(report.session_id);
        
        let participantsHtml = '';
        if (participants && participants.length > 0) {
            participantsHtml = `
                <table class="detail-table">
                    <thead>
                        <tr>
                            <th>Student Name</th>
                            <th>Stars Earned</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${participants.map(p => `
                            <tr>
                                <td>
                                    <div style="display: flex; align-items: center; gap: 12px;">
                                        <div class="top-player-avatar">${p.name.charAt(0).toUpperCase()}</div>
                                        <span style="font-weight: 500;">${escapeHtml(p.name)}</span>
                                    </div>
                                </td>
                                <td style="color: var(--warning); font-weight: 600; display: flex; align-items: center; gap: 6px;">
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>
                                    ${p.score}
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            `;
        } else {
            participantsHtml = `<div style="text-align: center; padding: 32px; color: var(--text-secondary);">No participants joined this session.</div>`;
        }

        content.innerHTML = `
            <div style="margin-bottom: 24px;">
                <button class="btn btn-secondary" onclick="renderReports()" style="display: flex; align-items: center; gap: 8px;">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15,18 9,12 15,6"/></svg>
                    Back to Reports
                </button>
            </div>
            
            <div class="profile-card" style="max-width: 800px; margin: 0 auto; width: 100%;">
                <div class="profile-header" style="border-bottom: none; margin-bottom: 16px; padding-bottom: 0;">
                    <div class="profile-avatar" style="background: var(--accent-teal); font-size: 32px;">📊</div>
                    <div class="profile-title">
                        <h2 style="font-size: 24px;">${escapeHtml(report.class_name)} Session</h2>
                        <p style="font-size: 14px;">${formatDate(report.session_date)}</p>
                    </div>
                </div>
                
                <div style="background: var(--bg-main); padding: 24px; border-radius: var(--radius-md); border: 1px solid var(--border);">
                    <div class="profile-info-grid" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(110px, 1fr)); gap: 16px; align-items: start;">
                        <div class="profile-stat">
                            <label>Class Code</label>
                            <span style="color: var(--accent-teal); font-weight: 700;">${report.class_code}</span>
                        </div>
                        <div class="profile-stat">
                            <label>Total Participants</label>
                            <span>${report.participant_count}</span>
                        </div>
                        <div class="profile-stat">
                            <label>Activities Conducted</label>
                            <span>${report.activities_count}</span>
                        </div>
                        <div class="profile-stat">
                            <label>Stars Awarded</label>
                            <span style="color: var(--warning); font-weight: 600; display: flex; align-items: center; gap: 6px;">
                                <svg width="20" height="20" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>
                                ${report.stars_awarded || 0}
                            </span>
                        </div>
                    </div>
                </div>
                
                <h3 style="margin-top: 32px; font-size: 16px; font-weight: 600;">Participant Scores</h3>
                ${participantsHtml}
            </div>
        `;
    } catch (err) {
        content.innerHTML = `
            <div style="margin-bottom: 24px;">
                <button class="btn btn-secondary" onclick="renderReports()" style="display: flex; align-items: center; gap: 8px;">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15,18 9,12 15,6"/></svg>
                    Back to Reports
                </button>
            </div>
            <div class="empty-state"><h3>Error loading details</h3><p>${err.message}</p></div>
        `;
    }
}

let activityReportPoll = null;

async function renderActivityReport(activityId) {
    if (activityReportPoll) clearInterval(activityReportPoll);

    const content = document.getElementById('page-content');
    content.innerHTML = `<div class="empty-state">Loading activity report...</div>`;
    
    try {
        const activity = await api.getActivity(activityId);
        window._currentActivityData = activity;
        const responses = await api.getResponses(activityId);
        
        let questionText = activity.question_text || 'Untitled Question';
        let options = [];
        try {
            const config = JSON.parse(activity.config_json || '{}');
            options = config.options || [];
            if (options.length === 0 && activity.type === 'multiple_choice') {
                const count = config.num_options || 4;
                for (let i = 0; i < count; i++) {
                    options.push(String.fromCharCode(65 + i));
                }
            }
        } catch { }
        
        // Tally responses
        const tally = {};
        options.forEach(opt => tally[opt] = 0);
        
        const optionVoters = {}; // map option -> list of students
        options.forEach(opt => optionVoters[opt] = []);

        responses.forEach(r => {
            try {
                let ans = r.answer;
                if (typeof ans === 'string') ans = JSON.parse(ans);
                
                if (Array.isArray(ans) && ans.length > 0 && typeof ans[0] === 'number') {
                    ans = { selected_options: ans.map(i => String.fromCharCode(65 + i)) };
                }
                
                if (ans.selected_options) {
                    ans.selected_options.forEach(opt => {
                        if (tally[opt] !== undefined) {
                            tally[opt]++;
                            optionVoters[opt].push({ name: r.participant_name, id: r.participant_id, responseId: r.id });
                        }
                    });
                }
            } catch { }
        });
        
        const maxVotes = Math.max(...Object.values(tally), 1);
        const barColors = ['#00d296', '#f43f5e', '#3b82f6', '#f59e0b', '#0B1F1C', '#f97316'];
        const chartHtml = options.map((opt, i) => {
            const count = tally[opt] || 0;
            const totalResponses = responses.length;
            const pct = totalResponses > 0 ? Math.round((count / totalResponses) * 100) : 0;
            const height = count > 0 ? Math.max((count / maxVotes) * 200, 24) : 6;
            const color = barColors[i % barColors.length];
            const escapedOpt = escapeHtml(opt).replace(/'/g, "\\'");
            
            return `
                <div class="bar-col" style="display: flex; flex-direction: column; align-items: center; justify-content: flex-end; cursor: pointer; flex: 1; margin: 0 16px; height: 280px;" onclick="openWhoChoseModal('${escapedOpt}')">
                    <div style="display: flex; flex-direction: column; align-items: center; width: 100%;">
                        <div style="font-weight: 600; background: #111827; color: white; padding: 4px 10px; border-radius: 12px; font-size: 11px; margin-bottom: 8px; position: relative;">
                            ${count} (${pct}%)
                            <div style="position: absolute; bottom: -4px; left: 50%; transform: translateX(-50%); border-width: 4px 4px 0 4px; border-style: solid; border-color: #111827 transparent transparent transparent;"></div>
                        </div>
                        <div style="width: 100%; max-width: 100px; height: ${height}px; background: ${color}; border-radius: ${count > 0 ? '8px 8px 0 0' : '4px'};"></div>
                    </div>
                    <div style="margin-top: 16px; font-weight: 500; width: 100%; text-align: center; color: #111827; font-size: 15px;">
                        ${escapeHtml(opt)}
                    </div>
                </div>
            `;
        }).join('');

        window._currentOptionVoters = optionVoters;

        content.innerHTML = `
            <div style="margin-bottom: 24px; display: flex; align-items: center; justify-content: center; position: relative;">
                <button class="btn btn-secondary" onclick="window.history.back()" style="position: absolute; left: 0; display: flex; align-items: center; gap: 8px; border: none; background: transparent; font-size: 18px; padding: 0;">
                    <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15,18 9,12 15,6"/></svg>
                </button>
                <h2 style="font-size: 16px; font-weight: 600; color: #111827;">${getActivityTypeLabel(activity.type)}</h2>
            </div>
            
            <div style="display: flex; flex-direction: column; align-items: center; max-width: 900px; margin: 0 auto; padding-top: 16px;">
                <div style="width: 100%; border: 1px solid var(--border); border-radius: 8px; overflow: hidden; margin-bottom: 24px; background: white; min-height: 200px; display: flex; flex-direction: column; justify-content: center; align-items: center; padding: 40px; position: relative;">
                    <h1 id="report-qtext-${activity.id}" style="font-size: 32px; font-weight: normal; margin-bottom: 24px;">${escapeHtml(questionText)}</h1>
                    <img src="/uploads/slides/activity_${activity.id}.png" style="max-width: 100%; max-height: 400px; object-fit: contain;" 
                         onload="document.getElementById('report-qtext-${activity.id}').style.display='none'; document.getElementById('report-qopts-${activity.id}').style.display='none';" 
                         onerror="this.style.display='none'" />
                    <div id="report-qopts-${activity.id}" style="display: flex; flex-direction: column; gap: 8px; font-size: 16px; margin-bottom: 40px; text-align: left;">
                        ${options.map((opt, i) => `<div><span style="font-weight: bold;">${String.fromCharCode(65 + i)}.</span> ${escapeHtml(opt)}</div>`).join('')}
                    </div>
                </div>
                
                <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 48px; width: 100%; flex-wrap: wrap; gap: 16px;">
                    <div style="display: flex; gap: 32px; flex-wrap: wrap;">
                        <div style="display: flex; flex-direction: column; align-items: flex-start; gap: 4px;">
                            <div style="display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 15px; color: #111827;">
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16c0 1.1.9 2 2 2h12a2 2 0 0 0 2-2V8l-6-6z"/><path d="M14 3v5h5M16 13H8M16 17H8M10 9H8"/></svg>
                                <span id="report-responses-${activity.id}">${responses.length}</span>
                            </div>
                            <span style="color: #6B7280; font-size: 13px;">Responses</span>
                        </div>
                        <div style="display: flex; flex-direction: column; align-items: flex-start; gap: 4px;">
                            <div style="display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 15px; color: #111827;">
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                                <span id="report-duration-${activity.id}">${activity.closed_at ? Math.round((new Date(activity.closed_at) - new Date(activity.started_at))/1000) : (activity.auto_close_seconds || 0)}s</span>
                            </div>
                            <span style="color: #6B7280; font-size: 13px;">Duration</span>
                        </div>
                        <div style="display: flex; flex-direction: column; align-items: flex-start; gap: 4px;">
                            <div style="display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 15px; color: #111827;">
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg>
                                <span id="report-stars-${activity.id}">${responses.reduce((sum, r) => sum + (r.stars_earned || 0), 0)}</span>
                            </div>
                            <span style="color: #6B7280; font-size: 13px;">Stars awarded</span>
                        </div>
                    </div>
                    <div style="display: flex; align-items: center; gap: 8px;">
                        <button class="btn btn-secondary" onclick="toggleActivityFavoriteHandler(${activity.id}, this)" style="background: #F3F4F6; border: none; padding: 10px; color: ${activity.is_favorite ? '#EF4444' : '#6B7280'}; border-radius: 6px;">
                            <svg width="18" height="18" viewBox="0 0 24 24" fill="${activity.is_favorite ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="2"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"/></svg>
                        </button>
                        <div style="position: relative;" class="dropdown-container">
                            <button class="participant-menu-btn btn btn-secondary" onclick="event.stopPropagation(); toggleReportMenu('act-${activity.id}')" style="background: #F3F4F6; border: none; padding: 10px; color: #6B7280; border-radius: 6px;">
                                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="1"/><circle cx="12" cy="5" r="1"/><circle cx="12" cy="19" r="1"/></svg>
                            </button>
                            <div class="dropdown-menu" id="report-menu-act-${activity.id}" style="width: 180px; padding: 8px; border-radius: 8px;">
                                <button class="dropdown-item text-danger" onclick="deleteActivity(${activity.id})" style="display: flex; align-items: center; gap: 8px; color: #ef4444; padding: 8px; width: 100%; border: none; background: transparent; cursor: pointer;">
                                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                                    Delete activity
                                </button>
                            </div>
                        </div>
                    </div>
                </div>

                <div id="report-chart-${activity.id}" style="display: flex; align-items: flex-end; justify-content: center; width: 100%; max-width: 600px; margin-top: auto;">
                    ${chartHtml}
                </div>
            </div>        </div>
            
            <!-- Who Chose Modal -->
            <div id="who-chose-modal" class="custom-modal-overlay" style="display: none;">
                <div class="custom-modal" style="max-width: 500px;">
                    <h2 id="who-chose-title" style="text-align: center; font-size: 18px; margin-bottom: 24px;">Who chose "A"</h2>
                    <div id="who-chose-list" style="max-height: 300px; overflow-y: auto; display: flex; flex-direction: column; gap: 12px; margin-bottom: 24px;">
                        <!-- List goes here -->
                    </div>
                    <div style="text-align: center;">
                        <button class="btn btn-primary" onclick="awardStarsToVoters()" style="background: var(--primary); border: none; padding: 10px 24px; border-radius: 6px; display: inline-flex; align-items: center; justify-content: center; gap: 8px;">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="#FBBF24" stroke="#F59E0B" stroke-width="1.5"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"></polygon></svg> Award star
                        </button>
                    </div>
                </div>
            </div>
            </div>
        `;

        // Start polling
        activityReportPoll = setInterval(async () => {
            if (!document.getElementById(`report-responses-${activity.id}`)) {
                clearInterval(activityReportPoll);
                return;
            }
            try {
                const newResponses = await api.getResponses(activityId);
                const updatedActivity = await api.getActivity(activityId);
                
                const newTally = {};
                options.forEach(opt => newTally[opt] = 0);
                const newOptionVoters = {};
                options.forEach(opt => newOptionVoters[opt] = []);

                newResponses.forEach(r => {
                    try {
                        let ans = r.answer;
                        if (typeof ans === 'string') ans = JSON.parse(ans);
                        
                        if (Array.isArray(ans) && ans.length > 0 && typeof ans[0] === 'number') {
                            ans = { selected_options: ans.map(i => String.fromCharCode(65 + i)) };
                        }
                        
                        if (ans.selected_options) {
                            ans.selected_options.forEach(opt => {
                                if (newTally[opt] !== undefined) {
                                    newTally[opt]++;
                                    newOptionVoters[opt].push({ name: r.participant_name, id: r.participant_id, responseId: r.id });
                                }
                            });
                        }
                    } catch { }
                });

                const newMaxVotes = Math.max(...Object.values(newTally), 1);
                const newChartHtml = options.map((opt, i) => {
                    const count = newTally[opt] || 0;
                    const totalResponses = newResponses.length;
                    const pct = totalResponses > 0 ? Math.round((count / totalResponses) * 100) : 0;
                    const height = count > 0 ? Math.max((count / newMaxVotes) * 200, 24) : 6;
                    const color = barColors[i % barColors.length];
                    const escapedOpt = escapeHtml(opt).replace(/'/g, "\\'");
                    
                    return `
                        <div class="bar-col" style="display: flex; flex-direction: column; align-items: center; justify-content: flex-end; cursor: pointer; flex: 1; margin: 0 16px; height: 280px;" onclick="openWhoChoseModal('${escapedOpt}')">
                            <div style="display: flex; flex-direction: column; align-items: center; width: 100%;">
                                <div style="font-weight: 600; background: #111827; color: white; padding: 4px 10px; border-radius: 12px; font-size: 11px; margin-bottom: 8px; position: relative;">
                                    ${count} (${pct}%)
                                    <div style="position: absolute; bottom: -4px; left: 50%; transform: translateX(-50%); border-width: 4px 4px 0 4px; border-style: solid; border-color: #111827 transparent transparent transparent;"></div>
                                </div>
                                <div style="width: 100%; max-width: 100px; height: ${height}px; background: ${color}; border-radius: ${count > 0 ? '8px 8px 0 0' : '4px'};"></div>
                            </div>
                            <div style="margin-top: 16px; font-weight: 500; width: 100%; text-align: center; color: #111827; font-size: 15px;">
                                ${escapeHtml(opt)}
                            </div>
                        </div>
                    `;
                }).join('');

                window._currentOptionVoters = newOptionVoters;
                document.getElementById(`report-chart-${activity.id}`).innerHTML = newChartHtml;
                document.getElementById(`report-responses-${activity.id}`).textContent = newResponses.length;
                document.getElementById(`report-duration-${activity.id}`).textContent = updatedActivity.closed_at ? Math.round((new Date(updatedActivity.closed_at) - new Date(updatedActivity.started_at))/1000) + 's' : (updatedActivity.auto_close_seconds || 0) + 's';
                document.getElementById(`report-stars-${activity.id}`).textContent = newResponses.reduce((sum, r) => sum + (r.stars_earned || 0), 0);

                if (document.getElementById('who-chose-modal').style.display === 'flex' && window._currentAwardOption) {
                    openWhoChoseModal(window._currentAwardOption);
                }
            } catch (err) { }
        }, 2000);

    } catch (err) {
        content.innerHTML = `<div class="empty-state"><h3>Error</h3><p>${err.message}</p></div>`;
    }
}

function openWhoChoseModal(option) {
    const list = window._currentOptionVoters[option] || [];
    document.getElementById('who-chose-title').textContent = `Who chose "${option}"`;
    
    window._currentAwardOption = option; // store for awarding
    
    const listEl = document.getElementById('who-chose-list');
    
    // Grid layout like ClassPoint
    listEl.style.display = 'grid';
    listEl.style.gridTemplateColumns = 'repeat(auto-fill, minmax(80px, 1fr))';
    listEl.style.gap = '24px';
    listEl.style.justifyItems = 'center';
    
    if (list.length === 0) {
        listEl.style.display = 'flex';
        listEl.innerHTML = `<div style="text-align: center; color: var(--text-secondary); width: 100%;">No one chose this option.</div>`;
    } else {
        listEl.innerHTML = list.map(v => `
            <div style="display: flex; flex-direction: column; align-items: center; gap: 8px;">
                <div style="width: 56px; height: 56px; background: #9ca3af; color: white; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-weight: bold; font-size: 20px;">
                    ${v.name.charAt(0).toUpperCase()}
                </div>
                <span style="font-weight: 500; font-size: 13px; color: #111827; text-align: center; word-break: break-word; max-width: 80px;">${escapeHtml(v.name.toUpperCase())}</span>
            </div>
        `).join('');
    }
    
    document.getElementById('who-chose-modal').style.display = 'flex';
}

// Close modal when clicking outside
document.addEventListener('click', function(e) {
    const modal = document.getElementById('who-chose-modal');
    if (e.target === modal) {
        modal.style.display = 'none';
    }
});

async function awardStarsToVoters() {
    const option = window._currentAwardOption;
    const voters = window._currentOptionVoters[option] || [];
    if (voters.length === 0) {
        document.getElementById('who-chose-modal').style.display = 'none';
        return;
    }
    
    document.getElementById('who-chose-modal').style.display = 'none';
    
    const activity = window._currentActivityData;
    if (!activity || !activity.class_id) {
        showToast("Error: Missing class ID", "error");
        return;
    }
    
    try {
        let awardedCount = 0;
        for (const voter of voters) {
            await api.adjustParticipantStars(activity.class_id, voter.id, 1);
            awardedCount++;
        }
        showToast(`Awarded stars to ${awardedCount} student(s)!`, "success");
    } catch(err) {
        showToast("Error awarding stars: " + err.message, "error");
    }
}
