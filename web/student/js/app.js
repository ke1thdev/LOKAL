// LOKAL Student App Controller

let studentState = {
    classCode: '',
    className: '',
    classData: null,
    participant: null,
    authToken: '',
    session: null,
    currentActivity: null,
    selectedAnswer: null,
    ws: null,
    activityStartTime: null,
    hasSubmitted: false,
    avatarData: '', // Holds base64 encoded custom avatar
    recentStars: 0
};

let activityWarningAudio = null;
let activityFinishedAudio = null;
let warnedActivityId = null;
let finishedActivityId = null;
let addStarRewardAudio = null;
let starRewardTimer = null;
let starRewardRemovalTimer = null;

function stopActivityTimerAudio() {
    [activityWarningAudio, activityFinishedAudio].forEach(audio => {
        if (!audio) return;
        try {
            audio.pause();
            audio.currentTime = 0;
        } catch (_) {}
    });
    activityWarningAudio = null;
    activityFinishedAudio = null;
}

function playActivityTimerSound(fileName, loop) {
    const audio = new Audio(`/assets/sounds/${fileName}`);
    audio.loop = !!loop;
    audio.volume = 0.75;
    audio.play().catch(() => {
        // Browser autoplay policies can reject audio before the first gesture.
        // The visual countdown remains fully functional in that case.
    });
    return audio;
}

function playAddStarRewardSound() {
    if (!addStarRewardAudio) {
        addStarRewardAudio = new Audio('/assets/sounds/add-star-sound.mp3');
        addStarRewardAudio.volume = 0.7;
    }
    try {
        addStarRewardAudio.pause();
        addStarRewardAudio.currentTime = 0;
    } catch (_) {}
    addStarRewardAudio.play().catch(() => {
        // The reward still renders when a browser blocks autoplay.
    });
}

// Professional Black & White School Theme colors
const ANSWER_COLORS = [
    { bg: '#2b2b2b', hover: '#111111', light: 'rgba(0,0,0,0.05)', label: 'A' },
    { bg: '#3a3a3a', hover: '#222222', light: 'rgba(0,0,0,0.05)', label: 'B' },
    { bg: '#4f4f4f', hover: '#333333', light: 'rgba(0,0,0,0.05)', label: 'C' },
    { bg: '#666666', hover: '#444444', light: 'rgba(0,0,0,0.05)', label: 'D' },
    { bg: '#2b2b2b', hover: '#111111', light: 'rgba(0,0,0,0.05)', label: 'E' },
    { bg: '#3a3a3a', hover: '#222222', light: 'rgba(0,0,0,0.05)', label: 'F' },
    { bg: '#4f4f4f', hover: '#333333', light: 'rgba(0,0,0,0.05)', label: 'G' },
    { bg: '#666666', hover: '#444444', light: 'rgba(0,0,0,0.05)', label: 'H' },
];

// Generate a simple device ID
function getDeviceID() {
    let id = localStorage.getItem('lokal_device_id');
    if (!id) {
        id = 'dev_' + Math.random().toString(36).substr(2, 12);
        localStorage.setItem('lokal_device_id', id);
    }
    return id;
}

// ===== STEP 1: Submit Class Code =====
async function submitClassCode() {
    const input = document.getElementById('class-code-input');
    const errorDiv = document.getElementById('code-error');
    const code = input.value.trim().toUpperCase();

    errorDiv.textContent = '';
    errorDiv.style.display = 'none';

    if (!code) {
        errorDiv.textContent = 'Please enter a class code';
        errorDiv.style.display = 'block';
        input.focus();
        return;
    }

    try {
        const classData = await studentApi.getClassByCode(code);
        studentState.classCode = code;
        studentState.classData = classData;

        // Switch to name step
        document.getElementById('step-code').style.display = 'none';
        document.getElementById('step-name').style.display = 'flex';
        document.getElementById('display-code').textContent = code;
        document.getElementById('student-name-input').focus();
    } catch (err) {
        errorDiv.textContent = 'Class not found. Check your code.';
        errorDiv.style.display = 'block';
        input.focus();
        input.select();
    }
}

// ===== STEP 1.5: Avatar Upload Logic =====
let avatarImage = new Image();
let avatarPanX = 0;
let avatarPanY = 0;
let isPanningAvatar = false;
let startPanX = 0;
let startPanY = 0;

function handleAvatarSelect(event) {
    const file = event.target.files[0];
    if (file) {
        const reader = new FileReader();
        reader.onload = function(e) {
            avatarImage.src = e.target.result;
            avatarImage.onload = function() {
                // Reset zoom and pan
                document.getElementById('avatar-zoom').value = 1;
                avatarPanX = 0;
                avatarPanY = 0;
                openAvatarModal();
                drawAvatarCanvas();
            }
        };
        reader.readAsDataURL(file);
    }
}

function openAvatarModal() {
    document.getElementById('edit-avatar-modal').style.display = 'flex';
}

function closeAvatarModal() {
    document.getElementById('edit-avatar-modal').style.display = 'none';
}

