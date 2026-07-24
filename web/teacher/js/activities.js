// Activities Page — Matches ClassPoint

let currentActivities = [];
let activitiesLoadedCount = 12;

async function renderActivities() {
    const content = document.getElementById('page-content');
    const currentType = new URLSearchParams(location.hash.split('?')[1] || '').get('type') || '';

    try {
        currentActivities = await api.getActivities(currentType);
        activitiesLoadedCount = 12;

        content.innerHTML = `
            <div class="filter-bar">
                <label>Activity type:</label>
                <select id="activity-type-filter" onchange="filterActivities(this.value)">
                    <option value="" ${!currentType ? 'selected' : ''}>All activity types</option>
                    <option value="multiple_choice" ${currentType === 'multiple_choice' ? 'selected' : ''}>Multiple Choice</option>
                    <option value="word_cloud" ${currentType === 'word_cloud' ? 'selected' : ''}>Word Cloud</option>
                    <option value="short_answer" ${currentType === 'short_answer' ? 'selected' : ''}>Short Answer</option>
                    <option value="fill_blanks" ${currentType === 'fill_blanks' ? 'selected' : ''}>Fill in the Blanks</option>
                    <option value="slide_drawing" ${currentType === 'slide_drawing' ? 'selected' : ''}>Slide Drawing</option>
                    <option value="image_upload" ${currentType === 'image_upload' ? 'selected' : ''}>Image Upload</option>
                    <option value="audio_record" ${currentType === 'audio_record' ? 'selected' : ''}>Audio Record</option>
                    <option value="video_upload" ${currentType === 'video_upload' ? 'selected' : ''}>Video Upload</option>
                </select>
            </div>
            <div id="activities-list-container"></div>
        `;
        
        renderActivitiesList();
    } catch (err) {
        content.innerHTML = `<div class="empty-state"><h3>Error</h3><p>${err.message}</p></div>`;
    }
}

function renderActivitiesList() {
    const container = document.getElementById('activities-list-container');
    if (!container) return;

    if (currentActivities.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">👋</div>
                <h3>No activities yet</h3>
                <p>There's no activities yet. After you run LOKAL activities with your students, they will appear here!</p>
            </div>
        `;
        return;
    }

    const toShow = currentActivities.slice(0, activitiesLoadedCount);
    
    let html = '<div id="activities-list" class="activities-grid">';
    html += toShow.map(a => `
        <div class="activity-card" onclick="window.location.hash = '#/reports?activity=' + ${a.id}" style="border: 1px solid #E5E7EB; border-radius: 8px; overflow: hidden; background: white; cursor: pointer; transition: box-shadow 0.2s, transform 0.2s;" onmouseover="this.style.boxShadow='0 4px 6px -1px rgba(0, 0, 0, 0.1), 0 2px 4px -1px rgba(0, 0, 0, 0.06)'; this.style.transform='translateY(-2px)'" onmouseout="this.style.boxShadow='none'; this.style.transform='none'">
            <div class="activity-card-img-wrapper" style="background: #F3F4F6; height: 180px; display: flex; align-items: center; justify-content: center; overflow: hidden; position: relative; border-bottom: 1px solid #E5E7EB;">
                <img src="/uploads/slides/activity_${a.id}.png" style="max-width: 100%; max-height: 100%; object-fit: contain;" onerror="this.outerHTML='<div class=\\'activity-icon-fallback\\' style=\\'font-size: 48px; color: #9CA3AF;\\'>${getActivityIcon(a.type)}</div>'" />
            </div>
            <div class="activity-card-footer">
                <div class="activity-card-type">${getActivityTypeLabel(a.type)}</div>
                <div class="activity-card-actions">
                    <div class="activity-card-action" title="Responses">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M14 2H6a2 2 0 0 0-2 2v16c0 1.1.9 2 2 2h12a2 2 0 0 0 2-2V8l-6-6z"/><path d="M14 3v5h5M16 13H8M16 17H8M10 9H8"/></svg>
                        <span>${a.response_count || 0}</span>
                    </div>
                    <div class="activity-card-action favorite ${a.is_favorite ? 'active' : ''}" onclick="event.stopPropagation(); toggleActivityFavoriteHandler(${a.id}, this)" title="Favorite">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="${a.is_favorite ? 'currentColor' : 'none'}" stroke="currentColor" stroke-width="1.5"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"/></svg>
                    </div>
                </div>
            </div>
        </div>
    `).join('');
    html += '</div>';

    if (activitiesLoadedCount < currentActivities.length) {
        html += `
            <div style="text-align: center; margin-top: 32px; margin-bottom: 32px;">
                <button class="btn btn-secondary" onclick="loadMoreActivities()">Load More</button>
            </div>
        `;
    }

    container.innerHTML = html;
}

function loadMoreActivities() {
    activitiesLoadedCount += 12;
    renderActivitiesList();
}

function filterActivities(type) {
    window.location.hash = type ? '#/activities?type=' + type : '#/activities';
}

window.toggleActivityFavoriteHandler = async function(id, element) {
    try {
        const res = await api.toggleActivityFavorite(id);
        const a = currentActivities.find(act => act.id === id);
        if (a) {
            a.is_favorite = res.is_favorite;
        }
        
        const svg = element.querySelector('svg');
        if (res.is_favorite) {
            element.classList.add('active');
            svg.setAttribute('fill', 'currentColor');
        } else {
            element.classList.remove('active');
            svg.setAttribute('fill', 'none');
        }
    } catch (err) {
        showToast("Failed to toggle favorite", "error");
    }
}
