using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AnthropicProxyDesktop
{
    sealed class NamedProfile
    {
        public NamedProfile() {}
        public string Name = "";
        public Settings Settings = new Settings();
    }
    sealed class ProfileLibrary
    {
        public ProfileLibrary() {}
        public int Version = 1;
        public string Selected = "Default";
        public List<NamedProfile> Profiles = new List<NamedProfile>();
    }
    sealed class ProfileStore
    {
        readonly string path;
        readonly Func<Settings> current;
        internal ProfileStore(string directory, Func<Settings> currentSettings)
        {
            path = Path.Combine(directory, "profiles.json"); current = currentSettings;
        }
        internal static string Name(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Length > 80 || value.Any(Char.IsControl))
                throw new Exception("Enter a profile name of 1 to 80 characters without control characters.");
            return value;
        }
        internal ProfileLibrary Read()
        {
            if (!File.Exists(path))
            {
                var initial = new ProfileLibrary();
                initial.Profiles.Add(new NamedProfile { Name = "Default", Settings = current() });
                Write(initial); return initial;
            }
            var library = new JavaScriptSerializer().Deserialize<ProfileLibrary>(File.ReadAllText(path));
            if (library == null || library.Version != 1 || library.Profiles == null || library.Profiles.Count == 0)
                throw new Exception("The saved profiles file is invalid or uses an unsupported version.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in library.Profiles)
                if (profile == null || profile.Settings == null || !names.Add(Name(profile.Name)))
                    throw new Exception("The saved profiles file contains invalid or duplicate profiles.");
            if (!names.Contains(library.Selected ?? "")) library.Selected = library.Profiles[0].Name;
            return library;
        }
        void Write(ProfileLibrary library)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(library), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        static NamedProfile Find(ProfileLibrary library, string name)
        {
            var profile = library.Profiles.FirstOrDefault(p => String.Equals(p.Name, Name(name), StringComparison.OrdinalIgnoreCase));
            if (profile == null) throw new Exception("Select a saved profile, or type a new name and choose Save.");
            return profile;
        }
        internal Settings Load(string name)
        {
            var library = Read();
            var profile = Find(library, name);
            profile.Settings.Validate(); profile.Settings.GetKey();
            library.Selected = profile.Name; Write(library);
            return profile.Settings;
        }
        internal string Save(string name, Settings settings)
        {
            name = Name(name); settings.Validate();
            var library = Read();
            var existing = library.Profiles.FirstOrDefault(p => String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null) library.Profiles.Add(new NamedProfile { Name = name, Settings = settings });
            else { existing.Settings = settings; name = existing.Name; }
            library.Selected = name; Write(library); return name;
        }
        internal string Rename(string oldName, string newName)
        {
            newName = Name(newName);
            var library = Read(); var profile = Find(library, oldName);
            if (library.Profiles.Any(p => p != profile && String.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
                throw new Exception("A profile with that name already exists. Choose another name.");
            if (String.Equals(library.Selected, profile.Name, StringComparison.OrdinalIgnoreCase)) library.Selected = newName;
            profile.Name = newName; Write(library); return newName;
        }
        internal static void Export(string file, Settings settings, bool includeKey)
        {
            settings.Validate();
            var serializer = new JavaScriptSerializer();
            var fields = serializer.Deserialize<Dictionary<string, object>>(serializer.Serialize(settings));
            fields.Remove("Secret");
            if (includeKey) fields["ApiKey"] = settings.GetKey();
            // Runtime paths are local to the selected machine; values are retained for review on import.
            File.WriteAllText(file, serializer.Serialize(fields), new UTF8Encoding(false));
        }
        internal string Import(string file)
        {
            // Import data only. Never evaluate shell commands or change autostart.
            if (new FileInfo(file).Length > 1024 * 1024) throw new Exception("Settings imports must be smaller than 1 MiB.");
            string text = File.ReadAllText(file);
            Settings settings;
            if (text.TrimStart().StartsWith("{"))
            {
                var serializer = new JavaScriptSerializer();
                var fields = serializer.Deserialize<Dictionary<string, object>>(text);
                if (fields == null || !fields.Keys.Any(k => String.Equals(k, "Upstream", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Choose a desktop settings.json file or a .env configuration file.");
                settings = serializer.Deserialize<Settings>(text);
                object plainKey;
                if (fields.TryGetValue("ApiKey", out plainKey) && plainKey is string) settings.SetKey((string)plainKey);
                try { settings.GetKey(); }
                catch (System.Security.Cryptography.CryptographicException) { throw new Exception("This file's API key belongs to another Windows account. Import a .env file or remove the encrypted Secret field and enter the key again."); }
            }
            else settings = FromEnvironment(text);
            if (!String.IsNullOrEmpty(settings.ConfigFile) && !File.Exists(settings.ConfigFile)) settings.ConfigFile = "";
            settings.Validate();
            string name = Path.GetFileNameWithoutExtension(file).Trim('.').Trim();
            if (String.IsNullOrWhiteSpace(name)) name = "Imported";
            name = Name(name.Length > 65 ? name.Substring(0, 65) : name);
            var library = Read(); string candidate = name; int suffix = 2;
            while (library.Profiles.Any(p => String.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase))) candidate = name + " (" + suffix++ + ")";
            return Save(candidate, settings);
        }
        static Settings FromEnvironment(string text)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            int row = 0;
            foreach (string raw in text.Split('\n'))
            {
                row++; string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("export ")) line = line.Substring(7).TrimStart();
                int equal = line.IndexOf('=');
                if (equal < 1) throw new Exception("Expected NAME=value on import line " + row + ".");
                string key = line.Substring(0, equal).Trim();
                values[key] = ParseValue(line.Substring(equal + 1).Trim(), row);
            }
            Func<string, string, string> get = (key, fallback) => values.ContainsKey(key) ? values[key] : fallback;
            string upstream = get("UPSTREAM_BASE_URL", get("ANTHROPIC_PROXY_BASE_URL", ""));
            if (String.IsNullOrWhiteSpace(upstream)) throw new Exception("The imported file must specify UPSTREAM_BASE_URL.");
            int port;
            if (!Int32.TryParse(get("PORT", "3000"), out port)) throw new Exception("The imported PORT must be a number.");
            var settings = new Settings { Upstream = upstream, Bind = get("ANTHROPIC_PROXY_BIND", "127.0.0.1"), Port = port,
                Completion = get("COMPLETION_MODEL", ""), Reasoning = get("REASONING_MODEL", ""),
                ModelMap = get("ANTHROPIC_PROXY_MODEL_MAP", ""), IgnoreTerms = get("ANTHROPIC_PROXY_SYSTEM_PROMPT_IGNORE_TERMS", ""),
                Passthrough = BooleanValue(get("UPSTREAM_API_KEY_PASSTHROUGH", "false")), Debug = BooleanValue(get("DEBUG", "false")), CaptureBodies = BooleanValue(get("VERBOSE", "false")) };
            settings.SetKey(get("UPSTREAM_API_KEY", get("OPENROUTER_API_KEY", ""))); return settings;
        }
        static bool BooleanValue(string value)
        {
            if (value == "1" || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || String.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            throw new Exception("Imported boolean settings must be true, false, 1 or 0.");
        }
        static string ParseValue(string value, int row)
        {
            if (value.Length == 0) return "";
            char quote = value[0];
            if (quote != '\'' && quote != '"')
            {
                for (int i = 0; i < value.Length; i++)
                    if (value[i] == '#' && (i == 0 || Char.IsWhiteSpace(value[i - 1]))) return value.Substring(0, i).TrimEnd();
                return value;
            }
            var result = new StringBuilder();
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                if (c == quote)
                {
                    string rest = value.Substring(i + 1).TrimStart();
                    if (rest.Length > 0 && !rest.StartsWith("#")) break;
                    return result.ToString();
                }
                if (quote == '"' && c == '\\' && i + 1 < value.Length)
                {
                    char next = value[++i];
                    if (next == 'n') result.Append('\n'); else if (next == 'r') result.Append('\r');
                    else if (next == 't') result.Append('\t'); else if (next == '\\' || next == '"') result.Append(next);
                    else result.Append('\\').Append(next);
                }
                else result.Append(c);
            }
            throw new Exception("Invalid quoted value on import line " + row + ". Use one line per setting.");
        }
    }
}
