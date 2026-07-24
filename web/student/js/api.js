// LOKAL Student API Client
const API_BASE = '/api/v1';

const studentApi = {
    token: '',

    setToken(token) {
        this.token = token || '';
    },

    async request(method, path, body = null) {
        const headers = { 'Content-Type': 'application/json' };
        if (this.token) headers.Authorization = `Bearer ${this.token}`;
        const options = { method, headers };
        if (body) options.body = JSON.stringify(body);

        const res = await fetch(API_BASE + path, options);
        const raw = await res.text();
        let data = {};
        try {
            data = raw ? JSON.parse(raw) : {};
        } catch (_) {}
        if (!res.ok) {
            const error = new Error(data.error || 'Request failed');
            error.status = res.status;
            throw error;
        }
        return data;
    },

    async getClassByCode(code) {
        const data = await this.request('GET', `/student/class/${code}`);
        return data.data;
    },

    async getClassState(code) {
        const data = await this.request('GET', `/student/class/${code}/state`);
        return data.data;
    },

    async joinClass(classCode, name, deviceID, avatar = '') {
        const data = await this.request('POST', '/student/join', {
            class_code: classCode, name, device_id: deviceID, avatar: avatar
        });
        return data.data;
    },

    async submitResponse(activityId, participantId, answer, responseTimeMs) {
        const data = await this.request('POST', '/student/submit', {
            activity_id: activityId,
            participant_id: participantId,
            answer,
            response_time_ms: responseTimeMs
        });
        return data.data;
    }
};