function drawAvatarCanvas() {
    const canvas = document.getElementById('avatar-canvas');
    const ctx = canvas.getContext('2d');
    const zoom = parseFloat(document.getElementById('avatar-zoom').value);
    
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    
    // Calculate aspect ratio and dimensions to fill the canvas
    const scale = Math.max(canvas.width / avatarImage.width, canvas.height / avatarImage.height) * zoom;
    const x = (canvas.width / 2) - (avatarImage.width / 2) * scale + avatarPanX;
    const y = (canvas.height / 2) - (avatarImage.height / 2) * scale + avatarPanY;
    
    ctx.drawImage(avatarImage, x, y, avatarImage.width * scale, avatarImage.height * scale);
}

// Add event listener to zoom slider and canvas
document.addEventListener('DOMContentLoaded', () => {
    const zoomSlider = document.getElementById('avatar-zoom');
    if (zoomSlider) {
        zoomSlider.addEventListener('input', drawAvatarCanvas);
    }

    const canvas = document.getElementById('avatar-canvas');
    if (canvas) {
        canvas.addEventListener('mousedown', (e) => {
            isPanningAvatar = true;
            startPanX = e.clientX - avatarPanX;
            startPanY = e.clientY - avatarPanY;
        });
        window.addEventListener('mousemove', (e) => {
            if (!isPanningAvatar) return;
            avatarPanX = e.clientX - startPanX;
            avatarPanY = e.clientY - startPanY;
            drawAvatarCanvas();
        });
        window.addEventListener('mouseup', () => {
            isPanningAvatar = false;
        });

        canvas.addEventListener('touchstart', (e) => {
            if (e.touches.length === 1) {
                isPanningAvatar = true;
                startPanX = e.touches[0].clientX - avatarPanX;
                startPanY = e.touches[0].clientY - avatarPanY;
            }
        }, { passive: false });
        window.addEventListener('touchmove', (e) => {
            if (!isPanningAvatar) return;
            if (e.touches.length === 1) {
                avatarPanX = e.touches[0].clientX - startPanX;
                avatarPanY = e.touches[0].clientY - startPanY;
                drawAvatarCanvas();
                e.preventDefault(); // Prevent scrolling
            }
        }, { passive: false });
        window.addEventListener('touchend', () => {
            isPanningAvatar = false;
        });
    }

    // Close modal if clicking on the overlay
    document.addEventListener('click', (e) => {
        if (e.target.classList.contains('avatar-modal')) {
            e.target.style.display = 'none';
        }
    });
});

function saveAvatar() {
    const canvas = document.getElementById('avatar-canvas');
    const dataUrl = canvas.toDataURL('image/jpeg', 0.8);
    
    // Save to state
    studentState.avatarData = dataUrl;
    
    // Display in join page
    document.getElementById('student-avatar-upload').style.display = 'none';
    document.getElementById('avatar-preview').src = dataUrl;
    document.getElementById('avatar-preview').style.display = 'block';
    
    closeAvatarModal();
}

function resetAvatar() {
    studentState.avatarData = '';
    document.getElementById('student-avatar-upload').style.display = 'flex';
    document.getElementById('avatar-preview').style.display = 'none';
    document.getElementById('avatar-input').value = '';
    closeAvatarModal();
}

// ===== STEP 2: Submit Name =====
async function submitStudentName() {
    const input = document.getElementById('student-name-input');
    const errorDiv = document.getElementById('name-error');
    const name = input.value.trim();

    errorDiv.textContent = '';
    errorDiv.style.display = 'none';

    if (!name) {
        errorDiv.textContent = 'Please enter your name';
        errorDiv.style.display = 'block';
        input.focus();
        return;
    }

    try {
        const result = await studentApi.joinClass(
            studentState.classCode, name, getDeviceID(), studentState.avatarData
        );

        studentState.participant = result.participant;
        studentState.authToken = result.auth_token;
        studentApi.setToken(result.auth_token);
        studentState.session = result.session;
        studentState.classData = result.class;
        studentState.className = result.class.name;

        // Pick up an activity already in progress (late join)
        if (result.activity && !result.activity.closed_at) {
            studentState.currentActivity = result.activity;
            studentState.activityStartTime = Date.now();
        }

        // Save to session storage for reconnection
        sessionStorage.setItem('lokal_student', JSON.stringify({
            classCode: studentState.classCode,
            participant: studentState.participant,
            authToken: studentState.authToken
        }));

        showStudentDashboard();
    } catch (err) {
        errorDiv.textContent = err.message || 'Error joining class';
        errorDiv.style.display = 'block';
    }
}

// ===== STUDENT DASHBOARD =====
function showStudentDashboard() {
    document.getElementById('join-screen').style.display = 'none';
    document.getElementById('student-dashboard').style.display = 'flex';

    // Set code badges
    const badges = document.getElementById('code-badges');
    badges.innerHTML = studentState.classCode.split('').map(c =>
        `<div class="code-badge">${c}</div>`
    ).join('');

    // Initialize stars badge
    const starsEl = document.getElementById('student-total-stars');
    if (starsEl && studentState.participant) {
        starsEl.textContent = studentState.participant.total_stars || 0;
    }

    // Connect WebSocket
    connectStudentWS();

    // Show waiting state or active activity
    if (studentState.currentActivity && !studentState.currentActivity.closed_at) {
        showActivity(studentState.currentActivity);
    } else {
        showWaitingState();
    }
}

