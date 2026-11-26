import { useState } from 'react'
import { api, type ResumoAtleta } from './api'
import { CartaoAtleta } from './Componentes'

/** O time do canal (Atlético-MG): "tirar o Galo" da busca, para procurar reforço. */
const GALO = 1062
/** As 10 seleções da Conmebol, com o nome em inglês como o provedor grava a nacionalidade. */
const SUL_AMERICANOS = ['Argentina', 'Uruguay', 'Paraguay', 'Chile', 'Colombia', 'Ecuador', 'Peru', 'Bolivia', 'Venezuela', 'Brazil']

type Props = { gravacao: boolean; onFechar: () => void; onAbrirAtleta: (a: ResumoAtleta) => void }

/**
 * Mapa de mercado na tela (backlog item 23): a mesma busca que o chat usa em "modo mercado" (POST /api/busca com mercado=true),
 * com os filtros à vista. Junta a Série A e as ligas de fora na régua da Série A; por padrão, só quem cabe no bolso de clube
 * brasileiro, quem continua no clube e sem o próprio Galo.
 */
export function PainelMercado({ gravacao, onFechar, onAbrirAtleta }: Props) {
  const [setor, setSetor] = useState('Ataque')
  const [idadeMax, setIdadeMax] = useState('')
  const [valorMax, setValorMax] = useState('')
  const [golsPor90, setGolsPor90] = useState('')
  const [minutos, setMinutos] = useState('900')
  const [realidade, setRealidade] = useState(true)
  const [deFora, setDeFora] = useState(false)
  const [sulAmericanos, setSulAmericanos] = useState(false)
  const [estimadas, setEstimadas] = useState(false)
  const [elencoAtual, setElencoAtual] = useState(true)
  const [semGalo, setSemGalo] = useState(true)
  const [resultado, setResultado] = useState<{ encontrados: number; atletas: ResumoAtleta[]; observacoes: string[] } | null>(null)
  const [buscando, setBuscando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  const numero = (s: string) => (s.trim() === '' ? null : Number(s.replace(',', '.')))

  async function buscar() {
    setBuscando(true); setErro(null)
    try {
      const valor = numero(valorMax)
      setResultado(await api.busca({
        mercado: true, limite: 20,
        setor: setor || null,
        idadeMax: numero(idadeMax),
        valorMaximo: valor != null ? Math.round(valor * 1_000_000) : null,
        golsPor90Minimo: setor === 'Ataque' ? numero(golsPor90) : null,
        minutosMinimos: numero(minutos),
        realidadeBrasileira: realidade,
        soLigasDeFora: deFora,
        nacionalidades: sulAmericanos ? SUL_AMERICANOS : null,
        incluirEstimadas: estimadas,
        soElencoAtual: elencoAtual,
        excluirClubeId: semGalo ? GALO : null,
      }))
    } catch (e) { setErro((e as Error).message) } finally { setBuscando(false) }
  }

  return (
    <div className="fundo" onClick={onFechar}>
      <div className="painel largo" role="dialog" aria-modal aria-label="Mapa de mercado" onClick={(e) => e.stopPropagation()}>
        <h2>🔎 Mapa de mercado</h2>
        <p className="legenda">Série A e ligas de fora na mesma régua (a nota de fora é convertida para a Série A). Clique num atleta para ver o perfil, a trajetória e os cartões.</p>
        <form className="mercado-filtros" onSubmit={(e) => { e.preventDefault(); buscar() }}>
          <label>Setor
            <select value={setor} onChange={(e) => setSetor(e.target.value)}>
              <option value="Ataque">Ataque</option><option value="Meio">Meio</option><option value="Defesa">Defesa</option>
              <option value="Goleiro">Goleiro</option><option value="">Todos</option>
            </select>
          </label>
          <label>Idade máxima<input inputMode="numeric" value={idadeMax} onChange={(e) => setIdadeMax(e.target.value)} placeholder="ex.: 27" /></label>
          <label>Valor até (€ mi)<input inputMode="decimal" value={valorMax} onChange={(e) => setValorMax(e.target.value)} placeholder="ex.: 8" /></label>
          {setor === 'Ataque' && <label>Gols por 90 a partir de<input inputMode="decimal" value={golsPor90} onChange={(e) => setGolsPor90(e.target.value)} placeholder="ex.: 0,40" /></label>}
          <label>Minutos mínimos<input inputMode="numeric" value={minutos} onChange={(e) => setMinutos(e.target.value)} /></label>
          <div className="mercado-opcoes">
            <label title="No máximo o valor dos 10% mais caros da Série A no mesmo setor"><input type="checkbox" checked={realidade} onChange={(e) => setRealidade(e.target.checked)} /> Cabe no bolso de clube brasileiro</label>
            <label><input type="checkbox" checked={deFora} onChange={(e) => setDeFora(e.target.checked)} /> Só ligas de fora</label>
            <label><input type="checkbox" checked={sulAmericanos} onChange={(e) => setSulAmericanos(e.target.checked)} /> Só sul-americanos</label>
            <label title="MLS, México e outras ligas sem confronto direto com a Série A: a nota convertida é estimativa"><input type="checkbox" checked={estimadas} onChange={(e) => setEstimadas(e.target.checked)} /> Incluir ligas com régua estimada</label>
            <label title="Tira quem saiu do clube ou não joga há mais de 60 dias"><input type="checkbox" checked={elencoAtual} onChange={(e) => setElencoAtual(e.target.checked)} /> Só quem continua no clube</label>
            <label><input type="checkbox" checked={semGalo} onChange={(e) => setSemGalo(e.target.checked)} /> Sem os atletas do Galo</label>
          </div>
          <div className="acoes"><button className="btn prim" type="submit" disabled={buscando}>{buscando ? 'Buscando…' : 'Buscar'}</button></div>
        </form>
        {erro && <p className="erro">{erro}</p>}
        {resultado && (
          <section>
            <h3>{resultado.atletas.length ? `${resultado.atletas.length} de ${resultado.encontrados} encontrados` : 'Ninguém com esses filtros'}</h3>
            {resultado.observacoes.map((o) => <p key={o} className="obs">{o}</p>)}
            <div className="cards">
              {resultado.atletas.map((a) => <CartaoAtleta key={`${a.id}-${a.temporada}`} atleta={a} gravacao={gravacao} onAbrir={() => onAbrirAtleta(a)} />)}
            </div>
          </section>
        )}
        <div className="acoes"><button className="btn" onClick={onFechar}>Fechar</button></div>
      </div>
    </div>
  )
}
