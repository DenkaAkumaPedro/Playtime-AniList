# AGENTS.md — AniList PlayTime

Extensão genérica para Playnite 10.x (C#, `net462`, WPF). Escreve o tempo de anime/mangá
do AniList no campo `Playtime` do Playnite, e opcionalmente uma sessão por item no
GameActivity. Sem solução, sem projeto de teste, sem CI.

> A pasta local chama "Aniilist Com Horas" (nome do addon antigo). O projeto é
> **AniList PlayTime** (chamado *Playtime AniList* até a 1.2), id
> `PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D`.
> Espelho em inglês deste arquivo: `AGENTS.en.md` (manter os dois em sincronia).

## Comandos

```powershell
dotnet build src\AniListWatchTime.csproj -c Release   # compila sozinho: o SDK do Playnite vem do NuGet
.\build.ps1 -NoPack                                   # build sem empacotar
.\build.ps1                                           # build + empacota o .pext em dist\
.\build.ps1 -PlayniteDir "C:\Program Files\Playnite"  # quando o autodetect falhar
```

- `build.ps1` exige um Playnite instalado com `Toolbox.exe` e `Playnite.SDK.dll`. O
  autodetect inclui um caminho pessoal (`D:\Progamas\Biblioteca\Playnite`).
- **Não existem** comandos de lint, format ou teste. Não invente. `net462` + WPF = build
  só no Windows.
- Como verificar lógica: não há teste automatizado; a prática do repo é chamar os métodos
  privados estáticos por reflexão sobre a DLL compilada, carregando as dependências com um
  `AssemblyResolve` apontando para o diretório do Playnite (ver "Verificação" em
  `CONTINUAR.md`). Para o GameActivity, carregar também
  `Extensions\playnite-gameactivity-plugin` e as assemblies do WPF
  (`PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Xaml`).
- Detalhe de PowerShell que morde: `Add-Type -AssemblyName A,B,C` falha para o WPF;
  chame uma assembly por vez. E `PropertyInfo.SetValue` desambigua mal quando o valor é
  `[Guid]`: em doubt, faça o teste em C# (`Add-Type -TypeDefinition`) recebendo os `Type`
  já carregados.

## Estado atual: 1.3 compilada e testada por reflexão, nada rodou no Playnite

- Working tree sujo, com a 1.2 e a 1.3 juntas. A 1.3 está compilada e empacotada, mas
  **nada rodou dentro do Playnite**.
- `ComputeTarget` e `PruneBaseline` têm suíte por reflexão (11 + 1 casos) e
  `GameActivityWriter.UpsertSession` tem suíte contra os tipos reais do GameActivity
  instalado. Tudo passa. Ainda falta o caminho ponta a ponta.
- **Não commitar** e **não** adicionar a entrada 1.3 em
  `Manifests/PackageInstaller/…yaml` até o usuário validar dentro do jogo: o `PackageUrl`
  aponta para uma release que precisa existir.
- `CONTINUAR.md` (não rastreado, pt-BR) é a nota de passagem viva — decisões travadas,
  checklist de validação e pendências. Ler antes de mexer em escopo.
- `PENDENCIAS.md` foi apagado de propósito e está no `.gitignore`. Não recriar.

## Onde mexer

| Arquivo | Papel |
| --- | --- |
| `src/AniListWatchTime.cs` | plugin, menus, autosync de 24 h, `state.json` |
| `src/Services/AniListClient.cs` | GraphQL, cálculo de tempo, escrita no banco, sessões pendentes |
| `src/Services/GameActivityWriter.cs` | escrita de sessão por reflexão (sem referência de compilação) |
| `src/AniListWatchTimeSettings.cs` | settings, validação e clamps |
| `src/AniListWatchTimeSettingsView.xaml` | tela de settings (texto fixo, sem binding de resource) |

Regras que quebram em silêncio se forem "simplificadas":

- `ComputeTarget` (`AniListClient.cs:807`) é o coração do "somar". Sem linha de base
  (`lastApplied`) nunca apaga tempo de origem desconhecida; com progresso 0 remove só a
  própria contribuição; com o somar ligado, **substitui** a contribuição anterior em vez de
  somar por cima — a inflação sem limite era o bug corrigido na 1.2.
- `PruneBaseline` (`AniListClient.cs:844`) só pode receber em `seenGameIds` o que foi
  realmente **avaliado** na rodada, nunca o que foi apenas consultado. Uma sync de janela
  semanal não avalia os mangás; se eles entrassem no prune, a próxima sync somaria o tempo
  inteiro de novo. Por isso o `seenGameIds.Add` fica no topo do laço, antes dos filtros.
- `SyncSelectedGames` não pode mais dar `continue` em `seconds <= 0`: precisa passar pelo
  `ComputeTarget` para limpar a contribuição e pela fila de sessões para remover a sessão
  antiga. Era um `continue` que deixava o selected sync sem o comportamento de progresso 0.
- `returnedIds` e `evaluableIds` (`AniListClient.cs:177-206`) precisam continuar separados:
  "o AniList não retornou este item" nunca pode virar "progresso 0", senão uma resposta
  parcial da API apaga tempo.
- `IndividualMediaQueryLimit = 30` (`AniListClient.cs:81`) porque a query por ids do AniList
  estoura em 50. Seleção maior que isso passa por `CollectBatch`; não monte a query com todos.
- As escritas no banco ficam dentro de `api.Database.BufferedUpdate()` e só chamam
  `Games.Update(game)` quando algo mudou de fato.
- Id do AniList: `Game.GameId` primeiro, o regex de `Game.Links` é só fallback
  (`GetMediaIdFromGame`). Jogos são filtrados por `PluginId == ImporterPluginId`
  (`2366fb38-bf25-45ea-9a78-dcc797ee83c3`).
- Token: lido de `ExtensionsData/2366fb38-…/config.json` (`AccountAccessCode`), com
  override opcional nas settings. Nunca logar.
- `HttpClient.SendAsync(...).GetAwaiter().GetResult()` é bloqueante de propósito: roda dentro
  de `ActivateGlobalProgress`. O autosync entra por `Task.Run` e volta pra UI via
  `RunOnUiThread`.
- Todo caminho de sync passa por `lock (syncLock)` (`AniListWatchTime.cs:207`, `:266`,
  `:279`, `:340`). O selected sync ficou de fora uma vez e podia rodar junto com a full.
- `SyncResult { Message, Failed }` substituiu a detecção de sucesso por prefixo de string.
  Não voltar a checar texto: uma falha pode começar com a mesma palavra.
- `LoadState(out bool damaged)` + `SaveState` atômico (`.tmp` + `File.Replace`,
  `AniListWatchTime.cs:401`). Arquivo corrompido **não** pode ser lido como vazio: isso
  transformaria "somar" em "substituir" sem o usuário pedir.
- `MediaListCollection` vem paginado na prática (≈1547 anime em 12 grupos, medido em
  09/2026). Nunca assumir um único grupo em `lists`.

## GameActivity por reflexão — as três armadilhas

Não há API pública do Playnite para escrever sessões, então `GameActivityWriter.cs` faz
tudo por reflexão e **não** referencia o GameActivity em tempo de compilação. As três
coisas que já quebraram e estão escritas em comentário no arquivo:

1. `api.Addons.Plugins` contém **instâncias de `Plugin`**, não o `IAddon`. Procurar o
   GameActivity pelo `Type.FullName == "GameActivity.GameActivity"`; tentar ler
   `plugin.Addon` não acha nada.
2. A interface chega **fecha** (`IPluginDatabase<GameActivities>`), então `Get` e
   `AddOrUpdate` já estão materializados: `IsGenericMethodDefinition` é `false` e
   `MakeGenericMethod` não existe nesse caso. Filtrar por ele encontra zero métodos.
3. `GameActivities.SessionPlaytime` é **propriedade calculada**, sem setter: o getter soma
   `Items`. Atribuir por reflexão lança `ArgumentException`, e o `try/catch` de
   `WriteSessions` engolia isso, então nenhuma sessão persistia sem erro visível.

Cadeia confirmada contra o GameActivity 3.5 instalado:
`GameActivity.GameActivity` → `GameActivityMonitoring` (prop. `GameActivityMonitoring`) →
`PluginDatabase` → `IPluginDatabase<GameActivities>`.

- `SessionSourceId` (`GameActivityWriter.cs:23`) é um `Guid` fixo e é o que identifica "nossa"
  sessão. Sessão de outro `SourceID` nunca é tocada, e `SessionPlaytime` continua contando
  ela. Trocar esse Guid faz a extensão duplicar a sessão uma vez.
- `UpsertSession` remove a sessão anterior **antes** de decidir o que inserir, para o
  progresso 0 apagar a sessão morta e o `SessionPlaytime` cair junto.
- Falha de GameActivity nunca pode reprovar a sync: o `Playtime` já foi gravado no banco.

## Versão mora em 4 lugares, e já esteve dessincronizado

1. `extension.yaml` → `Version:` e `Name:`
2. `src/Services/AniListClient.cs:137` → User-Agent `"AniListPlayTime/1.3 (Playnite extension)"`
3. `CHANGELOG.md` → seção da versão
4. `Manifests/PackageInstaller/PlaytimeAniList_….yaml` → `Packages:` (ainda só 1.0)

O nome do pacote **não** vem do `Id:`: `Toolbox.exe pack` não aceita nome customizado, então
`build.ps1` empacota e depois renomeia para `<Name>_<versão>.pext`
(`AniList_PlayTime_1-3.pext`). O `Id:` continua `PlaytimeAniList_…` de propósito, para o
Playnite tratar isto como atualização da extensão antiga e preservar as configurações.

O `build.ps1` fixa o caminho `src\bin\Release\net462\AniListWatchTime.dll`: mudar
`TargetFramework`, `AssemblyName` ou `Configuration` quebra o empacotamento sem erro de
compilação.

## Convenções

- Toda string de usuário (menus, XAML, resumo da sync, logs) é **português fixo**.
  `src/Localization/en_US.xaml` está vazio e nenhum código lê `Localization/` — o glob só
  entra no pacote. i18n está fora de escopo, não introduzir.
- O nome visível é "AniList PlayTime", com **um** `i` em `Ani`, apesar de a pasta e de
  alguns identificadores ainda dizerem `Aniilist`/`AniList`. Não "corrigir" a grafia do nome.
- Comentário só quando o "porquê" não é óbvio (matemática da linha de base, retornado vs
  avaliável, armadilhas de reflexão). Sem narração linha a linha.
- Sem `.editorconfig` nem analisadores: seguir o estilo existente (abre-chave de `if`/`else`
  em linha própria, `var` em quase tudo).
- Ao mexer em feature ou setting, atualizar `README.md` **e** `README.pt-BR.md` juntos.
- `dist/` e `*.pext` são gitignored: artefato nunca entra no commit, o upload da release é manual.

## Backup e release

- Antes de uma beta: snapshot em `backup\<versão>_<timestamp>\` com o `.pext` e um
  `src_<versão>.zip`, mais a tag `<versão>-snapshot-<timestamp>`. Apagar quando a beta sair
  do teste. A pasta só existe enquanto não há versão estável.
- Commit: subject único em inglês, `AniList PlayTime <versão>: <resumo>`. Só commitar
  quando o usuário pedir.

## Bugs conhecidos — não "conserte" sem o usuário pedir

- Se o Playnite cair entre o commit no banco e o `SaveState`, a linha de base dessincroniza
  por uma sync.
- **Apagar o `state.json` não é neutro.** Com "Somar ao tempo de jogo já existente" ligado,
  a primeira sync depois de apagar soma o tempo do AniList por cima do valor que a extensão
  já tinha gravado, porque esse valor passa a parecer tempo de origem desconhecida. O
  procedimento seguro está na mensagem de estado corrompido e em `docs/COMO_CALCULA_TEMPO.md`:
  apagar, desligar o somar, sincronizar, ligar de novo.