function showWaitingState() {
    setAnsweringMode(false);
    const slideContent = document.getElementById('slide-content');
    const inSession = !!studentState.session;
    slideContent.innerHTML = `
        <div class="waiting-state">
            <div class="hourglass-icon">⏳</div>
            <h3>${inSession ? "You're in!" : 'Presenter is not in slideshow'}</h3>
            <p class="waiting-subtitle">Waiting for your teacher to start an activity...</p>
        </div>
    `;

    // Show profile strip
    renderStudentStrip();
    document.getElementById('sidebar-content').innerHTML = '';
}

function starIconSvg(size = 30) {
    return `<svg class="lokal-star-svg" width="${size}" height="${size}" viewBox="0 0 64 64" aria-hidden="true">
        <defs><linearGradient id="studentStarGradient" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stop-color="#FFE783"/><stop offset=".52" stop-color="#FFC943"/><stop offset="1" stop-color="#F59E0B"/>
        </linearGradient></defs>
        <path d="M32 5.5 39.9 22l18.1 2.6-13 12.7 3.1 18L32 46.8 15.9 55.3l3.1-18L6 24.6 24.1 22z"
              fill="url(#studentStarGradient)" stroke="#F4A51C" stroke-width="3" stroke-linejoin="round"/>
        <path d="m32 13.5 5.1 10.6-5.1 3.2-5.1-3.2z" fill="#FFF4B4" opacity=".88"/>
    </svg>`;
}

function renderStudentStrip() {
    const tStrip = document.getElementById('teacher-strip');
    const sStrip = document.getElementById('student-strip');
    const p = studentState.participant;
    const tName = studentState.classData && studentState.classData.teacher_name ? studentState.classData.teacher_name : "Teacher";
    
    if (!p) return;
    
    let avatarHtml = `<div class="avatar" style="width: 48px; height: 48px; border-radius: 50%; background: #444; display: flex; align-items: center; justify-content: center; font-size: 1.2rem; color: white;">${p.name.charAt(0).toUpperCase()}</div>`;
    if (p.avatar_url) {
        avatarHtml = `<img src="${p.avatar_url}" style="width: 48px; height: 48px; border-radius: 50%; object-fit: cover; background: #222;">`;
    }

    if (tStrip) {
        tStrip.innerHTML = `
            <div style="display: flex; align-items: center; gap: 16px;">
                <div class="avatar" style="width: 40px; height: 40px; border-radius: 50%; background: #e0e0e0; display: flex; align-items: center; justify-content: center; font-size: 1.2rem; color: #333;">
                    ${tName.charAt(0).toUpperCase()}
                </div>
                <div style="display: flex; flex-direction: column;">
                    <div style="color: white; font-weight: 500; font-size: 1.1rem;">${escapeHtml(tName)}</div>
                    <div style="color: #999; font-size: 0.85rem;">Presenter</div>
                </div>
            </div>
            <button class="fullscreen-btn" onclick="toggleFullScreen()" style="background: none; border: none; color: white; cursor: pointer; opacity: 0.7; padding: 4px;">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/>
                </svg>
            </button>
        `;
    }

    if (sStrip) {
        const totalStars = Number(p.total_stars) || 0;
        const recentStars = Number(studentState.recentStars) || 0;
        const level = Math.max(1, Math.min(10, Number(p.level) || 1));
        sStrip.innerHTML = `
            <div class="student-identity">
                ${avatarHtml}
                <div>
                    <div class="student-identity-name">${escapeHtml(p.name)}</div>
                    <div class="student-progress-caption">Your class progress</div>
                </div>
            </div>
            <div class="student-progress-cards">
                <div class="student-progress-card" id="student-stars-badge">
                    <span>Total</span>
                    <div>${starIconSvg(30)}<strong id="student-total-stars">${totalStars}</strong></div>
                </div>
                <div class="student-progress-card recent">
                    <span>Recent</span>
                    <div><b class="recent-plus">+</b>${starIconSvg(30)}<strong id="student-recent-stars">${recentStars}</strong></div>
                </div>
                <div class="student-progress-card level">
                    <span>Level</span>
                    <i class="student-level-badge" style="--level-index:${level - 1}" aria-label="Level ${level}"></i>
                </div>
            </div>
        `;
    }
}

function setAnsweringMode(active) {
    document.body.classList.toggle('activity-answering', !!active);
    const studentStrip = document.getElementById('student-strip');
    if (studentStrip) studentStrip.style.display = active ? 'none' : '';
}

