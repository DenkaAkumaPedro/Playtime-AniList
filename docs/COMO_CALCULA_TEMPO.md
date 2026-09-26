# Como o tempo de mangá é calculado

Este documento explica, em detalhe, a fórmula usada pela extensão para transformar o
progresso do AniList em tempo de leitura. Ele fica em `docs/` porque é a parte do projeto
que mais depende de suposição: o AniList **não tem contagem de páginas**.

## O problema

O AniList guarda, para cada entrada da lista:

| Campo | Exemplo (Chainsaw Man) |
| --- | --- |
| `progress` | 232 (capítulos lidos) |
| `progressVolumes` | 24 (volumes lidos) |
| `chapters` | 232 (capítulos existentes) |
| `volumes` | 24 (volumes existentes) |
| `format` | `MANGA` |
| `countryOfOrigin` | `JP` |
| `duration` | ausente para mangá |

Não existe `pages`, nem `minutesPerChapter`. Só para **anime** existe `duration`, que é o
tempo de cada episódio. Por isso o tempo de anime é exato e o de mangá é uma estimativa.

## A fórmula

Com os valores padrão (200 páginas por volume, 18 segundos por página):

```
páginas por capítulo = páginas por volume × volumes ÷ capítulos
segundos             = progresso × páginas por capítulo × segundos por página
```

O número de páginas por capítulo é limitado a uma faixa de 6 a 120, porque:
- abaixo de 6 páginas por capítulo, séries de capítulos curtos (one-shot, capítulos
  extras) inflariam o tempo;
- acima de 120, séries em andamento com `volumes` incompleto (ex.: 1 volume de 900
  capítulos) estourariam o tempo.

## Por que 200 páginas e 18 segundos

Um tankobon tem, em média, 200 a 300 páginas. Usando 200 páginas e 18 segundos por
página, o resultado é **1 hora por volume**, que é de longe a melhor aproximação para o
tempo real de ler um volume físico.

| Série | Capítulos | Volumes | Resultado | Min/capítulo |
| --- | --- | --- | --- | --- |
| Chainsaw Man | 232 | 24 | 24 h | 6,2 |
| Jujutsu Kaisen | 272 | 30 | 30 h | 6,6 |
| Attack on Titan | 141 | 34 | 34 h | 14,5 |
| Tokyo Ghoul | 144 | 14 | 14 h | 5,8 |
| Kimetsu no Yaiba | 207 | 23 | 23 h | 6,7 |
| Oyasumi Punpun | 147 | 13 | 13 h | 5,3 |

Repare como tudo cai perto de 1 h por volume. *Attack on Titan* passa do dobro porque é
uma série de lançamento mensal (um capítulo por mês), e o leitor normalmente leva bem
mais que 6 minutos em cada.

Quando a série ainda não tem `chapters`/`volumes` no AniList (é comum em mangás em
andamento), cai para o fallback de **5 minutos por capítulo**:

| Série | Capítulos | Resultado |
| --- | --- | --- |
| One Piece | 500 | 41,7 h (5,0 min/capítulo) |

## Casos especiais

### `format: NOVEL` (light novel)

Não existe "página" de forma útil em uma novela, então a extensão usa **12 minutos por
capítulo**, configurável em *Minutos por capítulo (novela)*.

Exemplo: Mushoku Tensei, 334 capítulos → 66,8 h.

### `format: ONE_SHOT`

Um one-shot tem 1 capítulo e normalmente 1 volume. A extensão trata o item como
**1 one-shot = 1 volume inteiro = 200 páginas = 1 hora** com os padrões, em vez de tratar
aquele capítulo como um capítulo comum. O tratamento é explícito para não depender de o
AniList reportar capítulos e volumes de forma consistente nesse formato.

Exemplo: Look Back → 1 h.

### Webtoon (`countryOfOrigin: KR` ou `CN`)

Webtoons são lidos em rolagem vertical, em capítulos muito mais longos que um capítulo
de mangá impresso. A extensão usa **10 minutos por capítulo**, configurável em
*Minutos por capítulo (webtoon)*.

Exemplo: Solo Leveling, 143 capítulos / 11 volumes, `KR` → 23,8 h.

Observação: no schema do AniList os formatos válidos para mangá são `MANGA`, `NOVEL` e
`ONE_SHOT`. Webtoon não é um formato separado, é um `MANGA` com `countryOfOrigin` `KR`
ou `CN` - por isso a detecção é por origem, e não por formato.

### Progresso 0

Sempre `0`. Um item só no *Planning* não ganha tempo de leitura.

## Progresso em volumes

A extensão usa o `progress` do AniList, que é a **quantidade de capítulos lidos**. O
campo `progressVolumes` (volumes lidos) existe no AniList e chega na consulta, mas não é
usado no cálculo.

Na prática isso quase não faz diferença: o AniList mantém `progress` e `progressVolumes`
coerentes entre si, e como um volume é lido por completo, o número de capítulos é sempre
maior ou igual ao de volumes. Marcar "volume 3" em uma série de 30 volumes deixa o
`progress` em 3 capítulos ou mais, e é esse número que entra na conta.

## Ajustando para o seu jeito de ler

O número certo depende de quanto **você** lê por dia. Se a estimativa ficar alta ou
baixa, ajuste "Segundos por página" nas configurações - é para isso que ele existe.

Faixas aceitas (valores fora delas são ajustados para o limite, nunca rejeitados):

| Configuração | Mínimo | Máximo |
| --- | --- | --- |
| Páginas por volume | 20 | 500 |
| Segundos por página | 1 | 300 |
| Minutos por capítulo (fallback) | 1 | 120 |
| Minutos por capítulo (novela) | 1 | 120 |
| Minutos por capítulo (webtoon) | 1 | 120 |

## O que esta extensão **não** inventa

- Ela não soma tempo por dia/mês, porque a API pública do Playnite não expõe tabela de
  sessões - só `Playtime` e `LastActivity`. Os gráficos do Playnite somam o total, mas não
  mostram uma série temporal.
- Ela não lança o mangá: a extensão não executa nada, ela só escreve o campo `Playtime`.
- Ela não é tempo real de leitura: é progresso do AniList vezes uma velocidade média
  configurável. Serve para que a coleção entre nas estatísticas, não para medir a sua
  sessão de leitura.
