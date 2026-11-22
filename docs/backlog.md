# Backlog do AlpaFlow Scout

Ideias guardadas para depois. Desde 03/11/2026 o canal do YouTube e do TikTok é a vitrine do Scout (desde 12/11/2026 "Galo na Lupa", @galonalupa): entra primeiro
o que faz o vídeo sair mais rápido, mais bonito ou com um gancho melhor. Cada item sai daqui quando for feito.

## Para o canal (conteúdo mais rápido e com gancho)

1. **Pós-jogo automático** — FEITO em 03/11/2026 (RoteiristaPosJogo + AcompanhamentoPosJogo; ligar com SCOUT_POSJOGO_TIMES=1062
   e SCOUT_WHATSAPP_* no servidor). O maior ganho: quem publica primeiro depois do apito pega a busca.
   - Quando um jogo do Galo termina e as estatísticas chegam, o Scout monta sozinho "onde ganhou/perdeu", as notas e um roteiro
     curto de 40 s, e avisa no WhatsApp do Leandro. Ele só grava a fala por cima.
   - Usa o que já existe: analisar_partida, notas do jogo e percentis.

2. **Cartão vertical da resposta** — FEITO em 03/11/2026 (web/src/ParaVideo.ts: botão em cada resposta e 3 cartões no painel da partida)
   - Gera a resposta em 1080x1920, com letra grande, os números em destaque e o escudo, pronta para o Short.
   - Hoje a resposta é gravada da tela do computador e ampliada na edição: o texto fica pequeno e às vezes cortado.

3. **Sequências e marcas** — FEITO em 08/11/2026 (ferramenta `sequencias` do chat, ObterSequenciasHandler) (gancho de título: "o Galo não perde há 7 jogos")
   - Ferramenta do chat: invencibilidade, jogos sem sofrer gol, sequência de vitórias, maior goleada da temporada, "desde X".
   - A base já tem os placares; é conta em cima de procurar_partidas.

4. **Palpite da IA antes do jogo, e "acertou ou errou?" depois** — FEITO em 08/11/2026 (PalpitesHandler: chances exatas pela força
   Elo + Poisson, guardado em palpites e congelado no apito; ferramentas `palpite` e `historico_palpites` no chat; bloco e cartão
   "Palpite para vídeo" no painel da partida; o AcompanhamentoPosJogo faz o palpite dos jogos do Galo nas 48 h antes)
   - Chance de vitória, empate e derrota do próximo jogo pela força tipo Elo do modelo de chances da liga.
   - Gera dois vídeos por jogo (véspera e pós-jogo) e um motivo para voltar ao canal. Guardar o palpite para mostrar o histórico de acertos.

5. **Página pública de cada análise** (link da bio) — FEITO em 09/11/2026: scout.alpaflow.tec.br/jogo/<id> sem login (placar, gols,
   onde o jogo foi decidido, destaques e palpite; sem foto de atleta nem menor de 18; nunca consulta o provedor), botão
   "🔗 Link público" no painel da partida. Falta: prévia bonita ao compartilhar no WhatsApp (meta tags do servidor).
   - scout.alpaflow.tec.br/a/<id> com a resposta e os cartões, sem login, e um botão "pergunte você também" (cadastro).
   - Transforma quem vem do vídeo em usuário da plataforma.

13. **Palpite em campo neutro** (16/11/2026, por causa da final Galo x Boca em Barranquilla) — FEITO em 16/11/2026
    (ObterCopasDoTimeHandler.CampoNeutro: final da Libertadores e da Sul-Americana; vale no palpite, na força Elo e na
    simulação; `campoNeutro` no palpite e "campo neutro" no painel, na página pública e no cartão. Final Galo x Boca passou
    de 53/26/21 e 1 x 0 para 42/27/31 e 1 x 1.)
    - Hoje o modelo dá vantagem de mando a quem a API lista como mandante, mesmo em final de jogo único em campo neutro.
    - Tirar o mando quando o jogo for em campo neutro (final de copa em jogo único, ou estádio que não é de nenhum dos dois)
      e dizer isso no palpite ("campo neutro").