// ===== WEBSOCKET =====
function connectStudentWS() {
    if (!studentState.authToken || !studentState.participant || !studentState.classCode) {
        return;
    }
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${location.host}/ws?room=class:${encodeURIComponent(studentState.classCode)}&role=student&id=${studentState.participant.id}&token=${encodeURIComponent(studentState.authToken)}`;

    try {
        if (studentState.ws && studentState.ws.readyState === WebSocket.OPEN) {
            return; // Already connected
        }

        studentState.ws = new WebSocket(wsUrl);

        studentState.ws.onopen = () => {
            console.log('[WS] Connected to room:', studentState.classCode);
        };

        studentState.ws.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                handleWSMessage(msg);
            } catch (e) {
                console.error('[WS] Parse error:', e);
            }
        };

        studentState.ws.onclose = () => {
            if (studentState.authToken) {
                console.log('[WS] Disconnected, reconnecting in 3s...');
                setTimeout(connectStudentWS, 3000);
            }
        };

        studentState.ws.onerror = (err) => {
            console.error('[WS] Error:', err);
        };
    } catch (e) {
        console.error('[WS] Connection failed:', e);
        if (studentState.authToken) setTimeout(connectStudentWS, 5000);
    }
}

function handleWSMessage(msg) {
    switch (msg.type) {
        case 'session_start':
            studentState.session = msg.payload;
            if (!studentState.currentActivity) showWaitingState();
            showStudentToast('Session started!');
            break;

        case 'session_stop':
            studentState.session = null;
            studentState.currentActivity = null;
            studentState.hasSubmitted = false;
            studentState.authToken = '';
            studentApi.setToken('');
            // Class codes are per-presentation — this one is dead now
            sessionStorage.removeItem('lokal_student');
            showWaitingState();
            showStudentToast('Session ended — enter the new class code to rejoin');
            break;

        case 'activity_start':
            stopActivityTimerAudio();
            warnedActivityId = null;
            finishedActivityId = null;
            studentState.currentActivity = msg.payload;
            studentState.selectedAnswer = null;
            studentState.hasSubmitted = false;
            studentState.activityStartTime = Date.now();
            showActivity(msg.payload);
            showStudentToast('New activity started!');
            break;

        case 'activity_close':
            stopActivityTimerAudio();
            setAnsweringMode(false);
            if (studentState.timerInterval) {
                clearInterval(studentState.timerInterval);
                studentState.timerInterval = null;
            }
            const closedSidebar = document.getElementById('sidebar-content');
            if (closedSidebar) {
                closedSidebar.innerHTML = `
                    <div class="activity-closed-card">
                        <div class="activity-closed-icon">✓</div>
                        <h3>Submissions closed</h3>
                        <p>Your teacher is preparing the results.</p>
                    </div>`;
            }
            renderStudentStrip();
            if (studentState.hasSubmitted) {
                showStudentToast('Activity closed — results incoming!');
            } else {
                showStudentToast('Activity closed', 'error');
            }
            break;

        case 'slide_ready':
            // Slide snapshot uploaded — swap in the real slide image
            if (studentState.currentActivity &&
                msg.payload && msg.payload.activity_id === studentState.currentActivity.id) {
                const img = document.getElementById('activity-slide-img');
                if (img) {
                    img.src = msg.payload.slide_url + '?t=' + Date.now();
                    img.style.display = 'block';
                    updateSaveSlideButton();
                    const fb = document.getElementById('slide-text-fallback');
                    if (fb) fb.style.display = 'none';
                }
            }
            break;

        case 'slide_changed':
            // Slide changed without an activity
            if (msg.payload && msg.payload.slide_url) {
                const img = document.getElementById('activity-slide-img');
                if (img) {
                    img.src = msg.payload.slide_url + '?t=' + Date.now();
                    img.style.display = 'block';
                    updateSaveSlideButton();
                    const fb = document.getElementById('slide-text-fallback');
                    if (fb) fb.style.display = 'none';
                }
            }
            break;

        case 'stars_awarded':
            let showReward = false;
            if (msg.payload.correct_only) {
                if (msg.payload.correct_participant_ids && msg.payload.correct_participant_ids.includes(studentState.participant.id)) {
                    showReward = true;
                }
            } else {
                showReward = true; // Awarded to all
            }

            if (showReward) {
                const myResponse = msg.payload.responses?.find(r => r.participant_id === studentState.participant.id);
                const starCount = myResponse?.stars_earned || msg.payload.stars || 1;
                
                // Update total stars locally
                if (studentState.participant) {
                    const exactParticipant = msg.payload.participants?.find(
                        participant => Number(participant.id) === Number(studentState.participant.id)
                    );
                    if (exactParticipant) {
                        studentState.participant = {
                            ...studentState.participant,
                            ...exactParticipant
                        };
                    } else {
                        studentState.participant.total_stars =
                            (studentState.participant.total_stars || 0) + starCount;
                    }
                    studentState.recentStars = (studentState.recentStars || 0) + starCount;
                    renderStudentStrip();
                    
                    // Pop animation
                    const badge = document.getElementById('student-stars-badge');
                    if (badge) {
                        badge.style.transform = 'scale(1.2)';
                        badge.style.transition = 'transform 0.15s ease';
                        setTimeout(() => { badge.style.transform = ''; }, 300);
                    }
                }

                playAddStarRewardSound();
                showStarReward(starCount);
            }
            break;

        case 'participant_updated':
            if (msg.payload?.participant &&
                Number(msg.payload.participant.id) === Number(studentState.participant?.id)) {
                const previousStars = Number(studentState.participant.total_stars) || 0;
                studentState.participant = {
                    ...studentState.participant,
                    ...msg.payload.participant
                };
                const delta = Number(studentState.participant.total_stars) - previousStars;
                if (delta > 0) {
                    studentState.recentStars = (studentState.recentStars || 0) + delta;
                    playAddStarRewardSound();
                    showStarReward(delta);
                }
                renderStudentStrip();
            }
            break;

        default:
            console.log('[WS] Unknown message:', msg.type);
    }
}

// ===== ACTIVITY UI =====
function showActivity(activity) {
    studentState.currentActivity = activity;
    studentState.activityStartTime = studentState.activityStartTime || Date.now();
    
    if (studentState.timerInterval) clearInterval(studentState.timerInterval);
    stopActivityTimerAudio();
    warnedActivityId = null;
    finishedActivityId = null;

    const slideContent = document.getElementById('slide-content');

    // Preserve the teacher/slide context, but hide the student's profile and
    // progress strip while answer choices are active.
    renderStudentStrip();
    setAnsweringMode(!studentState.hasSubmitted && activity.type === 'multiple_choice');

    // Show slide preview with question
    if (activity.question_text) {
        let config = {};
        try { config = JSON.parse(activity.config || '{}'); } catch(e) {}

        let optionsHtml = '';
        if (activity.type === 'multiple_choice' && config.choices) {
            optionsHtml = `<div class="slide-options">${
                config.choices.map((c, i) => `<div>${ANSWER_COLORS[i]?.label || String.fromCharCode(65+i)}. ${escapeHtml(c)}</div>`).join('')
            }</div>`;
        }

        let timerHtml = '';
        if (activity.auto_close_seconds > 0) {
            timerHtml = `<div class="slide-timer-badge" id="slide-timer">⏳ --:--</div>`;
        }

        slideContent.innerHTML = `
            <div class="slide-preview">
                <img id="activity-slide-img" src="/uploads/slides/activity_${activity.id}.png?t=${Date.now()}" alt="Slide Preview" style="display:none; max-width:100%; max-height:100%; border: 1px solid #ccc; box-shadow: 0 4px 12px rgba(0,0,0,0.1);" onload="this.style.display='block'; document.getElementById('slide-text-fallback').style.display='none';" />
                <div id="slide-text-fallback">
                    <h2>${activity.question_text ? escapeHtml(activity.question_text) : 'Activity In Progress'}</h2>
                    ${optionsHtml}
                    <div style="display: flex; align-items: center; justify-content: center; gap: 8px;">
                        <div class="slide-activity-badge">
                            📊 ${getActivityTypeLabel(activity.type)}
                        </div>
                        ${timerHtml}
                    </div>
                </div>
            </div>
        `;
    }

    // Start timer loop
    if (activity.auto_close_seconds > 0) {
        studentState.timerInterval = setInterval(() => {
            const timerEl = document.getElementById('slide-timer');
            if (!timerEl) return;
            
            const serverStart = new Date(activity.started_at).getTime();
            const elapsed = Math.floor((Date.now() - serverStart) / 1000);
            const remaining = activity.auto_close_seconds - elapsed;
            
            if (remaining <= 0) {
                timerEl.innerHTML = `⏱ 00:00`;
                timerEl.classList.add('danger');
                clearInterval(studentState.timerInterval);
                studentState.timerInterval = null;
                if (activityWarningAudio) {
                    try {
                        activityWarningAudio.pause();
                        activityWarningAudio.currentTime = 0;
                    } catch (_) {}
                    activityWarningAudio = null;
                }
                if (finishedActivityId !== activity.id) {
                    finishedActivityId = activity.id;
                    activityFinishedAudio = playActivityTimerSound('ring-bell-after-timer.mp3', false);
                    setTimeout(() => {
                        if (!activityFinishedAudio) return;
                        try {
                            activityFinishedAudio.pause();
                            activityFinishedAudio.currentTime = 0;
                        } catch (_) {}
                        activityFinishedAudio = null;
                    }, 5000);
                }
            } else {
                const mins = Math.floor(remaining / 60);
                const secs = remaining % 60;
                timerEl.innerHTML = `⏱ ${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
                if (remaining <= 10) {
                    timerEl.classList.add('danger');
                    if (warnedActivityId !== activity.id) {
                        warnedActivityId = activity.id;
                        activityWarningAudio = playActivityTimerSound('timer-running-out.mp3', false);
                    }
                } else {
                    timerEl.classList.remove('danger');
                }
            }
        }, 1000);
    }

    // Show answer UI in sidebar
    const sidebar = document.getElementById('sidebar-content');

    if (studentState.hasSubmitted) {
        renderSubmittedUI(sidebar);
    } else if (activity.type === 'multiple_choice') {
        renderMultipleChoiceUI(sidebar, activity);
    } else {
        sidebar.innerHTML = `
            <div class="activity-header">📝 ${getActivityTypeLabel(activity.type)}</div>
            <p style="color: rgba(255,255,255,0.6); font-size: 0.875rem;">
                This activity type will be supported soon!
            </p>
        `;
    }
}

