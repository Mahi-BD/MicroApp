using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MicroApp
{
    /// <summary>
    /// Note sync across PCs. The .txt files on disk stay the source of truth and
    /// everything keeps working offline; this mirrors them into a Firebase project so
    /// the same address and password typed into Note Setting on another PC brings the
    /// same notes down.
    ///
    /// The Firebase project is the user's own - project ID and Web API key are typed
    /// into Note Setting, nothing is baked into the app. That means the notes sit in
    /// an account the user controls, and MicroApp never sees them.
    ///
    /// One document per note at users/{uid}/notes/{file name}. The security rules only
    /// let a signed-in user read or write their own uid, so the API key is a public
    /// identifier, not a secret - it is the password that guards the notes.
    ///
    /// Newest wins: both sides carry the note's last-write time in Unix milliseconds
    /// and the later one is copied over the older. Deletes leave a tombstone so the
    /// other PC removes its copy instead of pushing it back up.
    /// </summary>
    public static class NoteCloud
    {
        /// <summary>Two writes this close together are the same edit, not a conflict.</summary>
        private const long SkewMs = 2000;

        /// <summary>
        /// How often the other PCs are checked. Every tick reads one small document - the
        /// pulse - and only goes on to read the notes themselves when that says something
        /// actually changed, so checking this often still costs a few thousand reads a day
        /// however many notes there are.
        /// </summary>
        private static readonly TimeSpan Poll = TimeSpan.FromSeconds(15);

        /// <summary>A change made here goes up almost at once rather than at the next tick.</summary>
        private static readonly TimeSpan Soon = TimeSpan.FromSeconds(3);

        /// <summary>The rules the user pastes into their own project, shown by the setup guide.</summary>
        public const string Rules =
            "rules_version = '2';\r\n" +
            "service cloud.firestore {\r\n" +
            "  match /databases/{database}/documents {\r\n" +
            "    match /users/{uid}/{doc=**} {\r\n" +
            "      allow read, write: if request.auth != null && request.auth.uid == uid;\r\n" +
            "    }\r\n" +
            "  }\r\n" +
            "}\r\n";

        public static string ProjectId { get { return (Properties.Settings.Default.NoteSyncProject ?? "").Trim(); } }

        public static string ApiKey { get { return (Properties.Settings.Default.NoteSyncApiKey ?? "").Trim(); } }

        /// <summary>True once the user has pointed the app at a Firebase project of their own.</summary>
        public static bool IsConfigured { get { return ProjectId.Length > 0 && ApiKey.Length > 0; } }

        private static string SignUpUrl
        {
            get { return "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + Uri.EscapeDataString(ApiKey); }
        }

        private static string SignInUrl
        {
            get { return "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + Uri.EscapeDataString(ApiKey); }
        }

        private static string RefreshUrl
        {
            get { return "https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(ApiKey); }
        }

        private static string Documents
        {
            get
            {
                return "https://firestore.googleapis.com/v1/projects/" + Uri.EscapeDataString(ProjectId) +
                       "/databases/(default)/documents";
            }
        }

        private static readonly object Gate = new object();
        private static string _idToken;
        private static DateTime _tokenDies = DateTime.MinValue;   // UTC
        private static System.Threading.Timer _timer;
        private static Control _ui;                               // marshals callbacks onto the UI thread
        private static bool _busy;
        private static bool _dirty;                               // something changed here since the last push
        private static long _lastPulse;                           // the newest change any PC has announced
        private static long _skew;                                // server clock minus this PC's clock, ms
        private static string _lastLogged = "";                   // so a quiet tick does not repeat itself

        /// <summary>The one line Note Setting shows under the sign-in box.</summary>
        public static string Status = "";

        /// <summary>Raised on the UI thread when a sync changed something on disk.</summary>
        public static event Action Pulled;

        public static bool IsOn
        {
            get
            {
                return Properties.Settings.Default.NoteSyncOn && IsConfigured &&
                       (Properties.Settings.Default.NoteSyncEmail ?? "").Trim().Length > 0;
            }
        }

        public static string Account { get { return (Properties.Settings.Default.NoteSyncEmail ?? "").Trim(); } }

        /// <summary>
        /// Now, on the database's clock rather than this PC's. Newest-wins compares stamps
        /// written by different machines, so a PC whose clock is hours out would otherwise
        /// win every conflict and have its stale copies overwrite everyone else's edits.
        /// The offset is learned from the reply to each write; until the first one it is
        /// zero, which is simply this PC's own clock.
        /// </summary>
        public static long NowServer { get { return DateTime.UtcNow.ToMs() + _skew; } }

        private static long ToServer(long localMs) { return localMs + _skew; }

        private static long ToLocal(long serverMs) { return serverMs - _skew; }

        /// <summary>Every write comes back stamped by the server, which is where the offset comes from.</summary>
        private static void LearnClock(string reply)
        {
            try
            {
                string when = Json.Dig(Json.Parse(reply), "updateTime") as string;
                if (when == null) return;
                DateTime server = DateTime.Parse(when, CultureInfo.InvariantCulture,
                                                 DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                _skew = server.ToMs() - DateTime.UtcNow.ToMs();
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- sign in

        /// <summary>Marks a sync code so a pasted one can be told apart from any other text.</summary>
        private const string CodeTag = "MASYNC1-";

        /// <summary>
        /// Everything another PC needs to join: which project, and who to sign in as.
        /// The user never types any of it - they copy this one string across.
        /// </summary>
        public static string Code()
        {
            string password = Unprotect(Properties.Settings.Default.NoteSyncSecret);
            if (!IsConfigured || Account.Length == 0 || password.Length == 0) return "";
            string plain = ProjectId + "\n" + ApiKey + "\n" + Account + "\n" + password;
            return CodeTag + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain))
                                    .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// First PC: makes its own account in the user's project so nobody has to invent
        /// an address or a password. Blocking. Null on success, else a message.
        /// </summary>
        public static string StartFresh(string project, string apiKey)
        {
            project = (project ?? "").Trim();
            apiKey = (apiKey ?? "").Trim();
            if (project.Length == 0 || apiKey.Length == 0)
                return "Fill in the project ID and the Web API key from your Firebase project.";

            // a name nobody has to remember, on a domain that can never receive mail
            string email = "n" + Random(8) + "@microapp.invalid";
            return SignIn(project, apiKey, email, Random(16), true);
        }

        /// <summary>Any PC after the first: joins whatever the code points at. Blocking.</summary>
        public static string Join(string code)
        {
            var parts = Decode(code);
            if (parts == null)
                return "That does not look like a sync code. Copy the whole thing from the first PC.";
            return SignIn(parts[0], parts[1], parts[2], parts[3], false);
        }

        private static string[] Decode(string code)
        {
            try
            {
                var trimmed = new StringBuilder();
                foreach (char c in (code ?? "")) if (!char.IsWhiteSpace(c)) trimmed.Append(c);
                string text = trimmed.ToString();
                if (text.StartsWith(CodeTag, StringComparison.OrdinalIgnoreCase)) text = text.Substring(CodeTag.Length);
                text = text.Replace('-', '+').Replace('_', '/');
                while (text.Length % 4 != 0) text += "=";
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(text)).Split('\n');
                return parts.Length == 4 && parts[0].Length > 0 && parts[1].Length > 0 ? parts : null;
            }
            catch (Exception) { return null; }
        }

        private static string SignIn(string project, string apiKey, string email, string password, bool create)
        {
            // the sign-in below reads these back out of settings, so they go in first
            var settings = Properties.Settings.Default;
            string wasProject = settings.NoteSyncProject, wasKey = settings.NoteSyncApiKey;
            settings.NoteSyncProject = project;
            settings.NoteSyncApiKey = apiKey;

            string body = "{\"email\":\"" + Json.Escape(email) + "\",\"password\":\"" +
                          Json.Escape(password) + "\",\"returnSecureToken\":true}";
            object reply;
            try
            {
                reply = Json.Parse(Post(create ? SignUpUrl : SignInUrl, body, null));
            }
            catch (CloudException ex)
            {
                settings.NoteSyncProject = wasProject;
                settings.NoteSyncApiKey = wasKey;
                return Explain(ex.Code);
            }
            catch (Exception other)
            {
                settings.NoteSyncProject = wasProject;
                settings.NoteSyncApiKey = wasKey;
                return other.Message;
            }

            string uid = Json.Dig(reply, "localId") as string;
            string refresh = Json.Dig(reply, "refreshToken") as string;
            if (uid == null || refresh == null) return "The sign-in service replied in an unexpected shape.";

            lock (Gate)
            {
                _idToken = Json.Dig(reply, "idToken") as string;
                _tokenDies = DateTime.UtcNow.AddMinutes(50);
            }

            settings.NoteSyncEmail = email;
            settings.NoteSyncUid = uid;
            settings.NoteSyncToken = Protect(refresh);
            settings.NoteSyncSecret = Protect(password);
            settings.NoteSyncOn = true;
            settings.Save();

            Status = "Connected. First sync running...";
            Nudge();
            return null;
        }

        /// <summary>Letters and digits only, so a code stays readable if someone retypes it.</summary>
        private static string Random(int bytes)
        {
            var raw = new byte[bytes];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(raw);
            const string Alphabet = "abcdefghijkmnopqrstuvwxyz23456789";   // no l, no 0/1
            var text = new StringBuilder(bytes * 2);
            foreach (byte b in raw)
            {
                text.Append(Alphabet[b & 31]);
                text.Append(Alphabet[(b >> 3) & 31]);
            }
            return text.ToString();
        }

        /// <summary>Stops syncing on this PC. The notes stay where they are, here and in the cloud.</summary>
        public static void Disconnect()
        {
            var settings = Properties.Settings.Default;
            settings.NoteSyncOn = false;
            settings.NoteSyncToken = "";
            settings.NoteSyncSecret = "";
            settings.NoteSyncUid = "";
            settings.Save();
            lock (Gate) { _idToken = null; _tokenDies = DateTime.MinValue; }
            Status = "Not connected.";
        }

        /// <summary>
        /// Turns the service's error codes into something that says which setup step is
        /// missing - most first-time failures are a step of the guide not done yet.
        /// </summary>
        private static string Explain(string code)
        {
            switch (code)
            {
                case "INVALID_LOGIN_CREDENTIALS":
                case "INVALID_PASSWORD":
                    return "That password does not match the one this address was set up with.";
                case "INVALID_EMAIL": return "That does not look like an email address.";
                case "WEAK_PASSWORD": return "The password needs to be at least 6 characters.";
                case "TOO_MANY_ATTEMPTS_TRY_LATER": return "Too many attempts. Wait a few minutes and try again.";
                case "USER_DISABLED": return "That account has been disabled.";
                case "OPERATION_NOT_ALLOWED":
                    return "Email/Password sign-in is off in your Firebase project. Turn it on under " +
                           "Authentication, Sign-in method (guide step 4).";
                case "API_KEY_INVALID":
                case "API":
                    return "That Web API key is not valid for any Firebase project. Copy it again from " +
                           "Project settings, General (guide step 5).";
                default: return code;
            }
        }

        /// <summary>Same idea for the Firestore side, where the usual miss is the rules or the database itself.</summary>
        private static string ExplainFirestore(string message)
        {
            if (message.IndexOf("Missing or insufficient permissions", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Your Firestore rules are blocking MicroApp. Paste the rules from the Setup guide " +
                       "and publish them (guide step 3).";
            if (message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
                return "That project has no Firestore database yet. Create one in Native mode (guide step 2).";
            if (message.IndexOf("has not been used", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("is disabled", StringComparison.OrdinalIgnoreCase) >= 0)
                return "The Firestore API is not switched on for that project yet (guide step 2).";
            return message;
        }

        /// <summary>A live ID token, refreshed when the old one is close to expiring.</summary>
        private static string Token()
        {
            lock (Gate)
            {
                if (_idToken != null && DateTime.UtcNow < _tokenDies) return _idToken;
            }

            string refresh = Unprotect(Properties.Settings.Default.NoteSyncToken);
            if (string.IsNullOrEmpty(refresh)) throw new Exception("Not signed in.");

            string form = "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refresh);
            object reply = Json.Parse(Post(RefreshUrl, form, null, "application/x-www-form-urlencoded"));
            string token = Json.Dig(reply, "id_token") as string;
            if (token == null) throw new Exception("Could not refresh the sign-in.");

            string rolled = Json.Dig(reply, "refresh_token") as string;
            if (!string.IsNullOrEmpty(rolled) && rolled != refresh)
            {
                Properties.Settings.Default.NoteSyncToken = Protect(rolled);
                Properties.Settings.Default.Save();
            }

            lock (Gate)
            {
                _idToken = token;
                _tokenDies = DateTime.UtcNow.AddMinutes(50);
                return _idToken;
            }
        }

        // ---------------------------------------------------------------- scheduling

        /// <summary>Called once at startup. Syncs shortly after launch, then every few minutes.</summary>
        public static void Start(Control uiThread)
        {
            _ui = uiThread;
            Status = IsOn ? "Waiting for the first sync..." : "Not connected.";
            _timer = new System.Threading.Timer(Tick, null, TimeSpan.FromSeconds(8), Poll);
            if (IsOn) Log("started, syncing " + Account);
        }

        /// <summary>Something changed - sync soon rather than at the next tick.</summary>
        public static void Nudge()
        {
            _dirty = true;
            if (!IsOn || _timer == null) return;
            try { _timer.Change(Soon, Poll); }
            catch (Exception) { }
        }

        private static void Tick(object state)
        {
            if (!IsOn) return;
            lock (Gate) { if (_busy) { Log("tick skipped - one already running"); return; } _busy = true; }
            try
            {
                bool changed = Sync();
                if (Status != _lastLogged) { Log(Status); _lastLogged = Status; }
                if (changed && _ui != null && _ui.IsHandleCreated)
                {
                    try { _ui.BeginInvoke(new Action(RaisePulled)); }
                    catch (Exception) { }
                }
            }
            catch (Exception ex)
            {
                Status = "Sync failed: " + ExplainFirestore(ex.Message);
                Log(Status);
            }
            finally
            {
                lock (Gate) { _busy = false; }
            }
        }

        /// <summary>
        /// A short rolling record of what the sync did, next to the notes. Sync problems
        /// are invisible by nature - this is the one place to look when notes are not
        /// turning up on the other PC.
        /// </summary>
        private static void Log(string line)
        {
            try
            {
                string path = System.IO.Path.Combine(NoteStore.Folder, ".sync-log");
                var lines = new List<string>();
                if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
                lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line);
                if (lines.Count > 60) lines.RemoveRange(0, lines.Count - 60);
                File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception) { }
        }

        private static void RaisePulled()
        {
            var handler = Pulled;
            if (handler != null) handler();
        }

        /// <summary>Runs a sync right now and reports what happened. Blocking - for the settings form.</summary>
        public static string SyncNow()
        {
            if (!IsOn) return "Not connected.";
            lock (Gate) { if (_busy) return "A sync is already running."; _busy = true; }
            try
            {
                bool changed = Sync();
                if (changed && _ui != null && _ui.IsHandleCreated)
                {
                    try { _ui.BeginInvoke(new Action(RaisePulled)); }
                    catch (Exception) { }
                }
                return null;
            }
            catch (Exception ex)
            {
                Status = "Sync failed: " + ExplainFirestore(ex.Message);
                return ExplainFirestore(ex.Message);
            }
            finally
            {
                lock (Gate) { _busy = false; }
            }
        }

        // ---------------------------------------------------------------- the sync itself

        private class Remote
        {
            public string Name;
            public string Text;
            public long Stamp;
            public bool Deleted;
            public bool Pinned;
            public bool Archived;
            public int Colour = -1;
            public int Order = -1;
            public long MetaAt;
        }

        /// <summary>One pass: pull what is newer up there, push what is newer down here. True if disk changed.</summary>
        private static bool Sync()
        {
            string uid = (Properties.Settings.Default.NoteSyncUid ?? "").Trim();
            if (uid.Length == 0) throw new Exception("Not signed in.");

            // one small read tells us whether anyone has changed anything; when nobody has
            // and we have nothing of our own to send, that is the whole tick
            long announced = ReadPulse(uid);
            if (!_dirty && _lastPulse > 0 && announced <= _lastPulse) return false;
            _dirty = false;

            var remote = List(uid);
            var tombstones = NoteTrash.Load();
            string folder = NoteStore.Folder;
            bool touchedDisk = false;
            int pushed = 0, pulled = 0;

            var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(folder, "*.txt")) local[Path.GetFileName(path)] = path;

            // 1. deletes made here since the last sync win over whatever is up there
            foreach (var gone in tombstones)
            {
                Remote up;
                if (local.ContainsKey(gone.Key)) continue;              // it came back; leave it alone
                if (!remote.TryGetValue(gone.Key, out up) || up.Deleted) continue;
                if (up.Stamp > gone.Value + SkewMs) continue;           // someone edited it after we deleted
                Write(uid, gone.Key, new Remote { Name = gone.Key, Deleted = true, Stamp = gone.Value });
                up.Deleted = true;
                pushed++;
            }

            // 2. bring down anything newer up there, and honour tombstones from other PCs.
            // The order is one list rather than a per-note value, so it follows whichever
            // side changed decoration last - measured before any of it is applied, or the
            // two sides would always look equal by the time the question is asked.
            var order = new List<KeyValuePair<int, string>>();
            long newestRemoteMeta = 0;
            long newestHere = NoteMeta.NewestStamp();
            foreach (var up in remote.Values)
            {
                string path;
                bool here = local.TryGetValue(up.Name, out path);

                if (up.Deleted)
                {
                    if (here && ToServer(File.GetLastWriteTimeUtc(path).ToMs()) <= up.Stamp + SkewMs)
                    {
                        try { File.Delete(path); touchedDisk = true; local.Remove(up.Name); } catch (Exception) { }
                    }
                    continue;
                }

                if (!here)
                {
                    if (tombstones.ContainsKey(up.Name)) continue;      // deleted here, waiting to be pushed
                    path = Path.Combine(folder, up.Name);
                    Save(path, up);
                    local[up.Name] = path;
                    touchedDisk = true;
                    pulled++;
                }
                else if (up.Stamp > ToServer(File.GetLastWriteTimeUtc(path).ToMs()) + SkewMs)
                {
                    Save(path, up);
                    touchedDisk = true;
                    pulled++;
                }

                if (up.Order >= 0) order.Add(new KeyValuePair<int, string>(up.Order, up.Name));
                if (up.MetaAt > newestRemoteMeta) newestRemoteMeta = up.MetaAt;
                ApplyMeta(up);
            }

            // 3. push everything here that is new or newer
            foreach (var pair in local)
            {
                Remote up;
                long stamp = ToServer(File.GetLastWriteTimeUtc(pair.Value).ToMs());
                if (remote.TryGetValue(pair.Key, out up) && !up.Deleted &&
                    stamp <= up.Stamp + SkewMs && NoteMeta.StampOf(pair.Value) <= up.MetaAt) continue;

                Write(uid, pair.Key, Read(pair.Value, stamp));
                pushed++;
            }

            if (order.Count > 0 && newestRemoteMeta > newestHere) ApplyOrder(order);
            NoteTrash.Forget(tombstones.Keys);

            // tell the other PCs there is something new, and remember what we have seen
            if (pushed > 0)
            {
                long now = NowServer;
                WritePulse(uid, now);
                _lastPulse = now;
            }
            else
            {
                _lastPulse = Math.Max(announced, 1);   // 1 means "synced once", so 0 stays "never"
            }

            Properties.Settings.Default.NoteSyncStamp = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);
            Properties.Settings.Default.Save();

            Status = "Synced " + local.Count + (local.Count == 1 ? " note" : " notes") +
                     (pulled > 0 || pushed > 0 ? " (" + pulled + " down, " + pushed + " up)" : "") +
                     " at " + DateTime.Now.ToString("HH:mm");
            return touchedDisk;
        }

        private static Remote Read(string path, long stamp)
        {
            string name = Path.GetFileName(path);
            var note = new Remote { Name = name, Stamp = stamp };
            try { note.Text = File.ReadAllText(path); } catch (Exception) { note.Text = ""; }
            note.Pinned = NoteMeta.IsPinned(path);
            note.Archived = NoteMeta.IsArchived(path);
            note.Colour = NoteMeta.ColourIndex(path);
            note.Order = NoteMeta.OrderOf(path);
            note.MetaAt = NoteMeta.StampOf(path);
            return note;
        }

        private static void Save(string path, Remote note)
        {
            try
            {
                File.WriteAllText(path, note.Text ?? "", new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(path, ToLocal(note.Stamp).ToUtc());
            }
            catch (Exception) { }
        }

        /// <summary>Decoration changes are applied on the UI thread - NoteMeta is not thread safe.</summary>
        private static void ApplyMeta(Remote note)
        {
            if (_ui == null || !_ui.IsHandleCreated) return;
            try
            {
                _ui.BeginInvoke(new Action(() =>
                    NoteMeta.ApplyRemote(note.Name, note.Pinned, note.Archived, note.Colour, note.MetaAt)));
            }
            catch (Exception) { }
        }

        private static void ApplyOrder(List<KeyValuePair<int, string>> order)
        {
            if (_ui == null || !_ui.IsHandleCreated) return;
            // two PCs number their rows independently, so ties are possible; break them by
            // name rather than leaving the result to an unstable sort
            order.Sort((a, b) => a.Key != b.Key
                ? a.Key.CompareTo(b.Key)
                : string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
            var names = new List<string>();
            foreach (var pair in order) names.Add(pair.Value);
            try { _ui.BeginInvoke(new Action(() => NoteMeta.ApplyOrder(names))); }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- Firestore

        private static Dictionary<string, Remote> List(string uid)
        {
            var found = new Dictionary<string, Remote>(StringComparer.OrdinalIgnoreCase);
            string page = "";
            do
            {
                string url = Documents + "/users/" + Uri.EscapeDataString(uid) + "/notes?pageSize=300" +
                             (page.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(page) : "");
                object reply = Json.Parse(Get(url, Token()));
                var documents = Json.Dig(reply, "documents") as List<object>;
                if (documents != null)
                {
                    foreach (object document in documents)
                    {
                        var note = Decode(document);
                        if (note != null) found[note.Name] = note;
                    }
                }
                page = (Json.Dig(reply, "nextPageToken") as string) ?? "";
            }
            while (page.Length > 0);
            return found;
        }

        private static Remote Decode(object document)
        {
            string name = Json.Dig(document, "name") as string;
            if (name == null) return null;
            int slash = name.LastIndexOf('/');
            var note = new Remote { Name = slash >= 0 ? name.Substring(slash + 1) : name };

            note.Text = (Json.Dig(document, "fields", "text", "stringValue") as string) ?? "";
            note.Stamp = Number(Json.Dig(document, "fields", "updatedAt", "integerValue"));
            note.Deleted = Json.Dig(document, "fields", "deleted", "booleanValue") as bool? ?? false;
            note.Pinned = Json.Dig(document, "fields", "pinned", "booleanValue") as bool? ?? false;
            note.Archived = Json.Dig(document, "fields", "archived", "booleanValue") as bool? ?? false;
            note.Colour = (int)Number(Json.Dig(document, "fields", "colour", "integerValue"), -1);
            note.Order = (int)Number(Json.Dig(document, "fields", "order", "integerValue"), -1);
            note.MetaAt = Number(Json.Dig(document, "fields", "metaAt", "integerValue"));
            return note;
        }

        /// <summary>Firestore sends integers as strings, so they survive being bigger than a double.</summary>
        private static long Number(object value, long fallback = 0)
        {
            var text = value as string;
            long parsed;
            if (text != null && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return parsed;
            if (value is double) return (long)(double)value;
            return fallback;
        }

        private static string PulsePath(string uid)
        {
            return Documents + "/users/" + Uri.EscapeDataString(uid) + "/state/pulse";
        }

        /// <summary>When any PC last pushed. Zero when nobody has yet, or the read failed.</summary>
        private static long ReadPulse(string uid)
        {
            try { return Number(Json.Dig(Json.Parse(Get(PulsePath(uid), Token())), "fields", "at", "integerValue")); }
            catch (Exception) { return 0; }   // no pulse yet: fall through to a full sync
        }

        private static void WritePulse(string uid, long at)
        {
            try
            {
                LearnClock(Send("PATCH", PulsePath(uid),
                     "{\"fields\":{\"at\":{\"integerValue\":\"" + at + "\"}}}", Token(), "application/json"));
            }
            catch (Exception) { }   // a missed pulse only means the others notice at the next full sync
        }

        private static void Write(string uid, string name, Remote note)
        {
            var body = new StringBuilder();
            body.Append("{\"fields\":{");
            if (note.Deleted)
            {
                body.Append("\"deleted\":{\"booleanValue\":true},");
            }
            else
            {
                body.Append("\"text\":{\"stringValue\":\"").Append(Json.Escape(note.Text ?? "")).Append("\"},");
                body.Append("\"pinned\":{\"booleanValue\":").Append(note.Pinned ? "true" : "false").Append("},");
                body.Append("\"archived\":{\"booleanValue\":").Append(note.Archived ? "true" : "false").Append("},");
                body.Append("\"colour\":{\"integerValue\":\"").Append(note.Colour).Append("\"},");
                body.Append("\"order\":{\"integerValue\":\"").Append(note.Order).Append("\"},");
                body.Append("\"metaAt\":{\"integerValue\":\"").Append(note.MetaAt).Append("\"},");
            }
            body.Append("\"updatedAt\":{\"integerValue\":\"").Append(note.Stamp).Append("\"}}}");

            string url = Documents + "/users/" + Uri.EscapeDataString(uid) + "/notes/" + Uri.EscapeDataString(name);
            LearnClock(Send("PATCH", url, body.ToString(), Token(), "application/json"));
        }

        // ---------------------------------------------------------------- plumbing

        private class CloudException : Exception
        {
            public readonly string Code;
            public CloudException(string code, string message) : base(message) { Code = code; }
        }

        private static string Get(string url, string bearer) { return Send("GET", url, null, bearer, null); }

        private static string Post(string url, string body, string bearer, string type = "application/json")
        {
            return Send("POST", url, body, bearer, type);
        }

        private static string Send(string method, string url, string body, string bearer, string type)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            if (bearer != null) request.Headers["Authorization"] = "Bearer " + bearer;

            if (body != null)
            {
                request.ContentType = type ?? "application/json";
                byte[] payload = Encoding.UTF8.GetBytes(body);
                using (var stream = request.GetRequestStream()) stream.Write(payload, 0, payload.Length);
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
                if (string.IsNullOrEmpty(detail)) throw new Exception(ex.Message);

                object root = null;
                try { root = Json.Parse(detail); } catch (Exception) { }
                string message = Json.Dig(root, "error", "message") as string;
                if (message == null) throw new Exception(detail.Length > 300 ? detail.Substring(0, 300) : detail);

                // Identity Toolkit puts the machine-readable reason first: "EMAIL_EXISTS : ..."
                string code = message;
                int space = code.IndexOfAny(new[] { ' ', ':' });
                if (space > 0) code = code.Substring(0, space);
                throw new CloudException(code, message);
            }
        }

        /// <summary>
        /// The refresh token is a key to the notes, so it is sealed to this Windows
        /// account - copying user.config to another PC does not carry the sign-in with it.
        /// </summary>
        private static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] sealed_ = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(sealed_);
            }
            catch (Exception) { return ""; }
        }

        private static string Unprotect(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception) { return ""; }
        }
    }

    /// <summary>
    /// Notes deleted here that the cloud has not been told about yet. Without this a
    /// delete made offline would just be undone by the next sync pulling the note back.
    /// One line per note: name|unix ms.
    /// </summary>
    public static class NoteTrash
    {
        private const string FileName = ".notes-deleted";

        private static string Path_ { get { return System.IO.Path.Combine(NoteStore.Folder, FileName); } }

        public static void Record(string path)
        {
            if (!NoteCloud.IsOn) return;
            try
            {
                string line = System.IO.Path.GetFileName(path) + "|" +
                              NoteCloud.NowServer.ToString(CultureInfo.InvariantCulture);
                File.AppendAllText(Path_, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
            NoteCloud.Nudge();
        }

        public static Dictionary<string, long> Load()
        {
            var found = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(Path_)) return found;
                foreach (string line in File.ReadAllLines(Path_, Encoding.UTF8))
                {
                    var parts = line.Split('|');
                    long stamp;
                    if (parts.Length < 2 || parts[0].Length == 0) continue;
                    if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out stamp)) continue;
                    found[parts[0]] = stamp;
                }
            }
            catch (Exception) { }
            return found;
        }

        /// <summary>Drops the names that have been pushed, keeping any recorded meanwhile.</summary>
        public static void Forget(IEnumerable<string> names)
        {
            var done = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var keep = new List<string>();
            foreach (var pair in Load())
            {
                if (done.Contains(pair.Key)) continue;
                keep.Add(pair.Key + "|" + pair.Value.ToString(CultureInfo.InvariantCulture));
            }
            try
            {
                if (keep.Count == 0) File.Delete(Path_);
                else File.WriteAllLines(Path_, keep.ToArray(), Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }

    internal static class StampExtensions
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long ToMs(this DateTime utc) { return (long)(utc.ToUniversalTime() - Epoch).TotalMilliseconds; }

        public static DateTime ToUtc(this long ms) { return Epoch.AddMilliseconds(ms); }
    }
}
