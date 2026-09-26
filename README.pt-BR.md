# Playtime AniList

> Extensão genérica para o [Playnite](https://playnite.link) 10.x que transforma seu
> progresso do [AniList](https://anilist.co) em tempo de uso real dentro do Playnite.

Anime e mangá não são "jogos", mas têm lugar natural numa biblioteca de jogos: têm
título, capa, status e progresso. O que não têm é lugar no campo **Tempo de uso**, que é
justamente o que o Playnite usa em *Tempo total*, *Tempo médio* e *Mais jogados* na tela
de Estatísticas.

O **Playtime AniList** preenche esse campo a partir do seu progresso no AniList, para que
sua coleção inteira de anime (e, a partir da 1.1, de mangá) apareça nas estatísticas ao
lado dos jogos.

- Id da extensão: `PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D`
- Menu: **Extensões ▸ Playtime AniList**

## Recursos (1.0)

- Sincroniza o tempo de uso de todos os jogos importados pelo
  [`Importer for AniList`](https://github.com/darklinkpower/PlayniteExtensionsCollection/wiki/Importer-for-Anilist).
- O tempo é `progresso no AniList × duração do episódio`, então uma série pela metade
  recebe um valor proporcional em vez da duração completa.
- Três escopos de sincronização: biblioteca inteira, atualizações dos últimos 7 dias ou
  dos últimos 30 dias.
- Sincronização de um jogo só, pelo menu de contexto.
- Modo opcional **Somar ao tempo existente**, que acumula em vez de substituir.
- Reaproveita o token do `Importer for AniList`; também dá para colar o seu.

## Requisitos

- Playnite 10.x com API 6.17.0 ou superior.
- `Importer for AniList` instalado e autenticado (o token dele é reaproveitado).

## Instalação

1. Baixe o `.pext` na página de
   [releases](https://github.com/DenkaAkumaPedro/Playtime-AniList/releases).
2. Abra o Playnite e arraste o arquivo para a janela, ou dê duplo clique nele.

Depois que o add-on entrar na base oficial do Playnite, também dá para instalar pelo
navegador de addons do Playnite ou pela URI:

```
playnite://playnite/installaddon/PlaytimeAniList_C034A45E-3C56-48DA-ABE0-1C46B4C4A57D
```

## Uso

**Extensões ▸ Playtime AniList**

| Item | O que faz |
| --- | --- |
| Sincronizar tudo | Atualiza o tempo de todos os jogos importados. |
| Sincronizar atualizações da última semana | Só entradas atualizadas no AniList nos últimos 7 dias. |
| Sincronizar atualizações do último mês | Só entradas atualizadas no AniList nos últimos 30 dias. |
| Sincronizar anime e mangá | *(a partir da 1.1)* Igual a "Sincronizar tudo", para os dois tipos. |

Em um jogo só, clique com o botão direito e escolha
**Playtime AniList ▸ Atualizar tempo deste anime**.

Ao final aparece um resumo com quantos jogos foram atualizados, quantos não têm link do
AniList e quantos foram pulados.

## Como o tempo é calculado

Para anime:

```
segundos = progresso no AniList (episódios vistos) × duração do episódio (minutos) × 60
```

Exemplo: *Chainsaw Man*, 4 episódios de 24 min assistidos → `4 × 24 × 60 = 5 760 s` = 1 h 36 min.

Jogos já têm tempo de uso no Playnite, então o padrão é **substituir** o valor. Ative
*Somar ao tempo existente* se quiser que o valor do AniList seja somado ao que já estiver
lá (útil para jogos que também têm uma versão de PC com sessões reais).

## Configurações

*Playnite ▸ Configurações ▸ Extensões ▸ Playtime AniList*

| Opção | Descrição |
| --- | --- |
| Somar ao tempo existente | Acumula em vez de substituir. |
| Token alternativo | Deixe vazio para reaproveitar o token do `Importer for AniList`. |

## Limitações conhecidas na 1.0

- **Jogos importados sem link são pulados.** O `Importer for AniList` pode ser configurado
  com `addLinksAndImages: false`; nesse caso o `Game.Links` fica vazio e a 1.0 não
  encontra o id do AniList. Isso é corrigido na 1.1, que lê o `Game.GameId`.
- Mangá ainda não é suportado.

## Compilando a partir do código

```powershell
# detecta sozinho a pasta do Playnite (ou passe -PlayniteDir)
.\build.ps1
```

Requisitos: .NET SDK e uma instalação do Playnite com `Toolbox.exe` e `Playnite.SDK.dll`.
O script compila o `src/AniListWatchTime.csproj`, monta o staging com `extension.yaml` +
`icon.png` + a DLL e chama o `Toolbox.exe pack` para gerar o `.pext` em `dist/`.

## Links

- Código: <https://github.com/DenkaAkumaPedro/Playtime-AniList>
- Problemas: <https://github.com/DenkaAkumaPedro/Playtime-AniList/issues>
- Histórico de mudanças: [CHANGELOG.md](CHANGELOG.md)

## Créditos

Dados da API GraphQL do [AniList](https://anilist.co). Playnite é do
[Josef Nemec](https://github.com/JosefNemec/Playnite).

## Licença

[MIT](LICENSE)
