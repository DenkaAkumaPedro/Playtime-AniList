using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;

namespace AniListWatchTime
{
    public enum SessionMode
    {
        ReplaceTotal = 0,
        Incremental = 1
    }

    public class AniListWatchTimeSettings : ObservableObject
    {
        private const int DefaultPagesPerVolume = 200;
        private const int DefaultSecondsPerPage = 18;
        private const int DefaultFallbackMinutesPerChapter = 5;
        private const int DefaultNovelMinutesPerChapter = 12;
        private const int DefaultWebtoonMinutesPerChapter = 10;

        private string accessTokenOverride = string.Empty;
        private bool accumulateToExisting = false;
        private bool syncAnime = true;
        private bool syncManga = true;
        private int pagesPerVolume = DefaultPagesPerVolume;
        private int secondsPerPage = DefaultSecondsPerPage;
        private int fallbackMinutesPerChapter = DefaultFallbackMinutesPerChapter;
        private int novelMinutesPerChapter = DefaultNovelMinutesPerChapter;
        private int webtoonMinutesPerChapter = DefaultWebtoonMinutesPerChapter;
        private bool autoSyncOnLibraryUpdate = false;
        private bool autoSyncShowNotification = true;
        private bool writeActivitySessions = false;
        private SessionMode sessionMode = SessionMode.Incremental;

        public string AccessTokenOverride
        {
            get => accessTokenOverride;
            set => SetValue(ref accessTokenOverride, value);
        }

        public bool AccumulateToExisting
        {
            get => accumulateToExisting;
            set => SetValue(ref accumulateToExisting, value);
        }

        public bool SyncAnime
        {
            get => syncAnime;
            set => SetValue(ref syncAnime, value);
        }

        public bool SyncManga
        {
            get => syncManga;
            set => SetValue(ref syncManga, value);
        }

        public int PagesPerVolume
        {
            get => pagesPerVolume;
            set => SetValue(ref pagesPerVolume, value);
        }

        public int SecondsPerPage
        {
            get => secondsPerPage;
            set => SetValue(ref secondsPerPage, value);
        }

        public int FallbackMinutesPerChapter
        {
            get => fallbackMinutesPerChapter;
            set => SetValue(ref fallbackMinutesPerChapter, value);
        }

        public int NovelMinutesPerChapter
        {
            get => novelMinutesPerChapter;
            set => SetValue(ref novelMinutesPerChapter, value);
        }

        public int WebtoonMinutesPerChapter
        {
            get => webtoonMinutesPerChapter;
            set => SetValue(ref webtoonMinutesPerChapter, value);
        }

        public bool AutoSyncOnLibraryUpdate
        {
            get => autoSyncOnLibraryUpdate;
            set => SetValue(ref autoSyncOnLibraryUpdate, value);
        }

        public bool AutoSyncShowNotification
        {
            get => autoSyncShowNotification;
            set => SetValue(ref autoSyncShowNotification, value);
        }

        public bool WriteActivitySessions
        {
            get => writeActivitySessions;
            set => SetValue(ref writeActivitySessions, value);
        }

        public SessionMode SessionMode
        {
            get => sessionMode;
            set => SetValue(ref sessionMode, value);
        }

        public int GetPagesPerVolume()
        {
            return Clamp(pagesPerVolume, 20, 500);
        }

        public int GetSecondsPerPage()
        {
            return Clamp(secondsPerPage, 1, 300);
        }

        public int GetFallbackMinutesPerChapter()
        {
            return Clamp(fallbackMinutesPerChapter, 1, 120);
        }

        public int GetNovelMinutesPerChapter()
        {
            return Clamp(novelMinutesPerChapter, 1, 120);
        }

        public int GetWebtoonMinutesPerChapter()
        {
            return Clamp(webtoonMinutesPerChapter, 1, 120);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }

    public class AniListWatchTimeSettingsViewModel : ObservableObject, ISettings
    {
        private readonly AniListWatchTime plugin;
        private AniListWatchTimeSettings editingClone;
        private AniListWatchTimeSettings settings;

        public AniListWatchTimeSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public AniListWatchTimeSettingsViewModel(AniListWatchTime plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<AniListWatchTimeSettings>();
            Settings = savedSettings ?? new AniListWatchTimeSettings();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            // Sem BeginEdit nao ha clone: trocar mesmo assim deixaria Settings nulo e a tela
            // de configuracoes quebraria no binding.
            if (editingClone != null)
            {
                Settings = editingClone;
            }
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            if (!Settings.SyncAnime && !Settings.SyncManga)
            {
                errors.Add("Selecione ao menos um tipo de mídia para sincronizar (anime ou mangá).");
            }

            if (Settings.PagesPerVolume < 20 || Settings.PagesPerVolume > 500)
            {
                errors.Add("Páginas por volume deve estar entre 20 e 500.");
            }

            if (Settings.SecondsPerPage < 1 || Settings.SecondsPerPage > 300)
            {
                errors.Add("Segundos por página deve estar entre 1 e 300.");
            }

            if (Settings.FallbackMinutesPerChapter < 1 || Settings.FallbackMinutesPerChapter > 120)
            {
                errors.Add("Minutos por capítulo (padrão) deve estar entre 1 e 120.");
            }

            if (Settings.NovelMinutesPerChapter < 1 || Settings.NovelMinutesPerChapter > 120)
            {
                errors.Add("Minutos por capítulo de novel deve estar entre 1 e 120.");
            }

            if (Settings.WebtoonMinutesPerChapter < 1 || Settings.WebtoonMinutesPerChapter > 120)
            {
                errors.Add("Minutos por capítulo de webtoon deve estar entre 1 e 120.");
            }

            return errors.Count == 0;
        }
    }
}
