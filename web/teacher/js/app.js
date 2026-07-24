// LOKAL SPA Router & App Controller

// ===== INITIALIZATION =====
document.addEventListener('DOMContentLoaded', () => {
    if (api.isAuthenticated()) {
        showDashboard();
    } else {
        showAuth();
        if (location.hash === '#/register') {
            document.getElementById('login-form').style.display = 'none';
            document.getElementById('register-form').style.display = 'block';
        }
    }

    // Auth form handlers
    document.getElementById('login-form').addEventListener('submit', handleLogin);
    document.getElementById('register-form').addEventListener('submit', handleRegister);
    document.getElementById('show-register').addEventListener('click', (e) => {
        e.preventDefault();
        document.getElementById('login-form').style.display = 'none';
        document.getElementById('register-form').style.display = 'block';
        document.getElementById('auth-error').style.display = 'none';
    });
    document.getElementById('show-login').addEventListener('click', (e) => {
        e.preventDefault();
        document.getElementById('register-form').style.display = 'none';
        document.getElementById('login-form').style.display = 'block';
        document.getElementById('auth-error').style.display = 'none';
    });

    // Create class form
    document.getElementById('create-class-form').addEventListener('submit', handleCreateClass);
    document.getElementById('edit-class-form').addEventListener('submit', handleEditClass);

    // Name input updates avatar letter
    document.getElementById('class-name-input').addEventListener('input', (e) => {
        const letter = e.target.value.charAt(0).toUpperCase() || 'C';
        document.getElementById('avatar-letter').textContent = letter;
    });

    // Hash routing
    window.addEventListener('hashchange', handleRoute);

    // Close menu on outside click
    document.addEventListener('click', (e) => {
        const menu = document.getElementById('user-menu');
        const avatar = document.getElementById('header-avatar');
        if (!avatar.contains(e.target)) {
            menu.style.display = 'none';
        }

        // Close modal if clicking on the overlay
        if (e.target.classList.contains('modal-overlay') || e.target.classList.contains('custom-modal-overlay')) {
            if (e.target.id === 'custom-confirm-modal') {
                const cancelBtn = document.getElementById('btn-confirm-cancel');
                if (cancelBtn) cancelBtn.click();
            } else {
                e.target.style.display = 'none';
            }
        }

        // Close sidebar on mobile if clicked outside
        if (window.innerWidth <= 768) {
            const dashboard = document.getElementById('dashboard');
            const sidebar = document.querySelector('.sidebar');
            const toggleBtn = document.getElementById('menu-toggle-btn');
            if (dashboard.classList.contains('sidebar-toggled') && !sidebar.contains(e.target) && !toggleBtn.contains(e.target)) {
                dashboard.classList.remove('sidebar-toggled');
            }
        }
    });
});

// ===== CUSTOM MODAL =====
function showConfirm(title, message, confirmText = 'Confirm', isDanger = true) {
    return new Promise((resolve) => {
        const modal = document.getElementById('custom-confirm-modal');
        document.getElementById('confirm-title').textContent = title;
        document.getElementById('confirm-message').textContent = message;
        
        const btnConfirm = document.getElementById('btn-confirm-action');
        btnConfirm.textContent = confirmText;
        btnConfirm.className = isDanger ? 'btn btn-danger' : 'btn btn-primary';

        modal.style.display = 'flex';

        const handleCancel = () => {
            modal.style.display = 'none';
            cleanup();
            resolve(false);
        };

        const handleConfirm = () => {
            modal.style.display = 'none';
            cleanup();
            resolve(true);
        };

        const cleanup = () => {
            document.getElementById('btn-confirm-cancel').removeEventListener('click', handleCancel);
            btnConfirm.removeEventListener('click', handleConfirm);
        };

        document.getElementById('btn-confirm-cancel').addEventListener('click', handleCancel);
        btnConfirm.addEventListener('click', handleConfirm);
    });
}

// ===== AUTH =====
async function handleLogin(e) {
    e.preventDefault();
    const username = document.getElementById('login-username').value;
    const password = document.getElementById('login-password').value;
    try {
        await api.login(username, password);
        showDashboard();
    } catch (err) {
        showAuthError(err.message);
    }
}

async function handleRegister(e) {
    e.preventDefault();
    const displayName = document.getElementById('reg-display-name').value;
    const username = document.getElementById('reg-username').value;
    const email = document.getElementById('reg-email').value;
    const password = document.getElementById('reg-password').value;
    try {
        await api.register(username, email, password, displayName);
        showDashboard();
    } catch (err) {
        showAuthError(err.message);
    }
}

