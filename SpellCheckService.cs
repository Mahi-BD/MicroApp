using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace MicroApp
{
    /// <summary>
    /// Thin wrapper over the Windows Spell Checking API (Windows 8+). English is always
    /// tried; Bangla only when that language is installed, and Bangla words are never fed
    /// to the English checker, so a Bangla note does not light up all red. Results are
    /// cached per word because the note re-checks after every pause in typing. When the
    /// COM service is missing entirely, TryCreate returns null and the note simply shows
    /// no squiggles.
    /// </summary>
    public class SpellCheckService
    {
        private readonly ISpellChecker _english;
        private readonly ISpellChecker _bangla;
        private readonly Dictionary<string, bool> _cache = new Dictionary<string, bool>();

        private SpellCheckService(ISpellChecker english, ISpellChecker bangla)
        {
            _english = english;
            _bangla = bangla;
        }

        public static SpellCheckService TryCreate()
        {
            try
            {
                var factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass();
                ISpellChecker english = Create(factory, "en-US");
                ISpellChecker bangla = Create(factory, "bn-IN");
                if (bangla == null) bangla = Create(factory, "bn-BD");
                if (english == null && bangla == null) return null;
                return new SpellCheckService(english, bangla);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ISpellChecker Create(ISpellCheckerFactory factory, string tag)
        {
            try
            {
                int supported;
                factory.IsSupported(tag, out supported);
                if (supported == 0) return null;
                ISpellChecker checker;
                factory.CreateSpellChecker(tag, out checker);
                return checker;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool IsBangla(string word)
        {
            foreach (char c in word)
            {
                if (c >= 0x0980 && c <= 0x09FF) return true;
            }
            return false;
        }

        /// <summary>True when the word's language has a checker and that checker flags it.</summary>
        public bool IsMisspelled(string word)
        {
            bool bad;
            if (_cache.TryGetValue(word, out bad)) return bad;

            var checker = IsBangla(word) ? _bangla : _english;
            bad = false;
            if (checker != null)
            {
                IEnumSpellingError errors = null;
                try
                {
                    checker.Check(word, out errors);
                    if (errors != null)
                    {
                        ISpellingError error;
                        if (errors.Next(out error) == 0 && error != null)
                        {
                            bad = true;
                            Marshal.ReleaseComObject(error);
                        }
                    }
                }
                catch (Exception) { }
                finally
                {
                    if (errors != null) Marshal.ReleaseComObject(errors);
                }
            }

            if (_cache.Count > 4000) _cache.Clear();
            _cache[word] = bad;
            return bad;
        }

        public List<string> Suggest(string word, int max)
        {
            var list = new List<string>();
            var checker = IsBangla(word) ? _bangla : _english;
            if (checker == null) return list;

            IEnumString suggestions = null;
            try
            {
                checker.Suggest(word, out suggestions);
                if (suggestions != null)
                {
                    var one = new string[1];
                    while (list.Count < max && suggestions.Next(1, one, IntPtr.Zero) == 0)
                    {
                        if (!string.IsNullOrEmpty(one[0])) list.Add(one[0]);
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                if (suggestions != null) Marshal.ReleaseComObject(suggestions);
            }
            return list;
        }

        /// <summary>Adds the word to the user's Windows dictionary for its language.</summary>
        public void Add(string word)
        {
            var checker = IsBangla(word) ? _bangla : _english;
            if (checker == null) return;
            try
            {
                checker.Add(word);
                _cache.Remove(word);
            }
            catch (Exception) { }
        }
    }

    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    class SpellCheckerFactoryClass
    {
    }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactory
    {
        void get_SupportedLanguages(out IEnumString languages);
        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int isSupported);
        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker checker);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellChecker
    {
        void get_LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string tag);
        void Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError errors);
        void Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumString suggestions);
        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);
        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);
        void AutoCorrect([MarshalAs(UnmanagedType.LPWStr)] string from, [MarshalAs(UnmanagedType.LPWStr)] string to);
        void GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId, out byte value);
        void get_OptionIds(out IEnumString ids);
        void get_Id([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void add_SpellCheckerChanged(IntPtr handler, out uint cookie);
        void remove_SpellCheckerChanged(uint cookie);
        void GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId, out IntPtr description);
        void ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError errors);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingError
    {
        [PreserveSig]
        int Next(out ISpellingError error);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingError
    {
        void get_StartIndex(out uint start);
        void get_Length(out uint length);
        void get_CorrectiveAction(out uint action);
        void get_Replacement([MarshalAs(UnmanagedType.LPWStr)] out string replacement);
    }
}
