using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// Shared JSON settings — the Go backend speaks snake_case
    /// (class_code, session_id), so every serialize/deserialize
    /// must use this or fields silently come back null/0.
    /// </summary>
    public static class LokalJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new SnakeCaseNamingStrategy()
            },
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    /// <summary>
    /// The API stores activity config as JSON. Older add-in builds sent it as a
    /// quoted string, while current API responses correctly return an object.
    /// Keep the add-in model string-based for its existing consumers, but accept
    /// both wire formats so opening responses never fails during deserialization.
    /// </summary>
    public sealed class JsonValueStringConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) { return objectType == typeof(string); }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType == JsonToken.String) return reader.Value == null ? null : reader.Value.ToString();

            JToken token = JToken.Load(reader);
            return token.ToString(Formatting.None);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            string json = value as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                writer.WriteNull();
                return;
            }

            try { JToken.Parse(json).WriteTo(writer); }
            catch (JsonReaderException) { writer.WriteValue(json); }
        }
    }

    // ===== API Response Wrapper =====
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }
    }

    // ===== Auth =====
    public class AuthResponse
    {
        public string Token { get; set; }
        public Teacher Teacher { get; set; }
    }

    public class Teacher
    {
        public long Id { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ===== Class =====
    public class Class
    {
        public long Id { get; set; }
        public long TeacherId { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string AvatarColor { get; set; }
        public bool IsLocked { get; set; }
        public int MaxParticipants { get; set; }
        public DateTime CreatedAt { get; set; }
        public int ParticipantCount { get; set; }
        public int GroupCount { get; set; }
    }

    // ===== Participant =====
    public class Participant
    {
        public long Id { get; set; }
        public long ClassId { get; set; }
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public string AvatarUrl { get; set; }
        public int TotalStars { get; set; }
        public int SessionStars { get; set; }
        public long SessionResponseTimeMs { get; set; }
        public int Level { get; set; }
        public DateTime JoinedAt { get; set; }
    }

    // ===== Session =====
    public class Session
    {
        public long Id { get; set; }
        public long ClassId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public bool IsActive { get; set; }
    }

    // ===== Activity =====
    public class Activity
    {
        public long Id { get; set; }
        public long SessionId { get; set; }
        public long ClassId { get; set; }
        public string Type { get; set; }
        public string QuestionText { get; set; }
        [JsonConverter(typeof(JsonValueStringConverter))]
        public string Config { get; set; }
        public bool IsQuizMode { get; set; }
        public int AutoCloseSeconds { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public int ResponseCount { get; set; }
        public string ClassName { get; set; }
    }

    public class Response
    {
        public long Id { get; set; }
        public long ActivityId { get; set; }
        public long ParticipantId { get; set; }
        public object Answer { get; set; }
        public bool? IsCorrect { get; set; }
        public int StarsEarned { get; set; }
        public long ResponseTimeMs { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string ParticipantName { get; set; }
    }

    public class QuizSummaryRow
    {
        public long ParticipantId { get; set; }
        public string Name { get; set; }
        public int SubmittedCount { get; set; }
        public int CorrectCount { get; set; }
        public int StarsEarned { get; set; }
        public double AverageTimeMs { get; set; }
    }

    public class QuizSessionSummary
    {
        public long SessionId { get; set; }
        public int QuestionCount { get; set; }
        public List<QuizSummaryRow> Rows { get; set; }
    }

    // ===== Start Activity Request =====
    public class StartActivityRequest
    {
        public long SessionId { get; set; }
        public long ClassId { get; set; }
        public string Type { get; set; }
        public string QuestionText { get; set; }
        public string Config { get; set; }
        public bool IsQuizMode { get; set; }
        public int AutoCloseSeconds { get; set; }
    }

    // ===== WebSocket Message =====
    public class WsMessage
    {
        public string Type { get; set; }
        public object Payload { get; set; }
    }

    // ===== Auto Session Response =====
    public class AutoSessionResponse
    {
        public string ClassCode { get; set; }
        public long ClassId { get; set; }
        public long SessionId { get; set; }
        public string Token { get; set; }
        public string JoinUrl { get; set; }
    }
}
