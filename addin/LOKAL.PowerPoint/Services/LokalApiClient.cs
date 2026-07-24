using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// HTTP client for the LOKAL Go backend API.
    /// Handles JWT auth, JSON serialization, and all REST endpoints.
    /// </summary>
    public class LokalApiClient
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private string _token;

        public LokalApiClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _http = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(15)
            };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void SetToken(string token)
        {
            _token = token;
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        public string GetToken() => _token;

        // ===== HTTP Helpers =====

        private async Task<T> GetAsync<T>(string path)
        {
            var res = await _http.GetAsync("/api/v1" + path);
            var json = await res.Content.ReadAsStringAsync();
            var wrapper = JsonConvert.DeserializeObject<ApiResponse<T>>(json, LokalJson.Settings);
            if (wrapper == null || !wrapper.Success)
                throw new Exception(wrapper?.Error ?? "API request failed");
            return wrapper.Data;
        }

        private async Task<T> PostAsync<T>(string path, object body)
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(body, LokalJson.Settings), Encoding.UTF8, "application/json");
            var res = await _http.PostAsync("/api/v1" + path, content);
            var json = await res.Content.ReadAsStringAsync();
            var wrapper = JsonConvert.DeserializeObject<ApiResponse<T>>(json, LokalJson.Settings);
            if (wrapper == null || !wrapper.Success)
                throw new Exception(wrapper?.Error ?? "API request failed");
            return wrapper.Data;
        }

        private async Task PostAsync(string path, object body)
        {
            var content = new StringContent(
                JsonConvert.SerializeObject(body, LokalJson.Settings), Encoding.UTF8, "application/json");
            var res = await _http.PostAsync("/api/v1" + path, content);
            var json = await res.Content.ReadAsStringAsync();
            var wrapper = JsonConvert.DeserializeObject<ApiResponse<object>>(json, LokalJson.Settings);
            if (wrapper == null || !wrapper.Success)
                throw new Exception(wrapper?.Error ?? "API request failed");
        }

        private async Task DeleteAsync(string path)
        {
            var res = await _http.DeleteAsync("/api/v1" + path);
            var json = await res.Content.ReadAsStringAsync();
            var wrapper = JsonConvert.DeserializeObject<ApiResponse<object>>(json, LokalJson.Settings);
            if (wrapper == null || !wrapper.Success)
                throw new Exception(wrapper?.Error ?? "API request failed");
        }

        // ===== Auth =====

        public async Task<AuthResponse> LoginAsync(string username, string password)
        {
            var result = await PostAsync<AuthResponse>("/auth/login",
                new { username, password, device = GetDeviceRegistration() });
            SetToken(result.Token);
            return result;
        }

        public async Task<AuthResponse> RegisterAsync(string username, string email,
            string password, string displayName)
        {
            var result = await PostAsync<AuthResponse>("/auth/register",
                new { username, email, password, display_name = displayName,
                    device = GetDeviceRegistration() });
            SetToken(result.Token);
            return result;
        }

        private static object GetDeviceRegistration()
        {
            var id = Properties.Settings.Default.DeviceId;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "ppt-" + Guid.NewGuid().ToString("N");
                Properties.Settings.Default.DeviceId = id;
                Properties.Settings.Default.Save();
            }
            return new
            {
                id,
                name = Environment.MachineName + " · PowerPoint",
                platform = "windows-powerpoint",
                user_agent = "LOKAL PowerPoint Add-in"
            };
        }

        public async Task<Teacher> GetProfileAsync()
        {
            return await GetAsync<Teacher>("/profile");
        }

        // ===== Classes =====

        public async Task<List<Class>> GetClassesAsync()
        {
            return await GetAsync<List<Class>>("/classes");
        }

        public async Task<Class> CreateClassAsync(string name, string code, string avatarColor)
        {
            return await PostAsync<Class>("/classes", new { name = name, code = code, avatar_color = avatarColor });
        }

        public async Task<Class> GetClassAsync(long id)
        {
            return await GetAsync<Class>($"/classes/{id}");
        }

        public async Task<List<Participant>> GetParticipantsAsync(long classId)
        {
            return await GetAsync<List<Participant>>($"/classes/{classId}/participants");
        }

        public async Task<List<long>> GetOnlineParticipantIdsAsync(long classId)
        {
            return await GetAsync<List<long>>($"/classes/{classId}/online-participants");
        }

        public async Task<Participant> AdjustParticipantStarsAsync(long classId, long participantId, int stars)
        {
            return await PostAsync<Participant>($"/classes/{classId}/participants/{participantId}/stars",
                new { stars });
        }

        public async Task SetClassLockedAsync(long classId, bool locked)
        {
            await PostAsync($"/classes/{classId}/lock", new { locked });
        }

        public async Task DeleteParticipantAsync(long classId, long participantId)
        {
            await DeleteAsync($"/classes/{classId}/participants/{participantId}");
        }

        public async Task<List<Participant>> GetLeaderboardAsync(long classId, long? sessionId = null)
        {
            string query = sessionId.HasValue && sessionId.Value > 0
                ? "?session_id=" + sessionId.Value
                : string.Empty;
            return await GetAsync<List<Participant>>($"/classes/{classId}/leaderboard{query}");
        }

        // ===== Sessions =====

        public async Task<Session> StartSessionAsync(long classId)
        {
            return await PostAsync<Session>("/session/start",
                new { class_id = classId });
        }

        public async Task StopSessionAsync(long sessionId, long classId)
        {
            await PostAsync("/session/stop",
                new { session_id = sessionId, class_id = classId });
        }

        // Auto-start session (generates class code automatically, no auth needed)
        public async Task<AutoSessionResponse> AutoStartSessionAsync()
        {
            return await PostAsync<AutoSessionResponse>("/session/auto-start", new { });
        }

        // ===== Activities =====

        public async Task<Activity> StartActivityAsync(StartActivityRequest req)
        {
            return await PostAsync<Activity>("/activity/start", req);
        }

        public async Task CloseActivityAsync(long activityId, long classId)
        {
            await PostAsync("/activity/close",
                new { activity_id = activityId, class_id = classId });
        }

        public async Task<List<Response>> GetResponsesAsync(long activityId)
        {
            return await GetAsync<List<Response>>($"/activities/{activityId}/responses");
        }

        public async Task DeleteSessionResponsesAsync(long sessionId)
        {
            await DeleteAsync($"/sessions/{sessionId}/responses");
        }

        public async Task<QuizSessionSummary> GetQuizSummaryAsync(long sessionId)
        {
            return await GetAsync<QuizSessionSummary>($"/sessions/{sessionId}/quiz-summary");
        }

        public async Task UploadActivitySlideAsync(long activityId, string imageBase64)
        {
            await PostAsync($"/activities/{activityId}/slide",
                new { image_base64 = imageBase64 });
        }

        public async Task UploadClassSlideAsync(long classId, string imageBase64)
        {
            await PostAsync($"/classes/{classId}/slide",
                new { image_base64 = imageBase64 });
        }

        // ===== Star Levels =====

        public async Task AwardStarsToAllAsync(long activityId, int stars = 1)
        {
            await PostAsync($"/activities/{activityId}/award-stars",
                new { stars });
        }

        public async Task AwardStarsToCorrectAsync(long activityId, int stars = 1)
        {
            await PostAsync($"/activities/{activityId}/award-stars-correct",
                new { stars });
        }
    }
}
