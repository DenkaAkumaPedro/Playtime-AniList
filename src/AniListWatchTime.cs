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
        public const string MenuSectionName = "AniList PlayTime";

        // Sem o prefixo "@". Com "@" o Playnite colocava a extensao como menu proprio na
        // barra principal, e nao dentro de Extensoes como o README sempre prometeu.
        public const string ExtensionsMenuSectionName = MenuSectionName;

        private const string AutoSyncNotificationId = "AniListPlayTimeAutoSync";
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

            var state = LoadState(out _);
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

                // So carimba as 24 horas quando a sync chegou ao fim. Sem isso, uma falha de
                // rede ou um token invalido travava a sincronizacao automatica por um dia
                // inteiro sem o usuario tomar ciencia de nada.
                if (!result.Failed)
                {
                    state = LoadState(out _);
                    state.LastAutoSyncUtc = DateTime.UtcNow;
                    SaveState(state);
                }

                if (!string.IsNullOrEmpty(result.Message) && settings.Settings.AutoSyncShowNotification)
                {
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        AutoSyncNotificationId,
                        result.Message.Replace(Environment.NewLine, " "),
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
                    MenuSectionName);
                return;
            }

            var result = ExecuteSync(window, scope.Value);
            if (!string.IsNullOrEmpty(result.Message))
            {
                PlayniteApi.Dialogs.ShowMessage(result.Message, MenuSectionName);
            }
        }

        private SyncResult ExecuteSync(SyncWindow window, SyncScope scope)
        {
            lock (syncLock)
            {
                if (syncRunning)
                {
                    return new SyncResult("Já existe uma sincronização em andamento. Aguarde ela terminar.", true);
                }

                syncRunning = true;
            }

            try
            {
                bool stateDamaged;
                var state = LoadState(out stateDamaged);
                if (stateDamaged)
                {
                    return new SyncResult(StateDamagedMessage(), true);
                }

                if (state.AppliedSeconds == null)
                {
                    state.AppliedSeconds = new Dictionary<string, long>();
                }

                SyncResult result = null;
                RunOnUiThread(() =>
                {
                    var progressOptions = new GlobalProgressOptions(MenuSectionName + " - Sincronizando...", false)
                    {
                        IsIndeterminate = true
                    };

                    PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
                    {
                        try
                        {
                            result = anilistClient.SyncLibrary(settings.Settings, PlayniteApi, window, scope,
                                state.AppliedSeconds);
                        }
                        catch (Exception e)
                        {
                            LogManager.GetLogger().Error(e, "Erro ao sincronizar a biblioteca.");
                            result = new SyncResult("Erro ao sincronizar: " + e.Message, true);
                        }
                    }, progressOptions);
                });

                // A linha de base so e persistida quando a sincronizacao foi ate o fim.
                // Um erro no meio do caminho deixa o arquivo como estava, o custo e a proxima
                // sincronizacao somar por cima de uma vez.
                if (result != null && !result.Failed)
                {
                    SaveState(state);
                }

                return result ?? new SyncResult("A sincronização não produziu resultado.", true);
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
            // Mesma trava de ExecuteSync. Sem ela o autosync (Task.Run) e esta sincronizacao
            // rodavam juntos: dois LoadState/SaveState, e o segundo sobrescrevia o estado do
            // primeiro, o que fazia a sync seguinte somar o tempo da extensao por cima dele
            // mesmo.
            lock (syncLock)
            {
                if (syncRunning)
                {
                    PlayniteApi.Dialogs.ShowMessage(
                        "Já existe uma sincronização em andamento. Aguarde ela terminar.",
                        MenuSectionName);
                    return;
                }

                syncRunning = true;
            }

            try
            {
                bool stateDamaged;
                var state = LoadState(out stateDamaged);
                if (stateDamaged)
                {
                    PlayniteApi.Dialogs.ShowMessage(StateDamagedMessage(), MenuSectionName);
                    return;
                }

                if (state.AppliedSeconds == null)
                {
                    state.AppliedSeconds = new Dictionary<string, long>();
                }

                SyncResult result = null;
                var progressOptions = new GlobalProgressOptions(MenuSectionName + " - Atualizando selecionados...", false)
                {
                    IsIndeterminate = true
                };

                PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
                {
                    try
                    {
                        result = anilistClient.SyncSelectedGames(settings.Settings, PlayniteApi, games, state.AppliedSeconds);
                    }
                    catch (Exception e)
                    {
                        LogManager.GetLogger().Error(e, "Erro ao atualizar os selecionados.");
                        result = new SyncResult("Erro ao atualizar: " + e.Message, true);
                    }
                }, progressOptions);

                result = result ?? new SyncResult("A atualização não produziu resultado.", true);

                if (!result.Failed)
                {
                    SaveState(state);
                }

                if (!string.IsNullOrEmpty(result.Message))
                {
                    PlayniteApi.Dialogs.ShowMessage(result.Message, MenuSectionName);
                }
            }
            finally
            {
                lock (syncLock)
                {
                    syncRunning = false;
                }
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

        private PluginState LoadState(out bool damaged)
        {
            damaged = false;
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
                // Um state.json ilegivel significa perder a linha de base, e perder a linha
                // de base com "somar" ligado faz a proxima sync contar o tempo desta extensao
                // duas vezes. Melhor recusar a sync e deixar o arquivo intacto para o usuario
                // recuperar do que tratar o estado como vazio e inflar a biblioteca inteira.
                logger.Error(e, "Falha ao ler o estado interno da extensão.");
                damaged = true;
                return new PluginState();
            }
        }

        private string StateDamagedMessage()
        {
            return "Não foi possível ler o arquivo de estado da extensão (" +
                   Path.Combine(GetPluginUserDataPath(), StateFileName) + ")." + Environment.NewLine +
                   Environment.NewLine +
                   "Ele guarda o quanto esta extensão já escreveu em cada jogo. Sem ele a próxima " +
                   "sincronização não sabe separar o tempo dela do tempo que já estava no jogo, " +
                   "e com \"Somar ao tempo de jogo já existente\" ligado isso contaria o tempo duas vezes." +
                   Environment.NewLine + Environment.NewLine +
                   "Nada foi alterado e o arquivo foi preservado. Para recomeçar: apague o arquivo, " +
                   "desligue \"Somar ao tempo de jogo já existente\", sincronize uma vez e ligue a " +
                   "opção de novo. Sincronizar logo após apagar o arquivo, com a opção ligada, " +
                   "somaria o tempo do AniList ao valor que esta extensão tinha gravado antes." +
                   Environment.NewLine + Environment.NewLine +
                   "Para manter o tempo de origem desconhecida, restaure um backup em vez de apagar.";
        }

        private void SaveState(PluginState state)
        {
            var statePath = Path.Combine(GetPluginUserDataPath(), StateFileName);
            var tempPath = statePath + ".tmp";
            try
            {
                // Escrita em arquivo temporário e troca no lugar: um crash ou uma queda de
                // energia no meio da escrita deixaria um state.json truncado, que na proxima
                // leitura cai no caso de "estado danificado" acima.
                File.WriteAllText(tempPath, Serialization.ToJson(state, true));
                if (File.Exists(statePath))
                {
                    File.Replace(tempPath, statePath, null);
                }
                else
                {
                    File.Move(tempPath, statePath);
                }
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

        public Dictionary<string, long> AppliedSeconds { get; set; } = new Dictionary<string, long>();
    }
}
