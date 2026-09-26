using AniListWatchTime.Services;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace AniListWatchTime
{
    public class AniListWatchTime : GenericPlugin
    {
        public const string MenuSectionName = "Playtime AniList";

        private readonly AniListWatchTimeSettingsViewModel settings;
        private readonly AniListClient anilistClient;

        public override Guid Id { get; } = Guid.Parse("C034A45E-3C56-48DA-ABE0-1C46B4C4A57D");

        public AniListWatchTime(IPlayniteAPI api) : base(api)
        {
            settings = new AniListWatchTimeSettingsViewModel(this);
            anilistClient = new AniListClient();
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            return new List<MainMenuItem>
            {
                new MainMenuItem
                {
                    MenuSection = MenuSectionName,
                    Description = "Sincronizar tudo",
                    Action = a => RunSync(SyncWindow.All)
                },
                new MainMenuItem
                {
                    MenuSection = MenuSectionName,
                    Description = "Sincronizar atualizações da última semana",
                    Action = a => RunSync(SyncWindow.LastWeek)
                },
                new MainMenuItem
                {
                    MenuSection = MenuSectionName,
                    Description = "Sincronizar atualizações do último mês",
                    Action = a => RunSync(SyncWindow.LastMonth)
                }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var animeGames = args.Games.Where(g => g.PluginId == AniListClient.ImporterPluginId).ToList();
            if (animeGames.Count == 0)
            {
                return Enumerable.Empty<GameMenuItem>();
            }

            return new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    MenuSection = MenuSectionName,
                    Description = "Atualizar tempo deste anime",
                    Action = a => RunSelectedSync(animeGames)
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new AniListWatchTimeSettingsView();
        }

        private void RunSync(SyncWindow window)
        {
            var progressOptions = new GlobalProgressOptions("Playtime AniList - Sincronizando...", false)
            {
                IsIndeterminate = true
            };

            string result = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
            {
                try
                {
                    result = anilistClient.SyncLibrary(settings.Settings, PlayniteApi, window);
                }
                catch (Exception e)
                {
                    LogManager.GetLogger().Error(e, "Erro ao sincronizar a biblioteca.");
                    result = "Erro ao sincronizar: " + e.Message;
                }
            }, progressOptions);

            if (!string.IsNullOrEmpty(result))
            {
                PlayniteApi.Dialogs.ShowMessage(result, "Playtime AniList");
            }
        }

        private void RunSelectedSync(List<Game> animeGames)
        {
            var progressOptions = new GlobalProgressOptions("Playtime AniList - Atualizando selecionados...", false)
            {
                IsIndeterminate = true
            };

            string result = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
            {
                try
                {
                    result = anilistClient.SyncSelectedGames(settings.Settings, PlayniteApi, animeGames);
                }
                catch (Exception e)
                {
                    LogManager.GetLogger().Error(e, "Erro ao atualizar os selecionados.");
                    result = "Erro ao atualizar: " + e.Message;
                }
            }, progressOptions);

            if (!string.IsNullOrEmpty(result))
            {
                PlayniteApi.Dialogs.ShowMessage(result, "Playtime AniList");
            }
        }
    }
}