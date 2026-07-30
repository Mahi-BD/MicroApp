using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MicroApp
{
    /// <summary>
    /// The Grammar button's back end: sends the note to the configured AI service and
    /// returns the corrected text. Three providers, all plain HTTPS + JSON: MiMo and
    /// ChatGPT speak the OpenAI chat format, Gemini has its own. Keys and the model
    /// name live in Note Setting.
    /// </summary>
    public static class NoteAi
    {
        public const int ProviderMiMo = 0;
        public const int ProviderGemini = 1;
        public const int ProviderChatGpt = 2;
        public const int ProviderOpenRouter = 3;

        /// <summary>MiMo Token Plan endpoint; the console hands each subscriber their own regional URL.</summary>
        public const string DefaultMiMoBaseUrl = "https://token-plan-sgp.xiaomimimo.com/v1";

        public static string DefaultModel(int provider)
        {
            switch (provider)
            {
                case ProviderGemini: return "gemini-2.0-flash";
                case ProviderChatGpt: return "gpt-4o-mini";
                case ProviderOpenRouter: return "openai/gpt-4o-mini";
                default: return "mimo-v2.5";
            }
        }

        /// <summary>The configured MiMo base URL, falling back to the stock Token Plan one.</summary>
        public static string MiMoBaseUrl()
        {
            string url = (Properties.Settings.Default.NoteAiBaseUrl ?? "").Trim().TrimEnd('/');
            return url.Length > 0 ? url : DefaultMiMoBaseUrl;
        }

        private const string Prompt =
            "Fix all spelling and grammar mistakes in the text below. It may be English, " +
            "Bangla (Bengali), or a mix; keep the original language, meaning and line breaks. " +
            "Reply with only the corrected text - no explanations, no quotes, no markdown.";

        /// <summary>Blocking; run it off the UI thread. Throws with a readable message on failure.</summary>
        public static string CorrectGrammar(string text)
        {
            return Chat(Prompt, text);
        }

        /// <summary>
        /// The instruction box under the note: reshapes the whole note per the user's
        /// direction ("rewrite as a Facebook post", "make it formal", ...). Blocking.
        /// </summary>
        public static string Apply(string text, string instruction)
        {
            string prompt =
                "Apply this instruction to the text below: \"" + instruction + "\". " +
                "The text may be English, Bangla (Bengali), or a mix; keep whichever language fits the request. " +
                "Reply with only the resulting text - no explanations, no quotes, no markdown.";
            return Chat(prompt, text);
        }

        /// <summary>
        /// One Bangla word to English options. The string.bd dictionary only translates
        /// English to Bangla, so the reverse direction goes through the AI. Blocking.
        /// </summary>
        public static List<string> BanglaToEnglish(string word, int limit)
        {
            string reply = Chat(
                "Give the " + limit + " most common English translations of the Bangla word the user sends. " +
                "Reply with only the English words, separated by commas - no numbering, no explanations.",
                word);

            var options = new List<string>();
            foreach (string part in reply.Split(','))
            {
                string option = part.Trim().Trim('.', '"', '\'');
                if (option.Length > 0 && option.Length < 40 && !options.Contains(option)) options.Add(option);
                if (options.Count >= limit) break;
            }
            return options;
        }

        private static string Chat(string prompt, string text)
        {
            int provider = Properties.Settings.Default.NoteAiProvider;
            string key = (Properties.Settings.Default.NoteAiApiKey ?? "").Trim();
            if (key.Length == 0) throw new Exception("No API key is set. Add one in Note Setting.");
            string model = (Properties.Settings.Default.NoteAiModel ?? "").Trim();
            if (model.Length == 0) model = DefaultModel(provider);
            if (provider == ProviderMiMo && model == "mimo") model = DefaultModel(provider);   // pre-V2.5 default

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            string result;
            if (provider == ProviderGemini)
            {
                string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                             Uri.EscapeDataString(model) + ":generateContent?key=" + Uri.EscapeDataString(key);
                string body = "{\"contents\":[{\"parts\":[{\"text\":\"" +
                              Json.Escape(prompt + "\n\n" + text) + "\"}]}]}";
                object root = Json.Parse(Post(url, body, null));
                result = Json.Dig(root, "candidates", 0, "content", "parts", 0, "text") as string;
            }
            else
            {
                string url = provider == ProviderChatGpt
                    ? "https://api.openai.com/v1/chat/completions"
                    : provider == ProviderOpenRouter
                        ? "https://openrouter.ai/api/v1/chat/completions"
                        : MiMoBaseUrl() + "/chat/completions";
                string body = "{\"model\":\"" + Json.Escape(model) + "\",\"temperature\":0.2,\"messages\":[" +
                              "{\"role\":\"system\",\"content\":\"" + Json.Escape(prompt) + "\"}," +
                              "{\"role\":\"user\",\"content\":\"" + Json.Escape(text) + "\"}]}";
                object root = Json.Parse(Post(url, body, key));
                result = Json.Dig(root, "choices", 0, "message", "content") as string;
            }

            if (result == null) throw new Exception("The AI service sent a reply in an unexpected shape.");
            return result.Trim();
        }

        private static string Post(string url, string body, string bearer)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = 90000;
            request.ReadWriteTimeout = 90000;
            if (bearer != null) request.Headers["Authorization"] = "Bearer " + bearer;

            byte[] payload = Encoding.UTF8.GetBytes(body);
            using (var stream = request.GetRequestStream())
            {
                stream.Write(payload, 0, payload.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                string detail = null;
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        detail = reader.ReadToEnd();
                    }
                }
                if (!string.IsNullOrEmpty(detail))
                {
                    // most services wrap the reason as {"error":{"message":...}}
                    object root = null;
                    try { root = Json.Parse(detail); } catch (Exception) { }
                    string message = Json.Dig(root, "error", "message") as string;
                    if (message == null && detail.Length > 300) detail = detail.Substring(0, 300);
                    throw new Exception(message ?? detail);
                }
                throw new Exception(ex.Message);
            }
        }
    }

    public struct BanglaSuggestion
    {
        public string Phonetic;
        public string Bangla;
    }

    /// <summary>
    /// string.bd Suggestions API: phonetic text in ("ami"), ranked Bangla candidates out.
    /// Bearer token comes from Note Setting; results are cached per query for the session.
    /// </summary>
    public static class BanglaPhonetic
    {
        private static readonly Dictionary<string, List<BanglaSuggestion>> Cache =
            new Dictionary<string, List<BanglaSuggestion>>(StringComparer.Ordinal);

        public static bool HasToken
        {
            get { return (Properties.Settings.Default.NoteBanglaToken ?? "").Trim().Length > 0; }
        }

        /// <summary>
        /// English word to Bangla, from the string.bd dictionary
        /// (/api/dictionary/translate). Blocking; empty list on failure.
        /// </summary>
        public static List<string> Translate(string english, int limit)
        {
            var results = new List<string>();
            foreach (object item in GetList("/api/dictionary/translate?q=" +
                                            Uri.EscapeDataString(english) + "&limit=" + limit, "results"))
            {
                string bangla = Json.Dig(item, "bangla") as string;
                if (!string.IsNullOrEmpty(bangla) && !results.Contains(bangla)) results.Add(bangla);
            }
            return results;
        }

        private static List<object> GetList(string path, string field)
        {
            string token = (Properties.Settings.Default.NoteBanglaToken ?? "").Trim();
            if (token.Length == 0) return new List<object>();

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create("https://string.bd" + path);
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.Headers["Authorization"] = "Bearer " + token;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    return (Json.Dig(Json.Parse(reader.ReadToEnd()), field) as List<object>) ?? new List<object>();
                }
            }
            catch (Exception) { return new List<object>(); }
        }

        /// <summary>Already-known answer for a word, so a fast typist's space can still convert it.</summary>
        public static bool TryCached(string word, out List<BanglaSuggestion> items)
        {
            lock (Cache) { return Cache.TryGetValue(word, out items) && items.Count > 0; }
        }

        /// <summary>Blocking; run it off the UI thread. Empty list (never null) when nothing matches or the call fails.</summary>
        public static List<BanglaSuggestion> Suggest(string word, int limit)
        {
            List<BanglaSuggestion> cached;
            lock (Cache) { if (Cache.TryGetValue(word, out cached)) return cached; }

            var results = new List<BanglaSuggestion>();
            string token = (Properties.Settings.Default.NoteBanglaToken ?? "").Trim();
            if (token.Length == 0) return results;

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(
                "https://string.bd/api/suggestions?q=" + Uri.EscapeDataString(word) + "&limit=" + limit);
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.Headers["Authorization"] = "Bearer " + token;
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    object root = Json.Parse(reader.ReadToEnd());
                    var list = Json.Dig(root, "suggestions") as List<object>;
                    if (list != null)
                    {
                        foreach (object item in list)
                        {
                            string bangla = Json.Dig(item, "bangla") as string;
                            if (string.IsNullOrEmpty(bangla)) continue;
                            results.Add(new BanglaSuggestion
                            {
                                Bangla = bangla,
                                Phonetic = (Json.Dig(item, "phonetic") as string) ?? ""
                            });
                        }
                    }
                }
                lock (Cache) { Cache[word] = results; }
            }
            catch (Exception) { }   // typing must never break on a network hiccup
            return results;
        }
    }

    /// <summary>
    /// Just enough JSON for the AI replies: Parse returns nested
    /// Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null,
    /// Dig walks that tree, Escape makes a string safe inside a JSON literal.
    /// </summary>
    public static class Json
    {
        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Walks a parsed tree: strings index objects, ints index arrays; null when any step misses.</summary>
        public static object Dig(object node, params object[] path)
        {
            foreach (object step in path)
            {
                if (node == null) return null;
                var key = step as string;
                if (key != null)
                {
                    var map = node as Dictionary<string, object>;
                    if (map == null || !map.TryGetValue(key, out node)) return null;
                }
                else
                {
                    var list = node as List<object>;
                    int index = (int)step;
                    if (list == null || index < 0 || index >= list.Count) return null;
                    node = list[index];
                }
            }
            return node;
        }

        public static object Parse(string text)
        {
            int pos = 0;
            object value = ParseValue(text, ref pos);
            SkipWhite(text, ref pos);
            return value;
        }

        private static object ParseValue(string s, ref int pos)
        {
            SkipWhite(s, ref pos);
            if (pos >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[pos];
            if (c == '{') return ParseObject(s, ref pos);
            if (c == '[') return ParseArray(s, ref pos);
            if (c == '"') return ParseString(s, ref pos);
            if (c == 't') { Expect(s, ref pos, "true"); return true; }
            if (c == 'f') { Expect(s, ref pos, "false"); return false; }
            if (c == 'n') { Expect(s, ref pos, "null"); return null; }
            return ParseNumber(s, ref pos);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int pos)
        {
            var map = new Dictionary<string, object>();
            pos++;                                       // '{'
            SkipWhite(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; return map; }
            while (true)
            {
                SkipWhite(s, ref pos);
                string key = ParseString(s, ref pos);
                SkipWhite(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw new FormatException("Expected ':' in JSON object.");
                pos++;
                map[key] = ParseValue(s, ref pos);
                SkipWhite(s, ref pos);
                if (pos >= s.Length) throw new FormatException("Unterminated JSON object.");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return map; }
                throw new FormatException("Expected ',' or '}' in JSON object.");
            }
        }

        private static List<object> ParseArray(string s, ref int pos)
        {
            var list = new List<object>();
            pos++;                                       // '['
            SkipWhite(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref pos));
                SkipWhite(s, ref pos);
                if (pos >= s.Length) throw new FormatException("Unterminated JSON array.");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return list; }
                throw new FormatException("Expected ',' or ']' in JSON array.");
            }
        }

        private static string ParseString(string s, ref int pos)
        {
            if (pos >= s.Length || s[pos] != '"') throw new FormatException("Expected a JSON string.");
            pos++;
            var sb = new StringBuilder();
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= s.Length) break;
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length) throw new FormatException("Bad \\u escape in JSON.");
                        sb.Append((char)int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        pos += 4;
                        break;
                    default: throw new FormatException("Bad escape in JSON string.");
                }
            }
            throw new FormatException("Unterminated JSON string.");
        }

        private static double ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && ("+-0123456789.eE".IndexOf(s[pos]) >= 0)) pos++;
            double value;
            if (!double.TryParse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new FormatException("Bad JSON number.");
            return value;
        }

        private static void Expect(string s, ref int pos, string token)
        {
            if (pos + token.Length > s.Length || s.Substring(pos, token.Length) != token)
                throw new FormatException("Bad JSON literal.");
            pos += token.Length;
        }

        private static void SkipWhite(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n')) pos++;
        }
    }
}
