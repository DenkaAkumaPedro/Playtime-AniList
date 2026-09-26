using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace AniListWatchTime
{
    public class AniListWatchTimeSettings : ObservableObject
    {
        private string accessTokenOverride = string.Empty;
        private bool accumulateToExisting = false;

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
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}