function renderMultipleChoiceUI(container, activity) {
    let config = {};
    try { config = JSON.parse(activity.config || '{}'); } catch(e) {}

    const numChoices = config.num_choices || config.choices?.length || 4;
    const allowMultiple = config.allow_multiple || false;
    const name = studentState.participant?.name || 'Student';

    container.innerHTML = `
        <div class="mc-container">
            <div class="activity-header">
                <span class="activity-header-icon">📊</span>
                <span>Multiple Choice</span>
            </div>
            <div class="activity-instruction">
                <div class="instruction-avatar">${escapeHtml(name.charAt(0).toUpperCase())}</div>
                <div>
                    ${escapeHtml(name.toUpperCase())}, choose
                    <span class="highlight">${allowMultiple ? 'ONE OR MORE' : 'ONLY ONE'}</span>
                    answer${allowMultiple ? 's' : ''} to this question and click Submit.
                </div>
            </div>
            <div class="answer-grid answer-grid-${numChoices <= 4 ? '4' : '8'}" id="answer-grid">
                ${Array.from({length: numChoices}).map((_, i) => {
                    const char = String.fromCharCode(65 + i);
                    return `
                    <button class="answer-btn choice-${char}"
                        data-index="${i}"
                        onclick="selectAnswer(${i}, ${allowMultiple})">
                        <span class="answer-letter">${char}</span>
                    </button>`;
                }).join('')}
            </div>
            <button class="submit-answer-btn" id="submit-answer-btn" disabled onclick="submitAnswer()">
                <span class="submit-btn-text">Submit</span>
            </button>
        </div>
    `;
}

