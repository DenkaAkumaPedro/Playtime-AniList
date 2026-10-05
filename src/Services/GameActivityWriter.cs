using Playnite.SDK;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AniListWatchTime.Services
{
    public static class GameActivityWriter
    {
        private const string GameActivityAddonId = "playnite-gameactivity-plugin";
        private const string GameActivityAddonType = "GameActivity.GameActivity";
        private const string SessionActionName = "AniList";

        private const BindingFlags InstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Fixo de proposito: e o que torna a escrita idempotente. Cada sessao da extensao
        // e sobrescrita no lugar, nunca anexada, entao rodar a sync dez vezes nao cria
        // dez sessoes. Um SourceID novo por execucao transformaria o GameActivity num
        // inflador de tempo.
        private static readonly Guid SessionSourceId = new Guid("7f2c1e94-5b0a-4d63-9e18-3c6a5d84b2f1");

        private static bool warnedUnavailable;

        public static int WriteSessions(IPlayniteAPI api, List<PendingSession> sessions, ILogger logger)
        {
            if (sessions == null || sessions.Count == 0)
            {
                return 0;
            }

            try
            {
                var database = ResolveDatabase(api);
                if (database == null)
                {
                    WarnUnavailable(logger, "o plugin GameActivity (" + GameActivityAddonId + ") não está instalado ou a estrutura dele mudou; as sessões não foram gravadas.");
                    return 0;
                }

                var databaseType = database.Database.GetType();
                var interfaceType = databaseType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition().Name == "IPluginDatabase`1");
                if (interfaceType == null)
                {
                    WarnUnavailable(logger, "o GameActivity expôs um banco sem a interface IPluginDatabase<T>; as sessões não foram gravadas.");
                    return 0;
                }

                // Os metodos genericos so existem na interface. A interface ja vem fechada
                // (IPluginDatabase<GameActivities>), entao Get/AddOrUpdate ja estao
                // materializados com GameActivities: nao ha MakeGenericMethod aqui, e filtrar
                // por IsGenericMethodDefinition nao encontraria nada.
                var itemType = interfaceType.GetGenericArguments()[0];
                var getMethod = interfaceType.GetMethods()
                    .FirstOrDefault(m => m.Name == "Get" && m.GetParameters().Length == 3 &&
                        m.GetParameters()[0].ParameterType == typeof(Guid));
                var addOrUpdate = interfaceType.GetMethods()
                    .FirstOrDefault(m => m.Name == "AddOrUpdate" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType == itemType);
                if (getMethod == null || addOrUpdate == null)
                {
                    WarnUnavailable(logger, "as assinaturas Get/AddOrUpdate do GameActivity mudaram; as sessões não foram gravadas.");
                    return 0;
                }

                var written = 0;
                foreach (var session in sessions)
                {
                    var entry = getMethod.Invoke(database.Database, new object[] { session.GameId, false, false });
                    if (entry == null)
                    {
                        logger.Warn($"GameActivity: nenhum registro para o jogo {session.GameId}, sessão ignorada.");
                        continue;
                    }

                    if (UpsertSession(entry, itemType, session))
                    {
                        addOrUpdate.Invoke(database.Database, new object[] { entry });
                        written++;
                    }
                }

                if (written > 0)
                {
                    warnedUnavailable = false;
                }

                return written;
            }
            catch (Exception e)
            {
                // Falha aqui nunca pode derrubar a sync: o Playtime ja foi gravado no banco
                // do Playnite e as sessoes sao um extra opt-in.
                logger.Error(e, "Falha ao gravar sessões no GameActivity.");
return 0;
        }
    }

    public static int AddSessions(IPlayniteAPI api, List<PendingSession> sessions, ILogger logger)
    {
        if (sessions == null || sessions.Count == 0)
        {
            return 0;
        }

        try
        {
            var database = ResolveDatabase(api);
            if (database == null)
            {
                WarnUnavailable(logger, "o plugin GameActivity (" + GameActivityAddonId + ") não está instalado ou a estrutura dele mudou; as sessões não foram gravadas.");
                return 0;
            }

            var databaseType = database.Database.GetType();
            var interfaceType = databaseType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition().Name == "IPluginDatabase`1");
            if (interfaceType == null)
            {
                WarnUnavailable(logger, "o GameActivity expôs um banco sem a interface IPluginDatabase<T>; as sessões não foram gravadas.");
                return 0;
            }

            var itemType = interfaceType.GetGenericArguments()[0];
            var getMethod = interfaceType.GetMethods()
                .FirstOrDefault(m => m.Name == "Get" && m.GetParameters().Length == 3 &&
                    m.GetParameters()[0].ParameterType == typeof(Guid));
            var addOrUpdate = interfaceType.GetMethods()
                .FirstOrDefault(m => m.Name == "AddOrUpdate" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == itemType);
            if (getMethod == null || addOrUpdate == null)
            {
                WarnUnavailable(logger, "as assinaturas Get/AddOrUpdate do GameActivity mudaram; as sessões não foram gravadas.");
                return 0;
            }

            var written = 0;
            foreach (var session in sessions)
            {
                var entry = getMethod.Invoke(database.Database, new object[] { session.GameId, false, false });
                if (entry == null)
                {
                    logger.Warn($"GameActivity: nenhum registro para o jogo {session.GameId}, sessão ignorada.");
                    continue;
                }

                if (AddSession(entry, itemType, session))
                {
                    addOrUpdate.Invoke(database.Database, new object[] { entry });
                    written++;
                }
            }

            if (written > 0)
            {
                warnedUnavailable = false;
            }

            return written;
        }
        catch (Exception e)
        {
            logger.Error(e, "Falha ao gravar sessões no GameActivity.");
            return 0;
        }
    }

    private static bool AddSession(object entry, Type itemType, PendingSession session)
    {
        var itemsProperty = itemType.GetProperty("Items", InstanceMembers);
        var items = itemsProperty?.GetValue(entry, null) as IList;
        if (items == null)
        {
            return false;
        }

        var activityType = items.GetType().GetGenericArguments()[0];
        var sourceProperty = activityType.GetProperty("SourceID", InstanceMembers);
        var elapsedProperty = activityType.GetProperty("ElapsedSeconds", InstanceMembers);
        var platformIdsProperty = activityType.GetProperty("PlatformIDs", InstanceMembers);

        if (session.Seconds > 0)
        {
            var activity = Activator.CreateInstance(activityType);
            sourceProperty?.SetValue(activity, SessionSourceId, null);

            if (platformIdsProperty != null)
            {
                var platformIds = (IList)Activator.CreateInstance(platformIdsProperty.PropertyType);
                platformIds.Add(session.GameId);
                platformIdsProperty.SetValue(activity, platformIds, null);
            }

            activityType.GetProperty("GameActionName", InstanceMembers)?.SetValue(activity, SessionActionName, null);
            activityType.GetProperty("IdConfiguration", InstanceMembers)?.SetValue(activity, 0, null);
            activityType.GetProperty("DateSession", InstanceMembers)?.SetValue(activity, session.Date, null);
            elapsedProperty?.SetValue(activity, (ulong)session.Seconds, null);

            var detailsProperty = activityType.GetProperty("Details", InstanceMembers);
            if (detailsProperty != null)
            {
                detailsProperty.SetValue(activity, Activator.CreateInstance(detailsProperty.PropertyType), null);
            }

            items.Add(activity);
        }

        return true;
    }

    private static bool UpsertSession(object entry, Type itemType, PendingSession session)
        {
            var itemsProperty = itemType.GetProperty("Items", InstanceMembers);
            var items = itemsProperty?.GetValue(entry, null) as IList;
            if (items == null)
            {
                return false;
            }

            var activityType = items.GetType().GetGenericArguments()[0];
            var sourceProperty = activityType.GetProperty("SourceID", InstanceMembers);
            var elapsedProperty = activityType.GetProperty("ElapsedSeconds", InstanceMembers);
            var platformIdsProperty = activityType.GetProperty("PlatformIDs", InstanceMembers);

            // Remove a sessao anterior desta extensao antes de criar a nova. Sem isso o
            // tempo viraria sessao a cada sync.
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var candidate = items[i];
                var candidateSource = sourceProperty?.GetValue(candidate, null);
                if (candidateSource is Guid candidateGuid && candidateGuid == SessionSourceId)
                {
                    items.RemoveAt(i);
                }
            }

            if (session.Seconds > 0)
            {
                var activity = Activator.CreateInstance(activityType);
                sourceProperty?.SetValue(activity, SessionSourceId, null);

                // PlatformIDs e o que amarra a sessao ao jogo do Playnite.
                if (platformIdsProperty != null)
                {
                    var platformIds = (IList)Activator.CreateInstance(platformIdsProperty.PropertyType);
                    platformIds.Add(session.GameId);
                    platformIdsProperty.SetValue(activity, platformIds, null);
                }

                activityType.GetProperty("GameActionName", InstanceMembers)?.SetValue(activity, SessionActionName, null);
                activityType.GetProperty("IdConfiguration", InstanceMembers)?.SetValue(activity, 0, null);
                activityType.GetProperty("DateSession", InstanceMembers)?.SetValue(activity, session.Date, null);
                elapsedProperty?.SetValue(activity, (ulong)session.Seconds, null);

                var detailsProperty = activityType.GetProperty("Details", InstanceMembers);
                if (detailsProperty != null)
                {
                    detailsProperty.SetValue(activity, Activator.CreateInstance(detailsProperty.PropertyType), null);
                }

                items.Add(activity);
            }

            // SessionPlaytime nao e gravado aqui: e uma propriedade calculada, que o proprio
            // GameActivity soma a partir de Items, e nao tem setter. Mexer nela por reflexao
            // lancaria excecao.

            return true;
        }

        private static ResolvedDatabase ResolveDatabase(IPlayniteAPI api)
        {
            // api.Addons.Plugins traz as instancias reais dos plugins, tipadas pela classe
            // base do SDK. O casamento e pelo nome do tipo, e nao pelo Id do extension.yaml,
            // porque o Id e um GUID que muda entre versoes do GameActivity. Se o tipo mudar
            // de nome o jogo simplesmente nao acha nada e avisa uma vez no log.
            var plugin = api?.Addons?.Plugins?
                .FirstOrDefault(p => p?.GetType().FullName == GameActivityAddonType);
            if (plugin == null)
            {
                return null;
            }

            // GameActivityMonitoring e internal e PluginDatabase e private: o unico caminho
            // e por reflexao, e e por isso que a extensao nao referencia a DLL em tempo
            // de compilacao — assim ela continua funcionando sem o GameActivity instalado.
            var monitoring = plugin.GetType()
                .GetProperty("GameActivityMonitoring", InstanceMembers)?.GetValue(plugin, null);
            if (monitoring == null)
            {
                return null;
            }

            var database = monitoring.GetType()
                .GetProperty("PluginDatabase", InstanceMembers)?.GetValue(monitoring, null);
            return database == null ? null : new ResolvedDatabase(database);
        }

        private static void WarnUnavailable(ILogger logger, string reason)
        {
            if (warnedUnavailable)
            {
                return;
            }

            warnedUnavailable = true;
            logger.Warn("GameActivity: sessões não gravadas porque " + reason +
                " Ative a opção de sessões e confira se o plugin GameActivity está instalado e carregado.");
        }

        private class ResolvedDatabase
        {
            public ResolvedDatabase(object database)
            {
                Database = database;
            }

            public object Database { get; }
        }
    }
}