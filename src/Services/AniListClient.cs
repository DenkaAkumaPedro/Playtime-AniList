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

    public enum SyncScope
    {
        Anime,
        Manga,
        Both
    }

    public class AniListClient
    {
        public static readonly Guid ImporterPluginId = Guid.Parse("2366fb38-bf25-45ea-9a78-dcc797ee83c3");

        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private const long SecondsPerWeek = 7L * 24L * 60L * 60L;
        private const long SecondsPerMonth = 30L * 24L * 60L * 60L;

        private const double MinPagesPerChapter = 6d;
        private const double MaxPagesPerChapter = 120d;

        private static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };

        private readonly ILogger logger = LogManager.GetLogger();
        private readonly Regex anilistAnimeIdRegex = new Regex(@"(?:anilist\.co/(?:anime|manga)|anilist\.co)/?(\d+)", RegexOptions.IgnoreCase);

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
                        progressVolumes
                        updatedAt
                        media {
                            id
                            type
                            duration
                            format
                            chapters
                            volumes
                            countryOfOrigin
                        }
                    }
                }
            }
        }";

        private const string mediaEntryQuery = @"
        query ($id: Int) {
            Media(id: $id) {
                type
                duration
                format
                chapters
                volumes
                countryOfOrigin
                mediaListEntry {
                    progress
                    progressVolumes
                    updatedAt
                }
            }
        }";

        static AniListClient()
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PlaytimeAniList/1.1 (Playnite extension)");
        }

        public string SyncLibrary(AniListWatchTimeSettings pluginSettings, IPlayniteAPI api, SyncWindow window, SyncScope scope)
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

            var wantsAnime = scope == SyncScope.Anime || scope == SyncScope.Both;
            var wantsManga = scope == SyncScope.Manga || scope == SyncScope.Both;

            var entries = new List<MediaListEntry>();
            if (wantsAnime)
            {
                entries.AddRange(GetMediaList(token, userId, "ANIME"));
            }

            if (wantsManga)
            {
                entries.AddRange(GetMediaList(token, userId, "MANGA"));
            }

            if (entries.Count == 0)
            {
                return "Nenhum item foi retornado pelo AniList para o escopo selecionado.";
            }

            var cutoff = GetCutoff(window);
            var secondsByMediaId = new Dictionary<long, long>();
            var mangaMediaIds = new HashSet<long>();
            var activityByMediaId = new Dictionary<long, long>();

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

                var isManga = string.Equals(entry.Media.Type, "MANGA", StringComparison.OrdinalIgnoreCase);
                if (isManga)
                {
                    mangaMediaIds.Add(entry.Media.Id);
                }

                if (entry.UpdatedAt > 0)
                {
                    activityByMediaId[entry.Media.Id] = entry.UpdatedAt.Value;
                }

                var seconds = isManga
                    ? CalculateMangaSeconds(entry.Progress, entry.Media, pluginSettings)
                    : CalculateAnimeSeconds(entry.Progress, entry.Media.Duration);

                if (seconds > 0)
                {
                    secondsByMediaId[entry.Media.Id] = seconds;
                }
            }

            var animeGames = api.Database.Games.Where(g => g.PluginId == ImporterPluginId).ToList();
            var updated = 0;
            var noLink = 0;
            var skipped = 0;
            var mangaMatched = 0;
            var mangaSecondsAdded = 0L;
            var activityChanged = 0;

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

                    secondsByMediaId.TryGetValue(mediaId.Value, out var seconds);

                    var isMangaEntry = mangaMediaIds.Contains(mediaId.Value);
                    var previousPlaytime = game.Playtime;
                    var target = ComputeTarget(previousPlaytime, seconds, pluginSettings.AccumulateToExisting);
                    var changed = false;

                    if (seconds > 0 && target != previousPlaytime)
                    {
                        game.Playtime = target;
                        if (isMangaEntry && target > previousPlaytime)
                        {
                            mangaSecondsAdded += (long)(target - previousPlaytime);
                        }

                        changed = true;
                        updated++;
                    }

                    if (isMangaEntry)
                    {
                        mangaMatched++;
                    }

                    if (seconds == 0)
                    {
                        skipped++;
                    }

                    var activityChanges = ApplyActivityData(game, activityByMediaId, mediaId.Value, seconds > 0);
                    changed |= activityChanges > 0;
                    activityChanged += activityChanges;

                    if (changed)
                    {
                        api.Database.Games.Update(game);
                    }
                }
            }

            return BuildLibrarySummary(window, scope, entries.Count, animeGames.Count, secondsByMediaId.Count,
                updated, noLink, skipped, mangaMatched, mangaSecondsAdded, activityChanged);
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
            var animeUpdated = 0;
            var mangaUpdated = 0;

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

                    var isManga = string.Equals(media.Type, "MANGA", StringComparison.OrdinalIgnoreCase);
                    long seconds;
                    if (isManga)
                    {
                        if (!pluginSettings.SyncManga)
                        {
                            failed++;
                            continue;
                        }

                        seconds = CalculateMangaSeconds(media.MediaListEntry?.Progress, media, pluginSettings);
                    }
                    else
                    {
                        if (!pluginSettings.SyncAnime)
                        {
                            failed++;
                            continue;
                        }

                        seconds = CalculateAnimeSeconds(media.MediaListEntry?.Progress, media.Duration);
                    }

                    if (seconds <= 0)
                    {
                        failed++;
                        continue;
                    }

                    var target = ComputeTarget(game.Playtime, seconds, pluginSettings.AccumulateToExisting);
                    var changed = false;
                    if (target != game.Playtime)
                    {
                        game.Playtime = target;
                        changed = true;
                        updated++;
                        if (isManga)
                        {
                            mangaUpdated++;
                        }
                        else
                        {
                            animeUpdated++;
                        }
                    }

                    var activity = new Dictionary<long, long>
                    {
                        { mediaId.Value, media.MediaListEntry?.UpdatedAt ?? 0 }
                    };
                    changed |= ApplyActivityData(game, activity, mediaId.Value, true) > 0;

                    if (changed)
                    {
                        api.Database.Games.Update(game);
                    }
                }
            }

            return $"Selecionados: {games.Count} itens" + Environment.NewLine +
                   $"Atualizados: {updated} (anime: {animeUpdated}, mangá: {mangaUpdated})" + Environment.NewLine +
                   $"Sem dados/sem link: {failed}";
        }

        private static int ApplyActivityData(Game game, Dictionary<long, long> activityByMediaId, long mediaId, bool hasProgress)
        {
            var changes = 0;

            if (activityByMediaId.TryGetValue(mediaId, out var unix) && unix > 0)
            {
                var updatedAt = DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime;
                if (game.LastActivity == null || updatedAt > game.LastActivity.Value)
                {
                    game.LastActivity = updatedAt;
                    changes++;
                }
            }

            if (hasProgress && game.PlayCount == 0)
            {
                game.PlayCount = 1;
                changes++;
            }

            return changes;
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

        private List<MediaListEntry> GetMediaList(string token, int userId, string mediaType)
        {
            var variables = new Dictionary<string, object>
            {
                { "userId", userId },
                { "type", mediaType }
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
            if (game == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(game.GameId) &&
                long.TryParse(game.GameId.Trim(), out var gameId) &&
                gameId > 0)
            {
                return gameId;
            }

            if (game.Links == null)
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

        private static long CalculateAnimeSeconds(int? progress, int? duration)
        {
            if (!progress.HasValue || progress.Value <= 0 || !duration.HasValue || duration.Value <= 0)
            {
                return 0;
            }

            return (long)progress.Value * duration.Value * 60L;
        }

        public static long CalculateMangaSeconds(int? chaptersRead, MediaSummary media, AniListWatchTimeSettings settings)
        {
            return CalculateMangaSeconds(chaptersRead, media?.Format, media?.Chapters, media?.Volumes, media?.CountryOfOrigin, settings);
        }

        public static long CalculateMangaSeconds(int? chaptersRead, MediaEntryPayload media, AniListWatchTimeSettings settings)
        {
            return CalculateMangaSeconds(chaptersRead, media?.Format, media?.Chapters, media?.Volumes, media?.CountryOfOrigin, settings);
        }

        private static long CalculateMangaSeconds(int? chaptersRead, string format, int? totalChapters, int? totalVolumes, string countryOfOrigin, AniListWatchTimeSettings settings)
        {
            if (!chaptersRead.HasValue || chaptersRead.Value <= 0)
            {
                return 0;
            }

            var chapters = chaptersRead.Value;
            var formatName = (format ?? string.Empty).Trim().ToUpperInvariant();
            var origin = (countryOfOrigin ?? string.Empty).Trim().ToUpperInvariant();

            if (formatName == "ONE_SHOT")
            {
                return (long)Math.Round(chapters * (double)settings.GetPagesPerVolume() * settings.GetSecondsPerPage());
            }

            if (formatName == "NOVEL")
            {
                return chapters * settings.GetNovelMinutesPerChapter() * 60L;
            }

            if (origin == "KR" || origin == "CN")
            {
                return chapters * settings.GetWebtoonMinutesPerChapter() * 60L;
            }

            if (totalChapters.HasValue && totalVolumes.HasValue && totalChapters.Value > 0 && totalVolumes.Value > 0)
            {
                var pagesPerChapter = settings.GetPagesPerVolume() * (double)totalVolumes.Value / totalChapters.Value;
                pagesPerChapter = Math.Min(MaxPagesPerChapter, Math.Max(MinPagesPerChapter, pagesPerChapter));
                return (long)Math.Round(chapters * pagesPerChapter * settings.GetSecondsPerPage());
            }

            return chapters * settings.GetFallbackMinutesPerChapter() * 60L;
        }

        private static ulong ComputeTarget(ulong currentPlaytime, long seconds, bool accumulate)
        {
            if (seconds <= 0)
            {
                return currentPlaytime;
            }

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

        private static string BuildLibrarySummary(SyncWindow window, SyncScope scope, int totalEntries, int totalGames, int matched,
            int updated, int noLink, int skipped, int mangaMatched, long mangaSeconds, int activityUpdated)
        {
            var windowText = window == SyncWindow.All ? "toda a lista"
                : window == SyncWindow.LastWeek ? "última semana" : "último mês";

            var scopeText = scope == SyncScope.Anime ? "anime"
                : scope == SyncScope.Manga ? "mangá" : "anime e mangá";

            var summary = $"Sincronização concluída ({windowText}, {scopeText})." + Environment.NewLine + Environment.NewLine +
                          $"• Itens processados do AniList: {totalEntries}" + Environment.NewLine +
                          $"• Jogos de anime/mangá no Playnite: {totalGames}" + Environment.NewLine +
                          $"• Jogos com tempo disponível: {matched}" + Environment.NewLine +
                          $"• Tempos atualizados: {updated}" + Environment.NewLine +
                          $"• Mangás com tempo calculado: {mangaMatched} ({FormatHours(mangaSeconds)})" + Environment.NewLine +
                          $"• Atividade (última data) atualizada: {activityUpdated}" + Environment.NewLine +
                          $"• Já sincronizados/pulados: {skipped}" + Environment.NewLine +
                          $"• Jogos sem link do AniList: {noLink}";

            if (scope == SyncScope.Both)
            {
                summary += Environment.NewLine + Environment.NewLine +
                    "O tempo de anime e de mangá somado aparece em Playnite ▸ Estatísticas (Tempo total, Tempo médio, Mais jogados).";
            }

            return summary;
        }

        private static string FormatHours(long seconds)
        {
            if (seconds <= 0)
            {
                return "0h";
            }

            return string.Format("{0:0.0}h", TimeSpan.FromSeconds(seconds).TotalHours);
        }
    }
}