function showAuthError(msg) {
    const el = document.getElementById('auth-error');
    el.textContent = msg;
    el.style.display = 'block';
}

function showAuth() {
    document.getElementById('auth-screen').style.display = 'flex';
    document.getElementById('dashboard').style.display = 'none';
}

function showDashboard() {
    document.getElementById('auth-screen').style.display = 'none';
    document.getElementById('dashboard').style.display = 'flex';

    // Update user avatar
    if (api.teacher) {
        const initial = (api.teacher.display_name || api.teacher.username || 'T').charAt(0).toUpperCase();
        document.getElementById('user-avatar').textContent = initial;
        document.getElementById('user-menu-name').textContent = api.teacher.display_name || api.teacher.username;
    }

    // Navigate to initial route
    if (!location.hash || location.hash === '#/') {
        location.hash = '#/server';
    } else {
        handleRoute();
    }
}

async function logout() {
    if (await showConfirm('Sign Out', 'Are you sure you want to sign out?', 'Sign Out', true)) {
        try {
            await api.logout();
        } catch (_) {
            api.clearAuth();
        }
        showAuth();
        location.hash = '';
    }
}

function toggleUserMenu() {
    const menu = document.getElementById('user-menu');
    menu.style.display = menu.style.display === 'none' ? 'block' : 'none';
}

function togglePassword(inputId, btn) {
    const input = document.getElementById(inputId);
    if (input.type === 'password') {
        input.type = 'text';
        btn.textContent = 'Hide';
    } else {
        input.type = 'password';
        btn.textContent = 'Show';
    }
}

// ===== ROUTER =====
function handleRoute() {
    let hash = location.hash.slice(2) || 'server'; // Remove '#/'
    
    // Parse query string if present
    let queryParams = new URLSearchParams();
    if (hash.includes('?')) {
        const parts = hash.split('?');
        hash = parts[0];
        queryParams = new URLSearchParams(parts[1]);
    }
    
    const parts = hash.split('/');
    const page = parts[0];
    const subId = parts[1];
    const subPage = parts[2];

    // Update nav active state
    document.querySelectorAll('.nav-item').forEach(item => {
        item.classList.toggle('active', item.dataset.page === page);
    });

    // Update header
    const backBtn = document.getElementById('back-btn');
    backBtn.style.display = subId ? 'flex' : 'none';

    // Route to page
    switch (page) {
        case 'server':
            document.getElementById('header-title').textContent = 'Server';
            renderServer();
            break;
        case 'classes':
            if (subId) {
                renderClassDetail(subId, subPage || 'participants');
            } else {
                document.getElementById('header-title').textContent = 'Classes';
                renderClasses();
            }
            break;
        case 'reports':
            document.getElementById('header-title').textContent = 'Reports';
            const activityId = queryParams.get('activity');
            if (activityId) {
                renderActivityReport(activityId);
            } else {
                renderReports();
            }
            break;
        case 'activities':
            document.getElementById('header-title').textContent = 'Activities';
            renderActivities();
            break;
        case 'settings':
            document.getElementById('header-title').textContent = 'Settings';
            renderSettings(subId || 'star-levels');
            break;
        case 'account':
            document.getElementById('header-title').textContent = 'Account';
            renderAccount();
            break;
        default:
            location.hash = '#/server';
    }
}

// ===== MODAL HELPERS =====
function openModal(id) {
    document.getElementById(id).style.display = 'flex';
}

function closeModal(id) {
    document.getElementById(id).style.display = 'none';
}

// ===== TOAST =====
function showToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    container.appendChild(toast);
    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(40px)';
        toast.style.transition = 'all 0.3s ease';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

// ===== AVATAR COLORS =====
const avatarColors = ['#F97316', '#EF4444', '#0B1F1C', '#3B82F6', '#10B981', '#EC4899', '#F59E0B', '#334155', '#14B8A6', '#E11D48'];
let currentColorIndex = 0;
let editColorIndex = 0;

function cycleAvatarColor() {
    currentColorIndex = (currentColorIndex + 1) % avatarColors.length;
    document.getElementById('avatar-preview').style.backgroundColor = avatarColors[currentColorIndex];
}

