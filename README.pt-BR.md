# AniList PlayTime

> Extensão genérica para o [Playnite](https://playnite.link) 10.x que transforma seu
> progresso do [AniList](https://anilist.co) em tempo de uso real dentro do Playnite.

Anime e mangá não são "jogos", mas têm lugar natural numa biblioteca de jogos: têm
título, capa, status e progresso. O que não têm é lugar no campo **Tempo de uso**, que é
justamente o que o Playnite usa em *Tempo total*, *Tempo médio* e *Mais jogados* na tela
de Estatísticas.

O **AniList PlayTime** preenche esse campo a partir do seu progresso no AniList, para que
sua coleção inteira de anime **e mangá** apareça nas estatísticas ao lado dos jogos.

- Id da extensão: `PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D`
- Menu: **Extensões ▸ AniList PlayTime**

> **Renomeada:** a extensão se chamava *Playtime AniList* e ficava como um item próprio na
> barra de menus. Agora é *AniList PlayTime* e fica dentro de **Extensões**. O id não mudou,
> então isto é uma atualização normal: suas configurações vêm junto.

> **Atenção:** a 1.3 está em beta. A versão estável é a
> [1.0](https://github.com/DenkaAkumaPedro/Playtime-AniList/releases/tag/v1.0)
> (só anime). Veja o [CHANGELOG](CHANGELOG.md) para saber o que mudou.

## Recursos

### Tempo de anime e mangá
- O tempo é gravado no campo `Playtime` do Playnite, então aparece em
  **Biblioteca ▸ Estatísticas** (`Tempo total`, `Tempo médio`, `Mais jogados`).
- Anime: `progresso no AniList × duração do episódio`, então uma série pela metade recebe
  um valor proporcional em vez da duração completa.
- Mangá: estimado a partir do progresso e da contagem de volumes/capítulos. O AniList não
  tem contagem de páginas, então a velocidade de leitura é estimada - veja
  [Como o tempo de mangá é calculado](#como-o-tempo-de-mangá-é-calculado) e
  [docs/COMO_CALCULA_TEMPO.md](docs/COMO_CALCULA_TEMPO.md).
- Modo opcional **Somar ao tempo existente**, que soma o tempo do AniList por cima do tempo
  que a entrada já tinha. Desde a 1.2 a extensão guarda quanto escreveu em cada jogo, então
  ela substitui só a própria parte em vez de somar por cima dela a cada execução.
- Modo opcional **Gravar sessão no GameActivity**, que também registra uma sessão de uso
  por item, para os gráficos. Desligado por padrão - veja
  [Histórico de sessões](#histórico-de-sessões).

### Automação
- **Sincronização automática** opcional, no máximo uma vez a cada 24 horas, disparada por
  atualização da biblioteca (`OnLibraryUpdated`) em vez de rodar a cada inicialização.
- O resultado aparece como notificação do Playnite (pode ser desligado).
- A trava de 24 h fica em `state.json` na pasta de dados da extensão, fora do diálogo de
  configurações, então sobrevive a reinícios e não briga com a tela de settings.

### Escopos e menus
- `Extensões ▸ AniList PlayTime` (menu principal, com os 3 itens originais mais
  *Sincronizar anime e mangá*).
- `AniList PlayTime ▸ Atualizar tempo destes itens` no menu de contexto do jogo.
- Sincronizar tudo, ou só o que mudou no AniList nos últimos 7 ou 30 dias.

## Requisitos

- Playnite 10.x com API 6.17.0 ou superior.
- `Importer for AniList` instalado e autenticado (o token dele é reaproveitado).
- Opcional, só para o histórico de sessões: o add-on
  [GameActivity](https://github.com/Lacro59/playnite-gameactivity-plugin), 3.5 ou superior.

## Instalação

1. Baixe o `.pext` na página de
   [releases](https://github.com/DenkaAkumaPedro/Playtime-AniList/releases).
2. Abra o Playnite e arraste o arquivo para a janela, ou dê duplo clique nele.

> Vem da build de teste que se chamava **AniList Com Horas**? Desinstale antes (o id da
> extensão mudou para `PlaytimeAniList_…`, senão o Playnite carrega as duas cópias). As
> configurações começam do zero.

Depois que o add-on entrar na base oficial do Playnite, também dá para instalar pelo
navegador de addons do Playnite ou pela URI:

```
playnite://playnite/installaddon/PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D
```

## Uso

**Extensões ▸ AniList PlayTime**

| Item | O que faz |
| --- | --- |
| Sincronizar tudo | Atualiza todos os jogos importados, usando o escopo marcado nas configurações. |
| Sincronizar atualizações da última semana | Só entradas atualizadas no AniList nos últimos 7 dias. |
| Sincronizar atualizações do último mês | Só entradas atualizadas no AniList nos últimos 30 dias. |
| Sincronizar anime e mangá | Sincroniza sempre os dois tipos, ignorando o escopo das configurações. |

Em um jogo selecionado, clique com o botão direito e escolha
**AniList PlayTime ▸ Atualizar tempo destes itens**.

Ao final aparece um resumo com quantos itens do AniList têm tempo, quantos mangás têm tempo
e o total de horas de mangá, quantos tempos foram alterados naquela rodada, quantos ficaram
fora da janela de sincronização e quantos não têm link do AniList.

## Como o tempo de anime é calculado

```
segundos = progresso no AniList (episódios vistos) × duração do episódio (minutos) × 60
```

Exemplo: *Chainsaw Man*, 4 episódios de 24 min assistidos → `4 × 24 × 60 = 5 760 s` = 1 h 36 min.

## Como o tempo de mangá é calculado

O AniList não guarda contagem de páginas, então mangá precisa de estimativa. Com os
padrões (200 páginas por volume, 18 segundos por página):

```
páginas por capítulo = páginas por volume × volumes ÷ capítulos      (limitado a 6…120)
segundos              = capítulos lidos × páginas por capítulo × segundos por página
```

O resultado cai bem perto da realidade: um volume é um livro físico, e ler um leva
aproximadamente uma hora.

| Situação | Resultado com os padrões |
| --- | --- |
| Chainsaw Man, 232 capítulos / 24 volumes, tudo lido | 24 h (≈ 1 h por volume) |
| Attack on Titan, 141 capítulos / 34 volumes | 34 h (lançamento mensal, 14,5 min/capítulo) |
| One Piece, 500 capítulos, sem dados de volume | 41,7 h (fallback de 5 min/capítulo) |
| Solo Leveling, 143 capítulos / 11 volumes, `countryOfOrigin: KR` | 23,8 h (webtoon, 10 min/capítulo) |
| Mushoku Tensei, 334 capítulos, formato `NOVEL` | 66,8 h (novela, 12 min/capítulo) |
| Look Back, formato `ONE_SHOT` | 1 h (um volume = 200 páginas) |

Casos especiais, todos configuráveis: `NOVEL` usa minutos por capítulo, webtoons
(`countryOfOrigin` KR ou CN) têm os seus próprios minutos por capítulo, e `ONE_SHOT` é um
volume só. Séries sem contagem de capítulos ou volumes no AniList usam o fallback de
minutos por capítulo. Progresso `0` é sempre `0`.

## Configurações

*Playnite ▸ Configurações ▸ Extensões ▸ AniList PlayTime*

| Opção | Padrão | Descrição |
| --- | --- | --- |
| Sincronizar anime | ligado | Inclui anime na sincronização. |
| Sincronizar mangá | ligado | Inclui mangá na sincronização. |
| Somar ao tempo existente | desligado | Acumula o valor do AniList em vez de substituir. |
| Páginas por volume | 200 | Contagem típica de um tankobon, base das páginas por capítulo. |
| Segundos por página | 18 | Velocidade média de leitura. |
| Minutos por capítulo (fallback) | 5 | Usado quando o AniList não tem dados de capítulo/volume. |
| Minutos por capítulo (novela) | 12 | Para `format: NOVEL`. |
| Minutos por capítulo (webtoon) | 10 | Para `countryOfOrigin: KR` ou `CN`. |
| Sincronização automática ao atualizar a biblioteca | desligado | Sincroniza no máximo 1x a cada 24 h, após uma atualização da biblioteca. |
| Notificar após sincronização automática | ligado | Mostra o resultado como notificação do Playnite. |
| Gravar sessão no GameActivity | desligado | Também registra uma sessão de uso por item, para os gráficos. |
| Token alternativo | vazio | Deixe vazio para reaproveitar o token do `Importer for AniList`. |

Valores fora da faixa são ajustados para o limite, então um erro de digitação não gera um
tempo de uso absurdo.

## Histórico de sessões

Os gráficos do próprio Playnite só somam totais, porque a API pública de extensões expõe
`Playtime` e `LastActivity` mas não uma tabela de sessões. Com **Gravar sessão no
GameActivity** ligado, a extensão também escreve uma sessão por item através do add-on
[GameActivity](https://github.com/Lacro59/playnite-gameactivity-plugin), que tem essa
tabela - assim anime e mangá aparecem como série temporal, e não só como total.

- Uma sessão por entrada, datada com o `updatedAt` do AniList (quando você mexeu no item
  lá), carregando todo o tempo calculado.
- A sessão é **substituída**, nunca somada: sincronizar dez vezes continua deixando uma
  sessão só.
- Precisa do GameActivity 3.5 ou superior. Sem ele a extensão continua funcionando e
  continua gravando o `Playtime`; só o histórico de sessões é pulado, com uma linha no log.
- A escrita passa por reflexão, porque o Playnite não tem API pública para isso. Se uma
  versão futura do GameActivity mudar os nomes internos, a sincronização continua
  funcionando e as sessões são puladas - o log avisa em vez de a sincronização falhar.

## Como o id do AniList é encontrado

O `Importer for AniList` pode ser configurado com `addLinksAndImages: false` e, nesse
caso, o `Game.Links` fica vazio. Por isso a extensão lê o media id do AniList primeiro do
`Game.GameId` (é onde o importer grava) e só depois tenta extrair de uma URL
`anilist.co` em `Game.Links`. A 1.0 só fazia a segunda parte, e por isso pulava em
silêncio todo jogo importado sem links.

## Limitações conhecidas

- O tempo de mangá é uma estimativa, não uma medição. O AniList não tem contagem de
  páginas e a extensão não conhece a sua velocidade real de leitura. Ajuste as
  configurações para o seu jeito de ler.
- Sem o GameActivity não existe histórico de tempo por dia ou por mês, então os gráficos do
  Playnite somam totais em vez de mostrar uma série temporal. Veja
  [Histórico de sessões](#histórico-de-sessões).
- A sincronização não é em tempo real. O AniList não tem notificação push de mudança de
  lista, então a extensão sincroniza quando você manda, ou no máximo 1x por dia após uma
  atualização da biblioteca.
- A extensão não executa nada: ela só escreve o campo `Playtime` (e, se você ligar, uma
  sessão).
- A sessão só é redatada quando a entrada do AniList muda. Se você reler um volume sem
  marcar nada novo no AniList, o tempo da entrada também não muda.

## Compilando a partir do código

```powershell
# detecta sozinho a pasta do Playnite (ou passe -PlayniteDir)
.\build.ps1
```

Requisitos: .NET SDK e uma instalação do Playnite com `Toolbox.exe` e `Playnite.SDK.dll`.
O script compila o `src/AniListWatchTime.csproj`, monta o staging com `extension.yaml` +
`icon.png` + a DLL e chama o `Toolbox.exe pack` para gerar o `.pext` em `dist/`, renomeado
para `<Nome>_<versão>.pext` (por exemplo `AniList_PlayTime_1-3.pext`).

## Links

- Código: <https://github.com/DenkaAkumaPedro/Playtime-AniList>
- Problemas: <https://github.com/DenkaAkumaPedro/Playtime-AniList/issues>
- Histórico de mudanças: [CHANGELOG.md](CHANGELOG.md)

## Créditos

Dados da API GraphQL do [AniList](https://anilist.co). Playnite é do
[Josef Nemec](https://github.com/JosefNemec/Playnite). A velocidade de leitura padrão foi
conferida com relatos reais de tempo de leitura de tankobon e webtoon.

## Licença

[MIT](LICENSE)
