// LOKAL API Client
const API_BASE = '/api/v1';

function getTeacherDeviceRegistration() {
    let deviceId = localStorage.getItem('lokal_teacher_device_id');
    if (!deviceId) {
        deviceId = (globalThis.crypto && typeof globalThis.crypto.randomUUID === 'function')
            ? `web_${globalThis.crypto.randomUUID()}`
            : `web_${Date.now()}_${Math.random().toString(36).slice(2)}`;
        localStorage.setItem('lokal_teacher_device_id', deviceId);
    }
    const platform = navigator.userAgentData?.platform || navigator.platform || 'web';
    return {
        id: deviceId,
        name: `${platform} browser`,
        platform,
        user_agent: navigator.userAgent
    };
}

const api = {
    token: localStorage.getItem('lokal_token'),
    teacher: JSON.parse(localStorage.getItem('lokal_teacher') || 'null'),

    setAuth(token, teacher) {
        this.token = token;
        this.teacher = teacher;
        localStorage.setItem('lokal_token', token);
        localStorage.setItem('lokal_teacher', JSON.stringify(teacher));
    },

    clearAuth() {
        this.token = null;
        this.teacher = null;
        localStorage.removeItem('lokal_token');
        localStorage.removeItem('lokal_teacher');
    },

    isAuthenticated() {
        return !!this.token;
    },

    async request(method, path, body = null) {
        const headers = { 'Content-Type': 'application/json' };
        if (this.token) {
            headers['Authorization'] = `Bearer ${this.token}`;
        }

        const options = { method, headers };
        if (body) {
            options.body = JSON.stringify(body);
        }

        const res = await fetch(API_BASE + path, options);
        const raw = await res.text();
        let data = {};
        if (raw) {
            try {
                data = JSON.parse(raw);
            } catch (error) {
                const contentType = res.headers.get('content-type') || '';
                const hint = contentType.includes('application/json')
                    ? 'The server returned malformed JSON.'
                    : 'The running lokal.exe may be outdated or this API route is unavailable.';
                throw new Error(`${hint} Rebuild and restart lokal.exe, then try again.`);
            }
        }

        if (!res.ok) {
            if (res.status === 401 && !path.includes('/auth/login')) {
                this.clearAuth();
                window.location.href = 'login.html';
                return new Promise(() => {}); // Halt execution during redirect
            }
            throw new Error(data.error || 'Request failed');
        }
        return data;
    },

    // Auth
    async login(username, password) {
        const data = await this.request('POST', '/auth/login', {
            username, password, device: getTeacherDeviceRegistration()
        });
        this.setAuth(data.data.token, data.data.teacher);
        return data.data;
    },

    async register(username, email, password, displayName) {
        const data = await this.request('POST', '/auth/register', {
            username, email, password, display_name: displayName,
            device: getTeacherDeviceRegistration()
        });
        this.setAuth(data.data.token, data.data.teacher);
        return data.data;
    },

    async getProfile() {
        const data = await this.request('GET', '/profile');
        return data.data;
    },

    async logout() {
        if (this.token) await this.request('POST', '/auth/logout');
        this.clearAuth();
    },

    async getRegisteredDevices() {
        const data = await this.request('GET', '/auth/devices');
        return data.data;
    },

    async revokeRegisteredDevice(deviceId) {
        const data = await this.request('DELETE', `/auth/devices/${deviceId}`);
        return data.data;
    },

    async updateProfile(profile) {
        const data = await this.request('PUT', '/profile', profile);
        this.teacher = data.data;
        localStorage.setItem('lokal_teacher', JSON.stringify(data.data));
        return data.data;
    },

    // Classes
    async getClasses() {
        const data = await this.request('GET', '/classes');
        return data.data;
    },

    async getClass(id) {
        const data = await this.request('GET', `/classes/${id}`);
        return data.data;
    },

    async createClass(name, code, avatarColor) {
        const data = await this.request('POST', '/classes', {
            name, code, avatar_color: avatarColor
        });
        return data.data;
    },

    async updateClass(id, name, code, avatarColor) {
        const data = await this.request('PUT', `/classes/${id}`, {
            name, code, avatar_color: avatarColor
        });
        return data.data;
    },

    async deleteClass(id) {
        return this.request('DELETE', `/classes/${id}`);
    },

    async getParticipants(classId) {
        const data = await this.request('GET', `/classes/${classId}/participants`);
        return data.data;
    },

    async addParticipant(classId, name) {
        const data = await this.request('POST', `/classes/${classId}/participants`, { name });
        return data.data;
    },

    async updateParticipant(classId, participantId, name, stars, avatarUrl) {
        const data = await this.request('PUT', `/classes/${classId}/participants/${participantId}`, { name, stars, avatar_url: avatarUrl });
        return data.data;
    },

    async deleteParticipant(classId, participantId) {
        const data = await this.request('DELETE', `/classes/${classId}/participants/${participantId}`);
        return data.data;
    },

    async adjustParticipantStars(classId, participantId, starsAmount) {
        const data = await this.request('POST', `/classes/${classId}/participants/${participantId}/stars`, { stars: starsAmount });
        return data.data;
    },

    async getGroups(classId) {
        const data = await this.request('GET', `/classes/${classId}/groups`);
        return data.data;
    },

    async createGroup(classId, name, color) {
        const data = await this.request('POST', `/classes/${classId}/groups`, { name, color });
        return data.data;
    },

    async updateGroup(classId, groupId, name, color) {
        return this.request('PUT', `/classes/${classId}/groups/${groupId}`, { name, color });
    },

    async deleteGroup(classId, groupId) {
        return this.request('DELETE', `/classes/${classId}/groups/${groupId}`);
    },

    async setParticipantGroup(classId, participantId, groupId) {
        return this.request('PUT', `/classes/${classId}/participants/${participantId}/group`, {
            group_id: Number(groupId) || 0
        });
    },

    async resetStars(classId) {
        return this.request('POST', `/classes/${classId}/reset-stars`);
    },

    async getLeaderboard(classId) {
        const data = await this.request('GET', `/classes/${classId}/leaderboard`);
        return data.data;
    },

    async getClassReports(classId) {
        const data = await this.request('GET', `/classes/${classId}/reports`);
        return data.data;
    },

    // Reports
    async getReports() {
        const data = await this.request('GET', '/reports');
        return data.data;
    },

    async getReportDetails(sessionId) {
        const data = await this.request('GET', `/reports/${sessionId}`);
        return data.data;
    },

    async toggleFavoriteReport(sessionId) {
        const data = await this.request('POST', `/reports/${sessionId}/favorite`);
        return data.data; // { is_favorite: bool }
    },

    async deleteReport(sessionId) {
        return this.request('DELETE', `/reports/${sessionId}`);
    },

    // Activities
    async getActivities(type = '') {
        const query = type && type !== 'all' ? `?type=${type}` : '';
        const data = await this.request('GET', `/activities${query}`);
        return data.data;
    },

    async getActivity(id) {
        const data = await this.request('GET', `/activities/${id}`);
        return data.data;
    },

    async deleteActivity(id) {
        return this.request('DELETE', `/activities/${id}`);
    },

    async getResponses(activityId) {
        const data = await this.request('GET', `/activities/${activityId}/responses`);
        return data.data;
    },

    async awardStarsToAll(activityId, stars = 1) {
        return this.request('POST', `/activities/${activityId}/award-stars`, { stars });
    },

    async toggleActivityFavorite(activityId) {
        const data = await this.request('POST', `/activities/${activityId}/favorite`);
        return data.data;
    },

    // Settings
    async getStarLevels() {
        const data = await this.request('GET', '/settings/star-levels');
        return data.data;
    },

    async updateStarLevels(levels) {
        const data = await this.request('PUT', '/settings/star-levels', levels);
        return data.data;
    },

    // Server configuration and operating mode
    async getServerStatus() {
        const data = await this.request('GET', '/server/status');
        return data.data;
    },

    async getServerConfig() {
        const data = await this.request('GET', '/server/config');
        return data.data;
    },

    async updateServerConfig(config) {
        const data = await this.request('PUT', '/server/config', config);
        return data.data;
    },

    async getSyncStatus() {
        const data = await this.request('GET', '/sync/status');
        return data.data;
    },

    async getRelayStatus() {
        const data = await this.request('GET', '/relay/status');
        return data.data;
    },

    async runSync() {
        const data = await this.request('POST', '/sync/run');
        return data.data;
    },

    // Session
    async startSession(classId) {
        const data = await this.request('POST', '/session/start', { class_id: classId });
        return data.data;
    },

    async stopSession(sessionId, classId) {
        return this.request('POST', '/session/stop', { session_id: sessionId, class_id: classId });
    },

    async startActivity(req) {
        const data = await this.request('POST', '/activity/start', req);
        return data.data;
    },

    async closeActivity(activityId, classId) {
        const data = await this.request('POST', '/activity/close', {
            activity_id: activityId, class_id: classId
        });
        return data.data;
    }
};
