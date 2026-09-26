using AniListWatchTime.Services;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace AniListWatchTime
{
    public class AniListWatchTime : GenericPlugin
    {
        public const string MenuSectionName = "Playtime AniList";
        public const string ExtensionsMenuSectionName = "@" + MenuSectionName;

        private const string AutoSyncNotificationId = "PlaytimeAniListAutoSync";
        private const string StateFileName = "state.json";

        private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan AutoSyncStartDelay = TimeSpan.FromSeconds(5);

        private readonly AniListWatchTimeSettingsViewModel settings;
        private readonly AniListClient anilistClient;
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly object syncLock = new object();
        private bool syncRunning;

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
                    MenuSection = ExtensionsMenuSectionName,
                    Description = "Sincronizar tudo",
                    Action = a => RunSync(SyncWindow.All)
                },
                new MainMenuItem
                {
                    MenuSection = ExtensionsMenuSectionName,
                    Description = "Sincronizar atualizações da última semana",
                    Action = a => RunSync(SyncWindow.LastWeek)
                },
                new MainMenuItem
                {
                    MenuSection = ExtensionsMenuSectionName,
                    Description = "Sincronizar atualizações do último mês",
                    Action = a => RunSync(SyncWindow.LastMonth)
                },
                new MainMenuItem
                {
                    MenuSection = ExtensionsMenuSectionName,
                    Description = "-"
                },
                new MainMenuItem
                {
                    MenuSection = ExtensionsMenuSectionName,
                    Description = "Sincronizar anime e mangá",
                    Action = a => RunSync(SyncWindow.All, SyncScope.Both)
                }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var importedGames = args.Games.Where(g => g.PluginId == AniListClient.ImporterPluginId).ToList();
            if (importedGames.Count == 0)
            {
                return Enumerable.Empty<GameMenuItem>();
            }

            return new List<GameMenuItem>
            {
                new GameMenuItem
                {
                    MenuSection = MenuSectionName,
                    Description = "Atualizar tempo destes itens",
                    Action = a => RunSelectedSync(importedGames)
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

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            base.OnLibraryUpdated(args);

            if (!settings.Settings.AutoSyncOnLibraryUpdate)
            {
                return;
            }

            var scope = GetConfiguredScope();
            if (!scope.HasValue)
            {
                logger.Warn("Sincronização automática ignorada: nenhum tipo de mídia selecionado nas configurações.");
                return;
            }

            var state = LoadState();
            if (state.LastAutoSyncUtc.HasValue &&
                DateTime.UtcNow - state.LastAutoSyncUtc.Value < AutoSyncInterval)
            {
                logger.Info("Sincronização automática ignorada: a última execução foi há menos de 24 horas.");
                return;
            }

            logger.Info("Sincronização automática agendada após a atualização da biblioteca.");
            var syncScope = scope.Value;
            Task.Run(async () =>
            {
                await Task.Delay(AutoSyncStartDelay).ConfigureAwait(false);

                PlayniteApi.Notifications.Remove(AutoSyncNotificationId);
                var result = ExecuteSync(SyncWindow.All, syncScope);

                state = LoadState();
                state.LastAutoSyncUtc = DateTime.UtcNow;
                SaveState(state);

                if (!string.IsNullOrEmpty(result) && settings.Settings.AutoSyncShowNotification)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        AutoSyncNotificationId,
                        result.Replace(Environment.NewLine, " "),
                        NotificationType.Info));
                }
            });
        }

        private SyncScope? GetConfiguredScope()
        {
            var currentSettings = settings.Settings;
            if (currentSettings.SyncAnime && currentSettings.SyncManga)
            {
                return SyncScope.Both;
            }

            if (currentSettings.SyncAnime)
            {
                return SyncScope.Anime;
            }

            if (currentSettings.SyncManga)
            {
                return SyncScope.Manga;
            }

            return null;
        }

        private void RunSync(SyncWindow window, SyncScope? scopeOverride = null)
        {
            var scope = scopeOverride ?? GetConfiguredScope();
            if (!scope.HasValue)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "Selecione pelo menos um tipo de mídia (anime ou mangá) nas configurações da extensão.",
                    "Playtime AniList");
                return;
            }

            var result = ExecuteSync(window, scope.Value);
            if (!string.IsNullOrEmpty(result))
            {
                PlayniteApi.Dialogs.ShowMessage(result, "Playtime AniList");
            }
        }

        private string ExecuteSync(SyncWindow window, SyncScope scope)
        {
            lock (syncLock)
            {
                if (syncRunning)
                {
                    return "Já existe uma sincronização em andamento. Aguarde ela terminar.";
                }

                syncRunning = true;
            }

            try
            {
                string result = null;
                RunOnUiThread(() =>
                {
                    var progressOptions = new GlobalProgressOptions("Playtime AniList - Sincronizando...", false)
                    {
                        IsIndeterminate = true
                    };

                    PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
                    {
                        try
                        {
                            result = anilistClient.SyncLibrary(settings.Settings, PlayniteApi, window, scope);
                        }
                        catch (Exception e)
                        {
                            LogManager.GetLogger().Error(e, "Erro ao sincronizar a biblioteca.");
                            result = "Erro ao sincronizar: " + e.Message;
                        }
                    }, progressOptions);
                });

                return result;
            }
            finally
            {
                lock (syncLock)
                {
                    syncRunning = false;
                }
            }
        }

        private void RunSelectedSync(List<Game> games)
        {
            string result = null;
            var progressOptions = new GlobalProgressOptions("Playtime AniList - Atualizando selecionados...", false)
            {
                IsIndeterminate = true
            };

            PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
            {
                try
                {
                    result = anilistClient.SyncSelectedGames(settings.Settings, PlayniteApi, games);
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

        private void RunOnUiThread(Action action)
        {
            var dispatcher = PlayniteApi.MainView.UIDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action);
        }

        private PluginState LoadState()
        {
            var statePath = Path.Combine(GetPluginUserDataPath(), StateFileName);
            if (!File.Exists(statePath))
            {
                return new PluginState();
            }

            try
            {
                return Serialization.FromJsonFile<PluginState>(statePath) ?? new PluginState();
            }
            catch (Exception e)
            {
                logger.Error(e, "Falha ao ler o estado interno da extensão.");
                return new PluginState();
            }
        }

        private void SaveState(PluginState state)
        {
            var statePath = Path.Combine(GetPluginUserDataPath(), StateFileName);
            try
            {
                File.WriteAllText(statePath, Serialization.ToJson(state, true));
            }
            catch (Exception e)
            {
                logger.Error(e, "Falha ao salvar o estado interno da extensão.");
            }
        }
    }

    public class PluginState
    {
        public DateTime? LastAutoSyncUtc { get; set; }
    }
}