function selectAnswer(index, allowMultiple) {
    if (studentState.hasSubmitted) return; // Already submitted

    if (allowMultiple) {
        if (!studentState.selectedAnswer) studentState.selectedAnswer = [];
        const pos = studentState.selectedAnswer.indexOf(index);
        if (pos > -1) {
            studentState.selectedAnswer.splice(pos, 1);
        } else {
            studentState.selectedAnswer.push(index);
        }
    } else {
        studentState.selectedAnswer = [index];
    }

    // Update UI with selection animation
    document.querySelectorAll('.answer-btn').forEach((btn, i) => {
        const isSelected = studentState.selectedAnswer && studentState.selectedAnswer.includes(i);
        btn.classList.toggle('selected', isSelected);

        // Add a brief scale animation on selection
        if (isSelected && (i === index)) {
            btn.style.transform = 'scale(0.95)';
            setTimeout(() => { btn.style.transform = ''; }, 150);
        }
    });

    // Enable/disable submit button
    const submitBtn = document.getElementById('submit-answer-btn');
    const hasSelection = studentState.selectedAnswer && studentState.selectedAnswer.length > 0;
    submitBtn.disabled = !hasSelection;
    submitBtn.classList.toggle('ready', hasSelection);
}

async function submitAnswer() {
    if (!studentState.selectedAnswer || !studentState.currentActivity || studentState.hasSubmitted) return;

    const btn = document.getElementById('submit-answer-btn');
    btn.disabled = true;
    btn.querySelector('.submit-btn-text').textContent = 'Submitting...';

    const responseTime = Date.now() - (studentState.activityStartTime || Date.now());

    try {
        await studentApi.submitResponse(
            studentState.currentActivity.id,
            studentState.participant.id,
            studentState.selectedAnswer,
            responseTime
        );

        studentState.hasSubmitted = true;
        studentState.lastResponseTime = responseTime;

        // Save submission state to sessionStorage so it survives reload
        const saved = JSON.parse(sessionStorage.getItem('lokal_student') || '{}');
        saved.submissions = saved.submissions || {};
        saved.submissions[studentState.currentActivity.id] = {
            answer: studentState.selectedAnswer,
            responseTime: responseTime
        };
        sessionStorage.setItem('lokal_student', JSON.stringify(saved));

        // Show submitted confirmation and restore the profile/progress strip.
        setAnsweringMode(false);
        renderStudentStrip();
        renderSubmittedUI(document.getElementById('sidebar-content'));

        showStudentToast('Response submitted! ✓');
    } catch (err) {
        showStudentToast(err.message, 'error');
        btn.disabled = false;
        btn.querySelector('.submit-btn-text').textContent = 'Submit';
        studentState.hasSubmitted = false;
    }
}

