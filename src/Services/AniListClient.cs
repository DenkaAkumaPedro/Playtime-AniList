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

    public class SyncResult
    {
        public SyncResult(string message, bool failed)
        {
            Message = message;
            Failed = failed;
        }

        public string Message { get; }

        public bool Failed { get; }
    }

    public class PendingSession
    {
        public Guid GameId { get; set; }

        public long Seconds { get; set; }

        public DateTime Date { get; set; }
    }

    public class LibrarySyncCounts
    {
        public int Updated { get; set; }
        public int Zeroed { get; set; }
        public int NoLink { get; set; }
        public int NotReturned { get; set; }
        public int OutOfWindow { get; set; }
        public int Skipped { get; set; }
        public int MangaMatched { get; set; }
        public int MangaWithTime { get; set; }
        public int ActivityChanged { get; set; }
        public long MangaSeconds { get; set; }

        public int SessionsWritten { get; set; }
    }

    public class AniListClient
    {
        public static readonly Guid ImporterPluginId = Guid.Parse("2366fb38-bf25-45ea-9a78-dcc797ee83c3");

        private const string GraphQlEndpoint = "https://graphql.anilist.co";
        private const long SecondsPerWeek = 7L * 24L * 60L * 60L;
        private const long SecondsPerMonth = 30L * 24L * 60L * 60L;

        private const double MinPagesPerChapter = 6d;
        private const double MaxPagesPerChapter = 120d;

        // Acima disso, uma consulta Media(id) por item vira centenas de requisicoes em
        // sequencia dentro do ActivateGlobalProgress, o AniList comeca a responder 429 e
        // tudo acaba virando "sem dados". Nesse tamanho vale a consulta da lista inteira.
        private const int IndividualMediaQueryLimit = 30;

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
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AniListPlayTime/1.4 (Playnite extension)");
        }

        public SyncResult SyncLibrary(AniListWatchTimeSettings pluginSettings, IPlayniteAPI api, SyncWindow window, SyncScope scope,
            Dictionary<string, long> appliedSeconds)
        {
            var token = ResolveToken(pluginSettings, api);
            if (string.IsNullOrWhiteSpace(token))
            {
                return new SyncResult("Não foi possível obter o token do AniList." + Environment.NewLine +
                       "Confira se o 'Importer for AniList' está autenticado ou informe um token em Configurações.", true);
            }

            var userId = GetUserId(token);
            if (userId <= 0)
            {
                return new SyncResult("Não foi possível identificar a conta do AniList. Verifique o token atual.", true);
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
                return new SyncResult("Nenhum item foi retornado pelo AniList para o escopo selecionado.", true);
            }

            var cutoff = GetCutoff(window);
            var secondsByMediaId = new Dictionary<long, long>();
            var returnedIds = new HashSet<long>();
            var evaluableIds = new HashSet<long>();
            var mangaMediaIds = new HashSet<long>();
            var activityByMediaId = new Dictionary<long, long>();

            foreach (var entry in entries)
            {
                if (entry?.Media == null)
                {
                    continue;
                }

                var entryMediaId = entry.Media.Id;

                // Preenchido antes do filtro de janela: distingue "o AniList nao falou deste item"
                // de "o AniList disse que este item tem progresso zero".
                returnedIds.Add(entryMediaId);

                var isManga = string.Equals(entry.Media.Type, "MANGA", StringComparison.OrdinalIgnoreCase);
                if (isManga)
                {
                    mangaMediaIds.Add(entryMediaId);
                }

                if (cutoff.HasValue && (entry.UpdatedAt ?? 0) < cutoff.Value)
                {
                    continue;
                }

                evaluableIds.Add(entryMediaId);

                if (entry.UpdatedAt > 0)
                {
                    activityByMediaId[entryMediaId] = entry.UpdatedAt.Value;
                }

                var seconds = isManga
                    ? CalculateMangaSeconds(entry.Progress, entry.Media, pluginSettings)
                    : CalculateAnimeSeconds(entry.Progress, entry.Media.Duration);

                if (seconds > 0)
                {
                    secondsByMediaId[entryMediaId] = seconds;
                }
            }

            var animeGames = api.Database.Games.Where(g => g.PluginId == ImporterPluginId).ToList();
            var counts = new LibrarySyncCounts();
            var seenGameIds = new HashSet<string>();
            var pendingSessions = new List<PendingSession>();
            var wantsActivitySessions = pluginSettings.WriteActivitySessions;
            var sessionMode = pluginSettings.SessionMode;

            using (api.Database.BufferedUpdate())
            {
                foreach (var game in animeGames)
                {
                    // Marcado antes de qualquer filtro de propósito. A podagem do state.json
                    // existe para remover jogo que saiu da biblioteca; se so marcasse o que
                    // chegou ao fim do laco, uma sync de escopo parcial (so anime, com mangá
                    // desligado) ou uma resposta incompleta da API apagariam a linha de base
                    // do resto, e a proxima sync somaria o tempo da extensao por cima dele
                    // mesmo. Era a mesma inflacao que a 1.2 corrigiu.
                    seenGameIds.Add(game.Id.ToString("N"));

                    var mediaId = GetMediaIdFromGame(game);
                    if (mediaId == null)
                    {
                        counts.NoLink++;
                        continue;
                    }

                    if (!returnedIds.Contains(mediaId.Value))
                    {
                        counts.NotReturned++;
                        continue;
                    }

                    if (mangaMediaIds.Contains(mediaId.Value))
                    {
                        counts.MangaMatched++;
                    }

                    if (!evaluableIds.Contains(mediaId.Value))
                    {
                        counts.OutOfWindow++;
                        continue;
                    }

                    secondsByMediaId.TryGetValue(mediaId.Value, out var seconds);
                    var baselineKey = game.Id.ToString("N");
                    appliedSeconds.TryGetValue(baselineKey, out var lastApplied);

                    var isMangaEntry = mangaMediaIds.Contains(mediaId.Value);
                    var previousPlaytime = game.Playtime;
                    var target = ComputeTarget(previousPlaytime, seconds, pluginSettings.AccumulateToExisting, lastApplied);
                    var changed = false;

                    if (target != previousPlaytime)
                    {
                        game.Playtime = target;
                        changed = true;
                        counts.Updated++;

                        if (seconds <= 0)
                        {
                            counts.Zeroed++;
                        }
                    }

                    if (seconds > 0)
                    {
                        appliedSeconds[baselineKey] = seconds;

                        if (isMangaEntry)
                        {
                            counts.MangaWithTime++;
                            counts.MangaSeconds += seconds;
                        }
                    }
                    else
                    {
                        // A contribuicao da extensao voltou a zero: o registro deixa de valer.
                        if (lastApplied > 0)
                        {
                            appliedSeconds.Remove(baselineKey);
                        }

                        counts.Skipped++;
                    }

                    var activityChanges = ApplyActivityData(game, activityByMediaId, mediaId.Value, seconds > 0);
                    changed |= activityChanges > 0;
                    counts.ActivityChanged += activityChanges;

                    if (changed)
                    {
                        api.Database.Games.Update(game);
                    }

                    if (wantsActivitySessions &&
                        activityByMediaId.TryGetValue(mediaId.Value, out var sessionUnix) && sessionUnix > 0)
                    {
                        long sessionSeconds;
                        if (sessionMode == SessionMode.Incremental)
                        {
                            long delta = seconds - lastApplied;
                            if (delta > 0)
                            {
                                sessionSeconds = delta;
                            }
                            else
                            {
                                sessionSeconds = 0;
                            }
                        }
                        else
                        {
                            sessionSeconds = seconds;
                        }

                        if (sessionSeconds > 0)
                        {
                            pendingSessions.Add(new PendingSession
                            {
                                GameId = game.Id,
                                Seconds = sessionSeconds,
                                Date = DateTimeOffset.FromUnixTimeSeconds(sessionUnix).UtcDateTime
                            });
                        }
                    }
                }
            }

            PruneBaseline(appliedSeconds, seenGameIds);

            // Fora do BufferedUpdate: o GameActivity tem o proprio banco e nao deve
            //participar da transacao do Playnite.
            if (sessionMode == SessionMode.Incremental)
            {
                counts.SessionsWritten = GameActivityWriter.AddSessions(api, pendingSessions, logger);
            }
            else
            {
                counts.SessionsWritten = GameActivityWriter.WriteSessions(api, pendingSessions, logger);
            }

            return new SyncResult(
                BuildLibrarySummary(window, scope, entries.Count, animeGames.Count, secondsByMediaId.Count, counts),
                false);
        }

        public SyncResult SyncSelectedGames(AniListWatchTimeSettings pluginSettings, IPlayniteAPI api, List<Game> games,
            Dictionary<string, long> appliedSeconds)
        {
            var token = ResolveToken(pluginSettings, api);
            if (string.IsNullOrWhiteSpace(token))
            {
                return new SyncResult("Não foi possível obter o token do AniList." + Environment.NewLine +
                       "Confira se o 'Importer for AniList' está autenticado ou informe um token em Configurações.", true);
            }

            var userId = GetUserId(token);
            if (userId <= 0)
            {
                return new SyncResult("Não foi possível identificar a conta do AniList. Verifique o token atual.", true);
            }

            // Com muitos itens selecionados, uma consulta Media(id) por jogo estoura o
            // limite do AniList e devolve 429, o que vira "sem dados" em tudo. Nesse
            // tamanho a consulta da lista inteira resolve de uma vez.
            var useBatch = games.Count > IndividualMediaQueryLimit;
            Dictionary<long, BatchEntry> batch = null;
            if (useBatch)
            {
                batch = new Dictionary<long, BatchEntry>();
                if (pluginSettings.SyncAnime)
                {
                    CollectBatch(token, userId, "ANIME", pluginSettings, batch);
                }

                if (pluginSettings.SyncManga)
                {
                    CollectBatch(token, userId, "MANGA", pluginSettings, batch);
                }

                if (batch.Count == 0)
                {
                    return new SyncResult("Nenhum item foi retornado pelo AniList. Nada foi alterado.", true);
                }
            }

            var updated = 0;
            var failed = 0;
            var animeUpdated = 0;
            var mangaUpdated = 0;
            var pendingSessions = new List<PendingSession>();
            var wantsActivitySessions = pluginSettings.WriteActivitySessions;
            var sessionMode = pluginSettings.SessionMode;

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

                    var baselineKey = game.Id.ToString("N");
                    appliedSeconds.TryGetValue(baselineKey, out var lastApplied);

                    long seconds;
                    long sessionUnix;
                    bool isManga;

                    if (useBatch)
                    {
                        if (!batch.TryGetValue(mediaId.Value, out var batchEntry))
                        {
                            failed++;
                            continue;
                        }

                        seconds = batchEntry.Seconds;
                        sessionUnix = batchEntry.UpdatedAt;
                        isManga = batchEntry.IsManga;
                    }
                    else
                    {
                        var media = GetMediaEntry(token, mediaId.Value);
                        if (media == null)
                        {
                            failed++;
                            continue;
                        }

                        isManga = string.Equals(media.Type, "MANGA", StringComparison.OrdinalIgnoreCase);
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

                        sessionUnix = media.MediaListEntry?.UpdatedAt ?? 0;
                    }

                    if (seconds <= 0)
                    {
                        // A contribuicao da extensao voltou a zero: o registro deixa de valer.
                        // Nao pode pular com continue, senao o selected sync nunca limpa o
                        // tempo proprio nem a sessao antiga; o ComputeTarget abaixo resolve os
                        // dois casos, igual ao caminho da biblioteca.
                        if (lastApplied > 0)
                        {
                            appliedSeconds.Remove(baselineKey);
                        }
                    }

                    var target = ComputeTarget(game.Playtime, seconds, pluginSettings.AccumulateToExisting, lastApplied);
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

                    if (seconds > 0)
                    {
                        appliedSeconds[baselineKey] = seconds;
                    }

                    var activity = new Dictionary<long, long>
                    {
                        { mediaId.Value, sessionUnix }
                    };
                    changed |= ApplyActivityData(game, activity, mediaId.Value, seconds > 0) > 0;

                    if (changed)
                    {
                        api.Database.Games.Update(game);
                    }

                    if (wantsActivitySessions && sessionUnix > 0)
                    {
                        long sessionSeconds;
                        if (sessionMode == SessionMode.Incremental)
                        {
                            long delta = seconds - lastApplied;
                            if (delta > 0)
                            {
                                sessionSeconds = delta;
                            }
                            else
                            {
                                sessionSeconds = 0;
                            }
                        }
                        else
                        {
                            sessionSeconds = seconds;
                        }

                        if (sessionSeconds > 0)
                        {
                            pendingSessions.Add(new PendingSession
                            {
                                GameId = game.Id,
                                Seconds = sessionSeconds,
                                Date = DateTimeOffset.FromUnixTimeSeconds(sessionUnix).UtcDateTime
                            });
                        }
                    }
                }
            }

            int sessionsWritten;
            if (sessionMode == SessionMode.Incremental)
            {
                sessionsWritten = GameActivityWriter.AddSessions(api, pendingSessions, logger);
            }
            else
            {
                sessionsWritten = GameActivityWriter.WriteSessions(api, pendingSessions, logger);
            }

            var summary = $"Selecionados: {games.Count} itens" + Environment.NewLine +
                          $"Atualizados: {updated} (anime: {animeUpdated}, mangá: {mangaUpdated})" + Environment.NewLine +
                          $"Sem dados/sem link: {failed}";

            if (wantsActivitySessions)
            {
                summary += Environment.NewLine + $"Sessões gravadas no GameActivity: {sessionsWritten}";
            }

            return new SyncResult(summary, false);
        }

        private void CollectBatch(string token, int userId, string mediaType, AniListWatchTimeSettings settings,
            Dictionary<long, BatchEntry> target)
        {
            foreach (var entry in GetMediaList(token, userId, mediaType))
            {
                if (entry?.Media == null)
                {
                    continue;
                }

                var isManga = string.Equals(entry.Media.Type, "MANGA", StringComparison.OrdinalIgnoreCase);
                target[entry.Media.Id] = new BatchEntry
                {
                    IsManga = isManga,
                    UpdatedAt = entry.UpdatedAt ?? 0,
                    Seconds = isManga
                        ? CalculateMangaSeconds(entry.Progress, entry.Media, settings)
                        : CalculateAnimeSeconds(entry.Progress, entry.Media.Duration)
                };
            }
        }

        private struct BatchEntry
        {
            public bool IsManga { get; set; }

            public long UpdatedAt { get; set; }

            public long Seconds { get; set; }
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

        private static ulong ComputeTarget(ulong currentPlaytime, long seconds, bool accumulate, long lastApplied)
        {
            // lastApplied e o que a extensao escreveu por ultimo. Sem ele nao da para saber
            // quanto do tempo atual e nosso, entao nunca apagamos tempo de origem desconhecida.
            var hasBaseline = lastApplied > 0 && (ulong)lastApplied <= currentPlaytime;
            var foreign = hasBaseline ? currentPlaytime - (ulong)lastApplied : currentPlaytime;

            if (seconds <= 0)
            {
                // Progresso zero no AniList: remove apenas a contribuicao da extensao.
                return hasBaseline ? foreign : currentPlaytime;
            }

            if (!accumulate)
            {
                return (ulong)seconds;
            }

            if (hasBaseline)
            {
                // Substitui a contribuicao anterior em vez de somar por cima dela.
                if (foreign > ulong.MaxValue - (ulong)seconds)
                {
                    return ulong.MaxValue;
                }

                return foreign + (ulong)seconds;
            }

            if (foreign > ulong.MaxValue - (ulong)seconds)
            {
                return ulong.MaxValue;
            }

            return foreign + (ulong)seconds;
        }

        private static void PruneBaseline(Dictionary<string, long> appliedSeconds, HashSet<string> seenGameIds)
        {
            List<string> stale = null;
            foreach (var pair in appliedSeconds)
            {
                if (!seenGameIds.Contains(pair.Key))
                {
                    (stale ?? (stale = new List<string>())).Add(pair.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (var key in stale)
            {
                appliedSeconds.Remove(key);
            }
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

        private static string BuildLibrarySummary(SyncWindow window, SyncScope scope, int totalEntries, int totalGames,
            int withTime, LibrarySyncCounts counts)
        {
            var windowText = window == SyncWindow.All ? "toda a lista"
                : window == SyncWindow.LastWeek ? "última semana" : "último mês";

            var scopeText = scope == SyncScope.Anime ? "anime"
                : scope == SyncScope.Manga ? "mangá" : "anime e mangá";

            var summary = $"Sincronização concluída ({windowText}, {scopeText})." + Environment.NewLine + Environment.NewLine +
                          $"• Itens processados do AniList: {totalEntries}" + Environment.NewLine +
                          $"• Jogos de anime/mangá no Playnite: {totalGames}" + Environment.NewLine +
                          $"• Itens do AniList com tempo: {withTime}" + Environment.NewLine +
                          $"• Mangás com tempo na lista: {counts.MangaWithTime} de {counts.MangaMatched}" + Environment.NewLine +
                          $"• Tempo total de mangá: {FormatHours(counts.MangaSeconds)}" + Environment.NewLine +
                          $"• Tempos alterados nesta rodada: {counts.Updated}";

            if (counts.Zeroed > 0)
            {
                summary += Environment.NewLine + $"• Tempo zerado (progresso 0 no AniList): {counts.Zeroed}";
            }

            summary += Environment.NewLine +
                       $"• Atividade (última data) atualizada: {counts.ActivityChanged}";

            if (counts.Skipped > 0)
            {
                summary += Environment.NewLine + $"• Sem tempo (progresso 0 ou sem duração): {counts.Skipped}";
            }

            summary += Environment.NewLine + $"• Fora da janela de sincronização: {counts.OutOfWindow}";

            if (counts.NotReturned > 0)
            {
                summary += Environment.NewLine + $"• Não retornados pelo AniList: {counts.NotReturned}";
            }

summary += Environment.NewLine +
                       $"• Jogos sem link do AniList: {counts.NoLink}";

            if (counts.SessionsWritten > 0)
            {
                summary += Environment.NewLine + $"• Sessões gravadas no GameActivity: {counts.SessionsWritten}";
            }

            if (counts.ActivityChanged == 0)
            {
                summary += Environment.NewLine + Environment.NewLine +
                    "Atividade em 0 é o normal quando o 'Importer for AniList' está com " +
                    "'Atualizar última atividade ao atualizar a biblioteca' ligado: ele grava essa data antes desta extensão.";
            }

            if (counts.NotReturned > 0)
            {
                summary += Environment.NewLine +
                    "Itens não retornados pelo AniList tiveram o tempo preservado, para não apagar nada por causa de resposta incompleta.";
            }

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
