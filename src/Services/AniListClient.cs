using AniListWatchTime.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace AniListWatchTime.Services
{
    public enum SyncWindow
    {
        All,
        LastWeek,
        LastMonth
    }

    public class AniListClient
    {
        public static readonly Guid ImporterPluginId = Guid.Parse("2366fb38-bf25-45ea-9a78-dcc797ee83c3");

        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private const long SecondsPerWeek = 7L * 24L * 60L * 60L;
        private const long SecondsPerMonth = 30L * 24L * 60L * 60L;

        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };

        private readonly ILogger logger = LogManager.GetLogger();
        private readonly Regex anilistAnimeIdRegex = new Regex(@"(?:anilist\.co/anime|anilist\.co)/?(\d+)", RegexOptions.IgnoreCase);

        private const string viewerQuery = @"
        query {
            user: Viewer {
                id
                name
            }
        }";

        private const string mediaListQuery = @"
        query ($userId: Int, $type: MediaType) {
            list: MediaListCollection(userId: $userId, type: $type) {
                lists {
                    entries {
                        progress
                        updatedAt
                        media {
                            id
                            duration
                            format
                        }
                    }
                }
            }
        }";

        private const string mediaEntryQuery = @"
        query ($id: Int) {
            Media(id: $id) {
                duration
                mediaListEntry {
                    progress
                    updatedAt
                }
            }
        }";

        static AniListClient()
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PlaytimeAniList/1.0 (Playnite extension)");
        }

        public string SyncLibrary(AniListWatchTimeSettings pluginSettings, IPlayniteAPI api, SyncWindow window)
        {
            var token = ResolveToken(pluginSettings, api);
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Não foi possível obter o token do AniList." + Environment.NewLine +
                       "Confira se o 'Importer for AniList' está autenticado ou informe um token em Configurações.";
            }

            var userId = GetUserId(token);
            if (userId <= 0)
            {
                return "Não foi possível identificar a conta do AniList. Verifique o token atual.";
            }

            var entries = GetMediaList(token, userId);
            if (entries == null)
            {
                return "Falha ao buscar a lista de animes no AniList. Tente novamente.";
            }

            var cutoff = GetCutoff(window);
            var secondsByMediaId = new Dictionary<long, long>();
            foreach (var entry in entries)
            {
                if (entry?.Media == null)
                {
                    continue;
                }

                if (cutoff.HasValue && (entry.UpdatedAt ?? 0) < cutoff.Value)
                {
                    continue;
                }

                var seconds = CalculateSeconds(entry.Progress, entry.Media.Duration);
                if (seconds > 0)
                {
                    secondsByMediaId[entry.Media.Id] = seconds;
                }
            }

            var animeGames = api.Database.Games.Where(g => g.PluginId == ImporterPluginId).ToList();
            var updated = 0;
            var noLink = 0;
            var skipped = 0;

            using (api.Database.BufferedUpdate())
            {
                foreach (var game in animeGames)
                {
                    var mediaId = GetMediaIdFromGame(game);
                    if (mediaId == null)
                    {
                        noLink++;
                        continue;
                    }

                    if (!secondsByMediaId.TryGetValue(mediaId.Value, out var seconds))
                    {
                        skipped++;
                        continue;
                    }

                    var target = ComputeTarget(game.Playtime, seconds, pluginSettings.AccumulateToExisting);
                    if (target != game.Playtime)
                    {
                        game.Playtime = target;
                        api.Database.Games.Update(game);
                        updated++;
                    }
                }
            }

            return BuildLibrarySummary(window, entries.Count, animeGames.Count, secondsByMediaId.Count, updated, noLink, skipped);
        }

        public string SyncSelectedGames(AniListWatchTimeSettings pluginSettings, IPlayniteAPI api, List<Game> games)
        {
            var token = ResolveToken(pluginSettings, api);
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Não foi possível obter o token do AniList." + Environment.NewLine +
                       "Confira se o 'Importer for AniList' está autenticado ou informe um token em Configurações.";
            }

            var updated = 0;
            var failed = 0;

            using (api.Database.BufferedUpdate())
            {
                foreach (var game in games)
                {
                    var mediaId = GetMediaIdFromGame(game);
                    if (mediaId == null)
                    {
                        failed++;
                        continue;
                    }

                    var media = GetMediaEntry(token, mediaId.Value);
                    if (media == null)
                    {
                        failed++;
                        continue;
                    }

                    var seconds = CalculateSeconds(media.MediaListEntry?.Progress, media.Duration);
                    if (seconds <= 0)
                    {
                        failed++;
                        continue;
                    }

                    var target = ComputeTarget(game.Playtime, seconds, pluginSettings.AccumulateToExisting);
                    if (target != game.Playtime)
                    {
                        game.Playtime = target;
                        api.Database.Games.Update(game);
                        updated++;
                    }
                }
            }

            return $"Selecionados: {games.Count} animes" + Environment.NewLine +
                   $"Atualizados: {updated}" + Environment.NewLine +
                   $"Sem dados/sem link: {failed}";
        }

        private string ResolveToken(AniListWatchTimeSettings pluginSettings, IPlayniteAPI api)
        {
            if (!string.IsNullOrWhiteSpace(pluginSettings.AccessTokenOverride))
            {
                return pluginSettings.AccessTokenOverride.Trim();
            }

            var configPath = Path.Combine(api.Paths.ExtensionsDataPath, ImporterPluginId.ToString(), "config.json");
            if (!File.Exists(configPath))
            {
                logger.Error($"Config do importer não encontrado: {configPath}");
                return null;
            }

            try
            {
                var config = Serialization.FromJsonFile<ImporterSettingsConfig>(configPath);
                return config?.AccountAccessCode;
            }
            catch (Exception e)
            {
                logger.Error(e, "Falha ao ler config.json do Importer for AniList.");
                return null;
            }
        }

        private int GetUserId(string token)
        {
            var jwtId = GetUserIdFromJwt(token);
            if (jwtId > 0)
            {
                return jwtId;
            }

            var response = Post<ViewerResponse>(token, viewerQuery, new Dictionary<string, object>());
            return response?.User?.Id ?? -1;
        }

        private static int GetUserIdFromJwt(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2)
                {
                    return -1;
                }

                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                while (payload.Length % 4 != 0)
                {
                    payload += "=";
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                var jwt = Serialization.FromJson<JwtPayload>(json);
                if (jwt?.Sub != null && int.TryParse(jwt.Sub, out var id) && id > 0)
                {
                    return id;
                }
            }
            catch (Exception e)
            {
                LogManager.GetLogger().Error(e, "Falha ao decodificar JWT do AniList.");
            }

            return -1;
        }

        private List<MediaListEntry> GetMediaList(string token, int userId)
        {
            var variables = new Dictionary<string, object>
            {
                { "userId", userId },
                { "type", "ANIME" }
            };

            var data = Post<MediaListCollectionResponse>(token, mediaListQuery, variables);
            var entries = data?.List?.Lists?
                .Where(g => g?.Entries != null)
                .SelectMany(g => g.Entries)
                .Where(e => e?.Media != null)
                .ToList();

            return entries ?? new List<MediaListEntry>();
        }

        private MediaEntryPayload GetMediaEntry(string token, long mediaId)
        {
            var variables = new Dictionary<string, object>
            {
                { "id", (int)mediaId }
            };

            return Post<MediaEntryResponse>(token, mediaEntryQuery, variables)?.Media;
        }

        private T Post<T>(string token, string query, object variables)
        {
            var postParams = new Dictionary<string, string>
            {
                { "query", query },
                { "variables", Serialization.ToJson(variables) }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Content = new StringContent(Serialization.ToJson(postParams), Encoding.UTF8, "application/json");

            try
            {
                var response = httpClient.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    logger.Error($"AniList request falhou ({(int)response.StatusCode}): {body}");
                    return default;
                }

                var parsed = Serialization.FromJson<GraphQlResponse<T>>(body);
                if (parsed?.Errors != null && parsed.Errors.Count > 0)
                {
                    logger.Error("AniList retornou erros: " + string.Join("; ", parsed.Errors.Where(e => e != null).Select(e => e.Message)));
                    return default;
                }

                return parsed == null ? default : parsed.Data;
            }
            catch (Exception e)
            {
                logger.Error(e, "Erro durante request ao AniList.");
                return default;
            }
        }

        private long? GetMediaIdFromGame(Game game)
        {
            if (game?.Links == null)
            {
                return null;
            }

            foreach (var link in game.Links)
            {
                if (string.IsNullOrEmpty(link?.Url))
                {
                    continue;
                }

                var match = anilistAnimeIdRegex.Match(link.Url);
                if (match.Success && long.TryParse(match.Groups[1].Value, out var id))
                {
                    return id;
                }
            }

            return null;
        }

        private static long CalculateSeconds(int? progress, int? duration)
        {
            if (!progress.HasValue || progress.Value <= 0 || !duration.HasValue || duration.Value <= 0)
            {
                return 0;
            }

            return (long)progress.Value * duration.Value * 60L;
        }

        private static ulong ComputeTarget(ulong currentPlaytime, long seconds, bool accumulate)
        {
            if (!accumulate)
            {
                return (ulong)seconds;
            }

            if (currentPlaytime > ulong.MaxValue - (ulong)seconds)
            {
                return ulong.MaxValue;
            }

            return currentPlaytime + (ulong)seconds;
        }

        private static long? GetCutoff(SyncWindow window)
        {
            switch (window)
            {
                case SyncWindow.LastWeek:
                    return UnixNow() - SecondsPerWeek;
                case SyncWindow.LastMonth:
                    return UnixNow() - SecondsPerMonth;
                default:
                    return null;
            }
        }

        private static long UnixNow()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static string BuildLibrarySummary(SyncWindow window, int totalEntries, int totalGames, int matched, int updated, int noLink, int skipped)
        {
            var windowText = window == SyncWindow.All ? "toda a lista"
                : window == SyncWindow.LastWeek ? "última semana" : "último mês";

            return $"Sincronização concluída ({windowText})." + Environment.NewLine + Environment.NewLine +
                   $"• Animes processados do AniList: {totalEntries}" + Environment.NewLine +
                   $"• Jogos de anime no Playnite: {totalGames}" + Environment.NewLine +
                   $"• Jogos com tempo disponível: {matched}" + Environment.NewLine +
                   $"• Tempos atualizados: {updated}" + Environment.NewLine +
                   $"• Já sincronizados/pulados: {skipped}" + Environment.NewLine +
                   $"• Jogos sem link do AniList: {noLink}";
        }
    }
}