// ===== UTILITIES =====
function escapeHtml(unsafe) {
    if (typeof unsafe !== 'string') return '';
    return unsafe
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

function toggleFullScreen() {
    let overlay = document.getElementById('fullscreen-overlay');
    if (overlay) {
        document.body.removeChild(overlay);
        return;
    }

    const slideImg = document.getElementById('activity-slide-img');
    const fallbackText = document.getElementById('slide-text-fallback');

    overlay = document.createElement('div');
    overlay.id = 'fullscreen-overlay';
    overlay.style.position = 'fixed';
    overlay.style.top = '0';
    overlay.style.left = '0';
    overlay.style.width = '100vw';
    overlay.style.height = '100vh';
    overlay.style.backgroundColor = 'rgba(0,0,0,0.9)';
    overlay.style.zIndex = '999999';
    overlay.style.display = 'flex';
    overlay.style.alignItems = 'center';
    overlay.style.justifyContent = 'center';
    overlay.style.padding = '20px';
    overlay.style.boxSizing = 'border-box';
    overlay.onclick = function(e) {
        if (e.target === overlay) document.body.removeChild(overlay);
    };

    const innerContainer = document.createElement('div');
    innerContainer.style.backgroundColor = '#ffffff';
    innerContainer.style.borderRadius = '8px';
    innerContainer.style.padding = '10px';
    innerContainer.style.maxWidth = '100%';
    innerContainer.style.maxHeight = '100%';
    innerContainer.style.display = 'flex';
    innerContainer.style.alignItems = 'center';
    innerContainer.style.justifyContent = 'center';
    innerContainer.style.boxShadow = '0 10px 30px rgba(0,0,0,0.5)';
    innerContainer.style.position = 'relative';

    if (slideImg && slideImg.style.display !== 'none') {
        const clonedImg = slideImg.cloneNode();
        clonedImg.style.maxWidth = '100%';
        clonedImg.style.maxHeight = '90vh';
        clonedImg.style.objectFit = 'contain';
        clonedImg.style.boxShadow = 'none';
        innerContainer.appendChild(clonedImg);
    } else if (fallbackText) {
        const clonedFallback = fallbackText.cloneNode(true);
        clonedFallback.style.color = '#333';
        innerContainer.appendChild(clonedFallback);
    }

    const closeBtn = document.createElement('button');
    closeBtn.innerHTML = '✕';
    closeBtn.style.position = 'absolute';
    closeBtn.style.top = '20px';
    closeBtn.style.right = '20px';
    closeBtn.style.background = 'rgba(255,255,255,0.2)';
    closeBtn.style.color = 'white';
    closeBtn.style.border = 'none';
    closeBtn.style.borderRadius = '50%';
    closeBtn.style.width = '40px';
    closeBtn.style.height = '40px';
    closeBtn.style.fontSize = '20px';
    closeBtn.style.cursor = 'pointer';
    closeBtn.onclick = () => document.body.removeChild(overlay);

    overlay.appendChild(innerContainer);
    overlay.appendChild(closeBtn);
    document.body.appendChild(overlay);
}

function showStarReward(starCount) {
    const awarded = Math.max(1, Number(starCount) || 1);
    let reward = document.getElementById('star-reward-float');
    const previousCount = reward ? Number(reward.dataset.count) || 0 : 0;
    const totalCount = previousCount + awarded;

    clearTimeout(starRewardTimer);
    clearTimeout(starRewardRemovalTimer);

    if (!reward) {
        reward = document.createElement('div');
        reward.id = 'star-reward-float';
        reward.className = 'star-reward-float';
        reward.setAttribute('role', 'status');
        reward.setAttribute('aria-live', 'polite');
        document.body.appendChild(reward);
    }

    reward.dataset.count = String(totalCount);
    reward.innerHTML = `
        <div class="star-reward-icon">${starIconSvg(112)}</div>
        <div class="star-reward-count">+${totalCount} ${totalCount === 1 ? 'star' : 'stars'}</div>`;

    // Reuse one lightweight element instead of stacking canvas loops and modals.
    reward.classList.remove('leaving', 'playing');
    void reward.offsetWidth;
    reward.classList.add('playing');

    starRewardTimer = setTimeout(() => {
        if (!reward.isConnected) return;
        reward.classList.add('leaving');
        starRewardRemovalTimer = setTimeout(() => reward.remove(), 280);
    }, 1450);
}

function getActivityTypeLabel(type) {
    const labels = {
        'multiple_choice': 'Multiple Choice',
        'word_cloud': 'Word Cloud',
        'short_answer': 'Short Answer',
        'fill_blanks': 'Fill in the Blanks',
        'slide_drawing': 'Slide Drawing',
        'image_upload': 'Image Upload',
        'audio_record': 'Audio Record',
        'video_upload': 'Video Upload'
    };
    return labels[type] || type;
}

function showStudentToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    container.appendChild(toast);
    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateY(-8px)';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

function toggleStudentMenu() {
    const menu = document.getElementById('student-dropdown-menu');
    menu.classList.toggle('show');
}

function studentLogout() {
    if (confirm('Are you sure you want to log out and leave the class?')) {
        sessionStorage.removeItem('lokal_student');
        studentState.authToken = '';
        studentApi.setToken('');
        if (studentState.ws) studentState.ws.close();
        location.reload();
    }
}

// Close dropdown if clicking outside
window.onclick = function(event) {
    if (!event.target.matches('.menu-icon') && !event.target.closest('.menu-icon')) {
        const dropdowns = document.getElementsByClassName("dropdown-content");
        for (let i = 0; i < dropdowns.length; i++) {
            let openDropdown = dropdowns[i];
            if (openDropdown.classList.contains('show')) {
                openDropdown.classList.remove('show');
            }
        }
    }
}

function renderSubmittedUI(container) {
    const answerLabels = studentState.selectedAnswer.map(i => String.fromCharCode(65 + i));
    const rt = studentState.lastResponseTime || 0;
    const timeText = rt > 0 ? `<p class="submitted-time">Response time: ${(rt / 1000).toFixed(1)}s</p>` : '';

    container.innerHTML = `
        <div class="mc-container">
            <div class="activity-header">
                <span class="activity-header-icon">📊</span>
                <span>Multiple Choice</span>
            </div>
            <div class="submitted-state">
                <div class="submitted-check" style="background: var(--primary);">
                    <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2.5">
                        <polyline points="20,6 9,17 4,12"/>
                    </svg>
                </div>
                <div class="submitted-answers">
                    ${answerLabels.map((l, i) => `
                        <span class="submitted-answer-badge" style="background: #3b3b3b; border: 2px solid var(--primary); color: white;">${l}</span>
                    `).join('')}
                </div>
                <p class="submitted-message" style="color: #ffffff;">Nice job, you've submitted your response!</p>
                ${timeText}
                <button class="btn btn-outline" style="margin-top: 16px;" onclick="unsubmitAnswer()">
                    ✏️ Change Answer
                </button>
            </div>
        </div>
    `;
}

function unsubmitAnswer() {
    if (!studentState.currentActivity) return;
    
    // Clear submission state
    studentState.hasSubmitted = false;
    studentState.selectedAnswer = [];
    studentState.lastResponseTime = null;

    // Update local storage
    const saved = sessionStorage.getItem('lokal_student');
    if (saved) {
        try {
            const data = JSON.parse(saved);
            if (data.submissions && data.submissions[studentState.currentActivity.id]) {
                delete data.submissions[studentState.currentActivity.id];
                sessionStorage.setItem('lokal_student', JSON.stringify(data));
            }
        } catch(e) {}
    }

    // Re-render the activity
    showActivity(studentState.currentActivity);
}

function getVisibleSlideImage() {
    const candidates = [
        document.getElementById('activity-slide-img'),
        ...document.querySelectorAll('#slide-content img')
    ].filter(Boolean);
    return candidates.find(img => {
        const style = window.getComputedStyle(img);
        return img.src && style.display !== 'none' && style.visibility !== 'hidden' &&
            img.getBoundingClientRect().width > 0;
    }) || null;
}

function updateSaveSlideButton() {
    const button = document.getElementById('save-slide-btn');
    if (!button) return;
    button.hidden = !getVisibleSlideImage();
}

async function saveCurrentSlide() {
    const image = getVisibleSlideImage();
    if (!image) {
        showStudentToast('No slide is available to save yet', 'error');
        return;
    }

    try {
        const response = await fetch(image.src, { cache: 'no-store' });
        if (!response.ok) throw new Error('Slide download failed');
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `lokal-slide-${Date.now()}.png`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        showStudentToast('Slide saved');
    } catch (error) {
        showStudentToast('Unable to save this slide', 'error');
    }
}

window.saveCurrentSlide = saveCurrentSlide;

// ===== INIT =====
document.addEventListener('DOMContentLoaded', () => {
    const slideContent = document.getElementById('slide-content');
    if (slideContent) {
        new MutationObserver(updateSaveSlideButton).observe(slideContent, {
            childList: true,
            subtree: true,
            attributes: true,
            attributeFilter: ['src', 'style', 'class']
        });
    }
    // Enter key on code input
    document.getElementById('class-code-input').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') submitClassCode();
    });

    // Enter key on name input
    document.getElementById('student-name-input').addEventListener('keypress', (e) => {
        if (e.key === 'Enter') submitStudentName();
    });

    // Clear errors on input
    document.getElementById('class-code-input').addEventListener('input', () => {
        const err = document.getElementById('code-error');
        if (err) { err.style.display = 'none'; err.textContent = ''; }
    });
    document.getElementById('student-name-input').addEventListener('input', () => {
        const err = document.getElementById('name-error');
        if (err) { err.style.display = 'none'; err.textContent = ''; }
    });

    // Check for saved session
    const saved = sessionStorage.getItem('lokal_student');
    if (saved) {
        try {
            const data = JSON.parse(saved);
            if (!data.authToken || !data.classCode || !data.participant?.id) {
                throw new Error('Saved participant session is incomplete');
            }
            studentState.classCode = data.classCode;
            studentState.participant = data.participant;
            studentState.authToken = data.authToken;
            studentApi.setToken(data.authToken);

            // Resync live state so a reload picks up a running activity
            studentApi.getClassState(studentState.classCode)
                .then(state => {
                    studentState.session = state.session;
                    if (state.participant) {
                        studentState.participant = {
                            ...studentState.participant,
                            ...state.participant
                        };
                    }
                    if (state.class) {
                        studentState.classData = state.class;
                        studentState.className = state.class.name;
                    }
                    if (state.activity && !state.activity.closed_at) {
                        studentState.currentActivity = state.activity;
                        studentState.activityStartTime = Date.now();
                        
                        // Check if already submitted
                        if (data.submissions && data.submissions[state.activity.id]) {
                            studentState.hasSubmitted = true;
                            studentState.selectedAnswer = data.submissions[state.activity.id].answer;
                            studentState.lastResponseTime = data.submissions[state.activity.id].responseTime;
                        }
                    }
                })
                .then(showStudentDashboard)
                .catch(() => {
                    studentState.authToken = '';
                    studentApi.setToken('');
                    sessionStorage.removeItem('lokal_student');
                });
        } catch (e) {
            sessionStorage.removeItem('lokal_student');
        }
    }

    // Check for QR code URL parameter
    const urlParams = new URLSearchParams(window.location.search);
    const codeParam = urlParams.get('code');
    if (codeParam) {
        const input = document.getElementById('class-code-input');
        if (input) {
            input.value = codeParam;
            // Clear the URL parameter so it doesn't linger on refresh
            window.history.replaceState({}, document.title, window.location.pathname);
            // Wait a brief moment for UI to settle, then auto-submit
            setTimeout(submitClassCode, 300);
        }
    }
});