14. **Cartões do "Para vídeo" na marca Galo na Lupa** (12/11/2026 o canal virou Galo na Lupa, @galonalupa) — FEITO em 16/11/2026
    (ParaVideo.ts: paleta preta/grafite/dourada, listrado da camisa no fundo, selo dourado e rodapé com a lupa desenhada no
    canvas, "Galo na Lupa" e "@galonalupa · dados: AlpaFlow Scout"; prancheta e página pública /jogo no mesmo visual, link do
    canal youtube.com/@galonalupa; roteiro do pós-jogo fecha com "Se inscreve no Galo na Lupa pra ver se o modelo acerta o
    próximo"; clube fora da tabela de cores fica azul claro e o Boca entrou na tabela.)
    - Os cartões do Scout (jogo, atleta, palpite, prancheta) ainda saem no verde do "Futebol em Números".
    - Passar para preto, grafite, branco e dourado (#f5c400), com o logo da lupa (marketing/identidade-lupa no repositório
      do kazapet), para o vídeo sair sem remendo na edição.

15. **Cartão "placar do modelo"** (gancho de inscrição combinado em 13/11/2026) — FEITO em 16/11/2026
    (ParaVideo.cartaoPlacarDoModelo e botão "🎯 Placar do modelo" no palpite do painel da partida: acertos do Galo em
    grande, % e placares cravados, os últimos 5 jogos com ✅/❌/🎯 e o próximo palpite; rota GET /api/palpites/proximo?time=.)
    - "O modelo está em X de Y": acertos de resultado e placares cravados, a partir de `historico_palpites`.
    - Vai no fim do pós-jogo e no fixado dos vídeos ("se inscreve pra ver se ele acerta").

16. **Cartões de intervalo e fim de jogo direto do Scout** — FEITO em 16/11/2026
    (botão "📺 Placar agora" no painel da partida: GET /api/partidas/{id}/ao-vivo busca placar e números no provedor sem
    guardar nada (ApiFootballClient.Consultar com guardar: false, 2 consultas da cota) e gera o cartão do X 1600x900 e o do
    Short 1080x1920 com selo Intervalo, Ao vivo ou Fim de jogo e o que o modelo disse: cravou, acertou ou errou.)
    - Hoje saem de um HTML preenchido à mão (marketing/x/jogo no kazapet): placar, posse, finalizações, no alvo, escanteios e o
      veredito do palpite (cravou, acertou ou errou).
    - O Scout já tem esses números depois do apito: gerar os cartões 1600x900 (X) e 1080x1920 (Shorts) sozinho.

17. **"No alvo" do mapa conta chute bloqueado** (achado no pós-jogo Mirassol 0 x 0 Galo, 16/11/2026) — FEITO em 17/11/2026: no alvo = `is_on_target` sem `is_blocked`
    (Mirassol x Galo agora dá 1 e 2, igual à estatística), campo `Bloqueada` no mapa, "(7 travadas)" na frase do roteiro e no cartão,
    e o próximo jogo do roteiro diz "final única em campo neutro" (CampoNeutro por competição e rodada).
    - A PitchAPI marca `noAlvo` em chute bloqueado: Galo 8 "no alvo" = 1 no alvo + 7 bloqueados da API-Football; Mirassol 5 = 2 + 3.
    - O roteiro automático (FraseDoMapa) e o cartão do mapa usam esse número e contradizem a estatística do jogo e o aspecto
      "Finalizações no alvo" (percentil 3).
    - Corrigir: no roteiro e nos cartões, o "no alvo" vem da estatística da API-Football; no mapa, separar bloqueado (cor própria)
      quando der para saber, ou só "finalizações" sem dizer no alvo.
    - Junto: o roteiro disse "o Galo recebe o Boca" numa final em campo neutro; usar o CampoNeutro (item 13) no próximo jogo.

18. **Métricas de time da PitchAPI (/v1/matches/{id}/advanced)** — ideia de 18/11/2026, a partir do perfil @ovitorscout (Instagram, 15 mil)
    - **FEITO em 18/11/2026 (menos o cartão do Para vídeo):**
      - tabela estilos_no_jogo, preenchida por ColetaPitch.ColetarEstilos na AtualizacaoDiaria (até 150 jogos/dia) e pelo comando
        `pitch-estilos <liga> <temporada> [max]`;
      - consulta ObterEstiloNaLiga (média por jogo, posição e frase pronta, com mínimo de 5 jogos), servida em
        GET /api/times/{id}/estilo e pelo comando `estilo <clube>`;
      - seção "Como joga, na <liga>" no painel do time;
      - no roteiro do pós-jogo: EstiloDoJogo (do banco ou direto da PitchAPI logo depois do apito, por IEstiloDaPartida) e as
        frases do ranking em que o time está entre os 3 de cima ou de baixo.
    - Falta: o cartão "ranking do time na liga" no Para vídeo.
    - O endpoint traz por jogo e por time:
      - pressão e recuperação: ppda, high_turnovers (recuperações no campo adversário), counterpress_regains_5s;
      - posição: avg_defensive_action_x (altura da defesa), field_tilt;
      - estilo de ataque: direct_attacks e buildup_attacks;
      - também passes progressivos, xT e VAEP do time.
    - Nada disso é guardado hoje.
    - "Onde o time perde a bola" = high_turnovers do adversário no jogo.
    - Teste em 18/11 (Série A e liga argentina de 2026, todos os jogos):
      - Galo: 2º que mais perde a bola no próprio campo (9,1 por jogo) e 17º em PPDA (pressiona pouco).
      - Boca: 5º em PPDA na Argentina e 1º em ataques diretos.
    - Fazer:
      - tabela de desempenho do time por jogo vinda do /advanced, coletada na AtualizacaoDiaria;
      - ranking na liga (posição e média) no painel do time e da partida;
      - frase no roteiro automático ("o Galo é o 2º que mais perde a bola no próprio campo");
      - cartão "ranking do time na liga" no Para vídeo.
    - Cuidado: a métrica é da PitchAPI. O Wyscout mede diferente, e números de fontes diferentes não se comparam.

19. **"Leandro Borges comenta": opinião com o rosto e os números, depois de cada jogo** — ideia de 18/11/2026, a partir do vidIQ
    - **Fazer:** a partir do pós-jogo da final (21/11) ou na semana seguinte.
    - **Por quê:**
      - Nos Shorts do Galo que mais estouraram no mês, o formato "torcedor comenta" se destaca. O "HG Comenta empate do Galo contra
        o Mirassol" (GALO SEMPRE) fez 118 mil views em 3 dias, com 2 a 3 minutos de opinião com o rosto.
      - Ninguém no nicho faz isso com dados.
    - **Formato:**
      - Short de 2 a 3 min, gravado logo depois do apito.
      - Na tela: os cartões do Scout (placar, onde o jogo foi ganho ou perdido, nota da IA, a frase do estilo na liga).
      - Na fala: opinião do Leandro.
      - Título com nome próprio e afirmação, como "LEANDRO BORGES COMENTA: ...".
    - **No Scout:**
      - O roteiro do pós-jogo já existe.
      - Falta uma versão "pontos para comentar": 4 ou 5 números com a frase pronta, em vez de uma fala lida.
      - Pode virar um botão "Comentar o jogo" no painel da partida.

20. **Galo x Cruzeiro nos números do ano** — ideia de 18/11/2026, a partir do vidIQ
    - **Fazer:** na semana seguinte à final.
    - **Por quê:** entre os Shorts que mais estouraram no nicho do Galo no mês, a rivalidade com o Cruzeiro (zoação e "reação dos
      rivais") aparece várias vezes, com 50 a 200 mil views. Com dados dá para entrar nesse assunto sem baixar o nível.
    - **Conteúdo, os dois em 2026:**
      - campanha e pontos no Brasileirão;
      - gols pró e contra;
      - xG;
      - estilo na liga (item 18: quem pressiona, quem perde a bola no próprio campo);
      - melhor jogador de cada um pela nota da IA;
      - o clássico do ano.
    - **Formato:** Short de 45 s com 4 ou 5 cartões ("lado a lado") e um post no X com os mesmos cartões.
    - **No Scout:**
      - Quase tudo já sai das consultas de time e do estilo na liga.
      - Falta um cartão "time x time" no Para vídeo, aproveitando o lado a lado do raio-x de atletas.

21. **Atleta que saiu do clube continua no elenco** (achado em 21/11/2026, véspera da final)
    - **O problema:** a prancheta e o elenco do time (/api/times/{id}/prancheta) listam quem tem estatística pelo clube na
      temporada. Por isso o Hulk, que foi para o Fluminense, aparecia no Galo com nota 83, a 2ª maior do elenco. Também
      apareciam o Junior Alonso e o Iván Román, sem jogo pelo Galo desde 15/09.
    - **O risco:** vídeo ou roteiro citando jogador que não está mais no time.
    - **Fazer:**
      - marcar como "fora do elenco" quem não aparece em nenhuma escalação ou banco do clube nos últimos 60 dias;
      - quando der, confirmar pela transferência (API-Football /transfers ou Bzzoiro: elenco por time e transferências);
      - na prancheta, no elenco, na provável e no chat, só entra quem está no elenco atual;
      - na nota da temporada, mostrar "saiu em <mês>" em vez de sumir com o atleta.

22. **Ferramentas para crescer o canal: testar no planejamento de segunda (23/11/2026)** — conversa de 22/11/2026
    - **Fazer:** na manhã de segunda (23/11), antes do pré-jogo do Galo x Corinthians de quarta.
    - **Por quê:**
      - Os Shorts alternam picos (provável: 387) e quedas, o X quase não entrega e o TikTok novo não pegou.
      - Dos vídeos longos, os que funcionaram foram os de antes do jogo, com rosto na miniatura:
        - Os garotos do Galo: 91;
        - Galo x Grêmio: dá pra virar?: 68;
        - Paredes x Fred: 52.
      - Os de depois do jogo com miniatura só de cartão ficaram em 42 e 5.
    - **Para testar:**
      - **YouTube Studio, grátis:**
        - Insights → aba "Pesquisa": o que o público busca. Usar no título do pré-jogo do Corinthians.
        - "Testar e comparar": A/B de título e miniatura no próximo vídeo longo, com rosto x só cartão.
      - **Metricool, plano grátis:**
        - Painel único de YouTube, IG, FB, TikTok e X, com relatório semanal e melhores horários de cada rede.
        - O Leandro cria a conta e conecta as redes. Depois disso, o planejamento usa os dados de lá.
      - **Google Trends:** buscas sobre o Galo em MG antes e depois do jogo, para escolher título e gancho.
      - **TikTok Creative Center:** hashtags e sons em alta no Brasil, para o @galoemnumeros.
      - **vidIQ, funções que ainda não usamos:**
        - comment_insights e subscriber_insights;
        - title_thumbnail_wins;
        - postagem social: se publicar Reels e TikTok pelo vidIQ, troca parte do trabalho manual no Chrome. Testar com 1 vídeo.
    - **Fora:** TubeBuddy (faz o mesmo que o vidIQ) e corte automático tipo Opus Clip (a edição já é nossa).
    - **Resultado esperado:** decidir quais ficam na rotina semanal e registrar no plano da semana.

## Mapa de calor e finalizações no campo (levantamento de 03/11/2026)

FEITO em 03/11/2026 com a PitchAPI (Série A 2023-2026 confirmada): 4º cartão do "Para vídeo" e contagens de finalização no roteiro.

A API-Football (a nossa) não tem coordenadas de lance. Opções vistas:
- **PitchAPI** (pitchapi.dev): grátis, chave por cadastro, só pós-jogo. Finalizações com x/y em metros, xG e xGOT, e mapa de calor
  em grade 16x12 por time e atleta. Cita a Série B do Brasil (desde 2024); a Série A não aparece nomeada (conferir em /v1/leagues).
  Sem termos de uso publicados nem SLA: serviço pequeno, pode sumir. **Primeiro teste.**
- **TheStatsAPI**: a partir de US$ 50/mês (7 dias grátis), todas as ligas no mesmo plano. Anuncia mapa de calor e shot map, mas o
  exemplo do Brasileirão vem com "xg_available": false. Só pagar depois de ver no teste grátis que a Série A tem os dois.
- **Sportmonks**: €29 a €249/mês por nº de ligas; xG da Série A do Brasil (liga 648) como add-on. Não vende mapa de calor.
- **Hudl StatsBomb**: o dado de verdade (eventos com posição e 360), preço sob consulta, para clubes (Galo e Cruzeiro são clientes).
- **SofaScore / FotMob**: têm mapa de calor, mas sem API oficial; raspar viola os termos. Fora, ainda mais num canal público.

- **Prancheta: setas e os dois times** — FEITO em 09/11/2026: modo seta (movimento amarelo, passe branco tracejado; clique apaga),
  "Mostrar o adversário" quando aberta de um jogo (escalação provável dele espelhada, cores dele ou cinza se parecerem), selo com
  os dois esquemas; tudo também no modo grande e na imagem baixada.

## Para a ferramenta

6. **"Mais de dois gols" ambíguo** — FEITO em 09/11/2026 (golsNoJogoMinimo no procurar_partidas; na dúvida, as duas leituras): quando a pergunta puder ser soma ou diferença, responder as duas leituras em uma linha
   (hoje vale a diferença, regra de 03/11).
7. **Data relativa nas respostas** — FEITO em 09/11/2026 (campos Quando e JogosDepois): além de 12/08/2026, dizer "há 12 semanas, 21 jogos", que é o que a gente fala no vídeo.
8. **Banco local desatualizado** — FEITO em 09/11/2026 (`deploy/banco-local.sh usuario@servidor`): script para trazer os placares mais recentes de produção (ou rodar a coleta) antes de testar.

## Custos

9. **Sair do Pro da API-Football (US$ 19/mês)** — revisto em 08/11/2026.
   - **Descoberta:** o plano grátis só libera as temporadas 2022 a 2024 (temporada atual devolve "Free plans do not have access
     to this season"). Cortar para 100 requisições/dia NÃO resolve: no grátis o Scout não recebe nada de 2026.
   - **Decisão do Leandro (08/11):** renovar o Pro por mais 1 mês para fechar a temporada 2026 com o Scout completo e, nesse mês,
     construir a coleta pela PitchAPI (grátis) para 2027 não depender do Pro.
   - Plano da migração:
     1. Tabela nova de estatística por partida e atleta, vinda da PitchAPI (/v1/matches/{id}/players e /advanced/players),
        ligando o atleta da PitchAPI ao nosso por nome + time (como ligarJogador no ParaVideo.ts). Serve também ao item 10 v2.
        **FEITO em 08/11/2026:** tabela desempenhos_avancados (ColetaPitch + LigarNomes), roda na AtualizacaoDiaria depois da
        API-Football (até 150 jogos/dia) e pelo comando `pitch <liga> <temporada> [max]` do Scout.Coleta. Teste local: 10 jogos,
        250 de 254 atletas ligados ao cadastro (os 4 restantes ficam sem ligação de propósito, na dúvida não liga).
     2. Placar, escalação, lances e estatísticas do time por jogo pela PitchAPI, no lugar das 3-4 consultas por jogo.
     3. Somar a temporada a partir dos jogos (hoje vem pronta do /players da API-Football).
     4. O que a PitchAPI não tem (conferido em 08/11): copas (Copa do Brasil, Libertadores, Sul-Americana; no Brasil só tem
        Série A, Série B e Paulistão), data de nascimento (regra dos 18) e elenco do time (/teams/{id}/squad dá 404).
        Testar a Bzzoiro (item 11) e o ESPN para isso.
   - Antes de vencer o mês renovado: decidir se 2027 começa só com a PitchAPI ou se renova de novo.

## Cartões de atleta

10. **Cartão do atleta para vídeo (por 90 min, percentil e ranking)** — ideia de 06/11/2026, a partir de um "playercard" de outro perfil.
    - **v1 feita em 08/11/2026** (só API-Football): GET /api/atletas/{id}/cartao e botão "Cartão para vídeo" no painel do
      atleta; ranking contra o mesmo setor na liga. Falta: comparação com todos os de linha e as métricas da PitchAPI.
    - No "Para vídeo" do painel do atleta, no visual do canal (Galo na Lupa), 1080x1920.
    - Blocos com o que a API-Football tem: Passe (passes certos, precisão, passes decisivos, assistências), Defesa (desarmes,
      interceptações, bloqueios), Duelos (ganhos e %, faltas sofridas), Ataque (finalizações, no alvo, gols, dribles certos e %),
      Disciplina (faltas, cartões). Cada número por 90 min, com percentil (cor) e "3º de 413" contra atletas com 450+ min
      (MotorDeAnalise.MinutosMinimos), comparando com o mesmo setor e com todos os de linha.
    - PitchAPI tem quase tudo do cartão, POR JOGO (conferido em 06/11 com Botafogo x Galo, m_2KAgmB):
      /v1/matches/{id}/players (passes certos, passes_into_final_third, long_balls_accurate, touches, recoveries, duelos,
      xG, xA) e /v1/matches/{id}/advanced/players (progressive_passes, passes_into_box, switches, through_balls, crosses,
      progressive_carries, carries_into_final_third, take_ons, sca/gca, xag, xg_chain, xt, vaep, tackles, interceptions).
      Não há total da temporada: somar jogo a jogo numa tabela nova (estatística avançada por partida e atleta), ligando o
      atleta da PitchAPI ao nosso pelo nome + time (como ligarJogador no ParaVideo.ts).
    - Carga inicial ~700 consultas (34 rodadas x 10 jogos x 2), depois ~20 por rodada; sem cota publicada, só freio de
      excesso (429 com Retry-After): ir devagar.
    - Primeira versão pode sair só com a API-Football (o que já está no banco); a avançada entra depois.

## Fontes de dados para testar

11. **Bzzoiro Sports Data (sports.bzzoiro.com)** — TESTADA em 08/11/2026 com a chave (BZZOIRO_KEY no .env local; header
    `Authorization: Token <chave>`). Cobre o que falta na PitchAPI:
    - Ligas e copas de 2026: Série A (9), Série B (34), **Copa do Brasil (35), Libertadores (32), Sul-Americana (33)**.
    - **Elenco por time** (/api/players/?team=155 = Galo, 46 atletas) com **data de nascimento** (Gutte 2008-04-05, igual à
      ficha do Sub-17), posição, altura, peso, pé, contrato, valor de mercado e transferências.
    - Jogo (/api/events/{id}/): placar, xG real, treinadores, escalações com nota, desfalques, árbitro, público, confronto.
      Galo x Grêmio da Copa do Brasil (602319) veio completo.
    - **Estatística por atleta e jogo** (/api/player-stats/?event={id}): minutos, nota, gols, xA, finalizações, passes, passes
      decisivos, cruzamentos, bolas longas, toques, dribles, duelos, aéreos, desarmes, cortes, interceptações, recuperações,
      faltas, cartões, defesas, pênaltis.
    - Cuidados: há duplicata no cadastro (Gutte aparece duas vezes, com 04 e 05/04/2008); ids próprios (ligar por nome + time,
      como na PitchAPI); serviço novo e sem garantia, igual à PitchAPI.
    - **Conclusão:** PitchAPI (ligas: mapas e métricas avançadas) + Bzzoiro (copas, elencos, nascimento, estatística por jogo)
      cobrem o Scout sem o Pro da API-Football. Próximo passo: ColetaBzzoiro (elencos e nascimento primeiro, depois os jogos de
      copa) e, em dezembro, um teste de uma semana com o Scout lendo só das duas antes de decidir não renovar.
    - **Nascimentos FEITO em 08/11/2026** (ColetaBzzoiro.PreencherNascimentos; roda na AtualizacaoDiaria e pelo comando
      `nascimentos-bzzoiro <temporada>`): só preenche quem está sem data, ignora 1º de janeiro (data de quem só sabe o ano) e
      duplicatas com datas diferentes. Teste local: 141 de 516 atletas sem data (41 de 110 clubes ligados; os demais são de
      competições que a Bzzoiro não tem).
    - **Jogos de copa FEITO em 08/11/2026** (ColetaBzzoiro.ColetarCopas; até 40 jogos/dia e comando `copas-bzzoiro <temporada>
      [max]`): desempenho por atleta nos jogos de Copa do Brasil, Libertadores e Sul-Americana com time da Série A, na mesma
      tabela da PitchAPI (desempenhos_avancados, coluna fonte). Teste local: 15 jogos, 390 de 398 atletas ligados.
    - **Argentina (conferido em 17/11/2026):** a PitchAPI tem a Liga Profesional 2024-2026 (l_45VZuL, grátis: 497 jogos em 2026,
      33 do Boca, com /advanced/players: xT, VAEP, passe progressivo, SCA) e a Bzzoiro também (liga 85). Serve para analisar o
      adversário argentino (Boca na final) e, se quiser, cadastrar a liga no Scout.
    - Falta para 2027 sem o Pro: criar a partida a partir da Bzzoiro/PitchAPI quando a API-Football não tiver o jogo (hoje a
      coleta só complementa jogos que já estão na base) e montar escalação, placar e estatística do time a partir delas.

## Imagem e direitos (escudos, fotos, cores)

12. **Modo gravação com escudos e cores dos clubes, sem fotos** — conversa de 06/11/2026.
    - **Feito em 08/11/2026:** escudos aparecem no modo gravação (fotos seguem escondidas); cores por clube em web/src/Cores.ts
      nos cartões do jogo (placar, barras, destaques, anel das notas, mapas) e nas bolinhas da prancheta. Times de cores
      parecidas: o segundo fica cinza.
    - Hoje o "Modo gravação" esconde escudos e fotos. Mudar para: escudos aparecem (pequenos, em placar e tabela: uso
      informativo, sem parecer oficial), fotos de atletas continuam escondidas.
    - Cartões do "Para vídeo" e da prancheta com as cores de cada clube (ex.: Grêmio azul e preto) no lugar do verde único.
      Cor sozinha não é protegida.
    - Fotos nos vídeos: só de divulgação do clube para a imprensa, sempre com crédito ("Foto: Atlético/divulgação"), ou com
      licença livre (Wikimedia Commons). Evitar foto de agência (Getty, AGIF, Reuters): é de onde vêm as denúncias de direito
      autoral (3 derrubam o canal). Menor de 18: só foto de divulgação, nunca de rede social pessoal.
    - Nunca imagem de transmissão (direito de arena): o maior risco de bloqueio.
    - Quando o canal monetizar ou o Scout for vendido a clubes: consultar advogado de direito desportivo.