function cycleEditAvatarColor() {
    editColorIndex = (editColorIndex + 1) % avatarColors.length;
    document.getElementById('edit-avatar-preview').style.backgroundColor = avatarColors[editColorIndex];
}

// ===== CLASS CRUD =====
async function handleCreateClass(e) {
    e.preventDefault();
    const name = document.getElementById('class-name-input').value;
    const code = document.getElementById('class-code-input').value.toUpperCase();
    const color = avatarColors[currentColorIndex];

    try {
        await api.createClass(name, code, color);
        closeModal('create-class-modal');
        document.getElementById('create-class-form').reset();
        showToast('Class created successfully!');
        renderClasses();
    } catch (err) {
        showToast(err.message, 'error');
    }
}

async function handleEditClass(e) {
    e.preventDefault();
    const id = document.getElementById('edit-class-id').value;
    const name = document.getElementById('edit-class-name').value;
    const code = document.getElementById('edit-class-code').value.toUpperCase();
    const color = avatarColors[editColorIndex];

    try {
        await api.updateClass(id, name, code, color);
        closeModal('edit-class-modal');
        showToast('Class updated successfully!');
        renderClasses();
    } catch (err) {
        showToast(err.message, 'error');
    }
}

function openEditClassModal(classData) {
    document.getElementById('edit-class-id').value = classData.id;
    document.getElementById('edit-class-name').value = classData.name;
    document.getElementById('edit-class-code').value = classData.code;
    document.getElementById('edit-avatar-preview').style.backgroundColor = classData.avatar_color;
    document.getElementById('edit-avatar-letter').textContent = classData.name.charAt(0).toUpperCase();
    editColorIndex = avatarColors.indexOf(classData.avatar_color);
    if (editColorIndex === -1) editColorIndex = 0;
    openModal('edit-class-modal');
}

async function confirmDeleteClass(id) {
    if (await showConfirm('Delete Class', 'Are you sure you want to delete this class? This cannot be undone.', 'Delete', true)) {
        try {
            await api.deleteClass(id);
            showToast('Class deleted');
            location.hash = '#/classes';
        } catch (err) {
            showToast(err.message, 'error');
        }
    }
}

// ===== UTILITY =====
function formatDate(dateStr) {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
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

function getActivityIcon(type) {
    const icons = {
        'multiple_choice': '📊',
        'word_cloud': '☁️',
        'short_answer': '📝',
        'fill_blanks': '📋',
        'slide_drawing': '🎨',
        'image_upload': '🖼️',
        'audio_record': '🎤',
        'video_upload': '📹'
    };
    return icons[type] || '📌';
}

// ===== CUSTOM MODALS =====

window.showConfirmModal = function(title, message, onConfirm) {
    const modal = document.getElementById('custom-confirm-modal');
    if (!modal) return;
    
    document.getElementById('confirm-title').textContent = title;
    document.getElementById('confirm-message').textContent = message;
    
    const cancelBtn = document.getElementById('btn-confirm-cancel');
    const confirmBtn = document.getElementById('btn-confirm-action');
    
    // Remove old listeners
    const newCancel = cancelBtn.cloneNode(true);
    const newConfirm = confirmBtn.cloneNode(true);
    cancelBtn.parentNode.replaceChild(newCancel, cancelBtn);
    confirmBtn.parentNode.replaceChild(newConfirm, confirmBtn);
    
    newCancel.addEventListener('click', () => {
        modal.style.display = 'none';
    });
    
    newConfirm.addEventListener('click', () => {
        modal.style.display = 'none';
        if (typeof onConfirm === 'function') {
            onConfirm();
        }
    });
    
    modal.style.display = 'flex';
};

window.showAlertModal = function(title, message) {
    const modal = document.getElementById('custom-confirm-modal');
    if (!modal) return;
    
    document.getElementById('confirm-title').textContent = title;
    document.getElementById('confirm-message').textContent = message;
    
    const cancelBtn = document.getElementById('btn-confirm-cancel');
    const confirmBtn = document.getElementById('btn-confirm-action');
    
    cancelBtn.style.display = 'none';
    
    const newConfirm = confirmBtn.cloneNode(true);
    confirmBtn.parentNode.replaceChild(newConfirm, confirmBtn);
    newConfirm.textContent = 'OK';
    
    newConfirm.addEventListener('click', () => {
        modal.style.display = 'none';
        cancelBtn.style.display = 'inline-block'; // reset for next use
        newConfirm.textContent = 'Confirm';
    });
    
    modal.style.display = 'flex';
};
