# 🗺️ Transit Empire — Roadmap de Melhorias Pendentes
*(Inspiração Principal: **Voxel Tycoon** & **Transport Fever 2**)*

> **Histórico Consolidado:** Todas as mecânicas base (**Fases 1 a 8**) e todo o plano diretor gráfico (**Sprints G1 a G4**) já foram **100% implementados e validados**. Este documento organiza exclusivamente o **backlog restante em ordem técnica recomendada** (da fundação de desempenho até o polimento audiovisual final).

---

## 🚀 Backlog Reorganizado — Sprints E1 a E7 (Ordem de Dependência & Implementação)

### ✅ Sprint E1 — Fundação de Desempenho & Escalabilidade (`Engine & Render`) — *Concluído*
*Base obrigatória antes de adicionar dezenas de novos veículos, rotas dinâmicas e megaestruturas ao mapa.*
- [x] **1. Batching / Mesh Merging da Malha Viária por Setor (`Chunked Road Mesh`):**
  - Agrupar a geometria estática das estradas em *chunks* `10x10` via `SurfaceTool`, reduzindo drasticamente nós `MeshInstance3D` e *draw calls* em redes rodoviárias extensas (`ProceduralModels3D.CreateRoadChunkNode` + `MainView.RebuildRoadChunk` / `RebuildAllRoadChunks`).
- [x] **2. Cache de Rotas A\* com Invalidação por Evento (`Pathfinding Route Cache`):**
  - Armazenar em cache caminhos calculados por `(OriginNodeId, TargetNodeId, RequiresHighway, WeatherBucket)` (`RoadPathfinder`) com cópias defensivas, estatísticas de hits/misses e invalidação automática na `SimulationEngine` em construção/upgrade/repave viário, semáforos, desbloqueio regional e deslizamentos (`MountainLandslide`).
- [x] **3. LOD & Frustum Throttling para Caminhões e Partículas:**
  - Suspender cálculo de rotação individual de rodas, esterçamento e partículas secundárias (`MaintenanceSmoke`, `TireDust`, `RainSpray`) e animações industriais finas fora do frustum ou com distância à câmera acima de `45m`/`55m`.

---

### ✅ Sprint E2 — Ferramentas de Mapa, Automação & Controle de Frota (`Core UX`) — *Concluído*
*Estabelece a estrutura de Linhas Logísticas, demolição integrada aos chunks/cache da E1 e seleção direta 3D.*
- [x] **1. Linhas Logísticas Compartilhadas (`Shared Route Templates`):**
  - Criar e salvar **Linhas Nomeadas** (`LogisticsLine`, `SimulationEngine.CreateLogisticsLine` / `UpdateLogisticsLine`, persistidas no `SaveGameSerializer`); editar as paradas ou nome de uma Linha atualiza automaticamente todos os caminhões vinculados (`AssignedLineId`).
- [x] **2. Ferramenta de Demolição (`🚜 Bulldozer`) & Reembolso Parcial:**
  - Ferramenta na barra inferior (`ToolMode.Demolish`, `SimulationEngine.TryDemolishAt`) para demolir estradas obsoletas, garagens, hubs e pedágios, reembolsando `40%` do custo, removendo visuais/túneis, reconstruindo os *chunks* `10x10` vizinhos e invalidando o cache A\*.
- [x] **3. Seleção Direta de Caminhões no Mapa 3D (`3D Truck Picking`):**
  - Clicar diretamente sobre qualquer caminhão no mapa 3D (`TryPickTruckAtScreenPos` / `SelectAndFocusTruck`) para selecioná-lo imediatamente, destacar sua rota (`SelectedTruckRoute3D`) e focar sua Agenda e câmera PiP.
- [x] **4. Upgrade Automático de Frota (`Fleet Modernizer`):**
  - Substituição rápida na Concessionária/Frota (`SimulationEngine.TryUpgradeTruckModel`, botão `⬆️` por veículo e painel de modernização): vende veículo antigo por `60%` do valor e transfere instantaneamente sua Linha Logística, agenda e carga compatível para o novo modelo.

---

### ✅ Sprint E3 — Identidade da Transportadora, Showroom 3D & Veterania (`Frota & Livery`) — *Concluído*
*Cria o sistema unificado de pintura (`Livery`) nos modelos 3D — que será reaproveitado pelas transportadoras rivais na Sprint E4.*
- [x] **1. Oficina de Pintura & Identidade Visual da Frota (`Company Livery Editor`):**
  - Personalizar **Nome da Transportadora**, **Cor Primária**, **Cor das Faixas** e **Acabamento (Metálico / Fosco / Cromado)** na Concessionária (`FleetLivery`, `PlayerState.CompanyName`, `SimulationEngine.UpdateCompanyLivery` / `UpdateLineLivery`), aplicável a toda a frota ou por `LogisticsLine`, persistido no `SaveGameSerializer`.
- [x] **2. Showroom 3D Interativo na Concessionária (`In-Game Truck Showcase`):**
  - Plataforma giratória 3D ao vivo (`SubViewport` isolado com `OwnWorld3D`, órbita por arraste/zoom) dentro da aba **Frota/Concessionária** (`Showroom 3D & Pintura`) para inspecionar os 6 caminhões em 360°, acionar faróis, testar cargas 3D e visualizar pintura em tempo real antes da aplicação/compra.
- [x] **3. Veterania de Caminhão & Motoristas Especialistas (`Truck Rank`):**
  - Evolução por quilometragem (`★ Novato` < 50 km $\rightarrow$ `★★ Veterano` >= 50 km $\rightarrow$ `★★★ Lenda da Estrada` >= 150 km), concedendo economia de combustível (até +10% via `FuelCostMultiplier`), velocidade em subida/serra (até +12% via `HillClimbSpeedMultiplier`), evento `OnTruckRankUp` e insígnia dourada visível no teto do veículo (`ProceduralModels3D.CreateTruckMeshNode`).

---

### ✅ Sprint E4 — Ecossistema Rodoviário Vivo: Rivais, Tráfego Civil & Balanças (`Tráfego NPC`) — *Concluído*
*Usa o cache A\* e LOD da E1 + o pipeline de pintura da E3 para popular as rodovias com tráfego concorrente e civil.*
- [x] **1. Frota Visível das Empresas Rivais nas Estradas (Pedágio Real):**
  - Caminhões das empresas rivais (`TrafficVehicle`, `TrafficVehicleKind.RivalTruck`, pintados com `RivalCompany.ColorHex`) circulam fisicamente pelas rodovias (`SimulationEngine.SpawnRivalTruck` / `ProcessNpcTraffic`); ao cruzar uma `TollStation` do jogador, acionam a cancela 3D e pagam pedágio real para o caixa.
- [x] **2. Tráfego Civil & Ônibus Intermunicipais (`Ambient City Traffic`):**
  - Carros de passeio voxel e ônibus rodoviários (`CivilCar`, `IntercityBus`, `ProceduralModels3D.CreateTrafficVehicleNode`) trafegam entre cidades conectadas proporcionalmente à população urbana, disputando espaço em `ActiveTrucksCount` e valorizando `Highway` / `DualCarriage`.
- [x] **3. Fiscalização na Balança 3D & Comboios Especiais (`Convoy & Weigh Events`):**
  - Letreiro luminoso 3D nas Praças de Pedágio/Balança (`TollStation.LastWeighDisplay` + `OnTollWeighUpdated` + `Label3D` no pórtico) mostrando pesagem em tempo real (`45t OK` / `74t RODOTREM` / `RIVAL OK`) + contratos de **Carga Indivisível / Comboio Escoltado** (`LogisticsContract.IsSpecialConvoy`, `CreateSpecialConvoyContract`) com viatura escolta batedora + giroflex alternado em 3D (`EscortLead`) e recompensa tripla (`3x`).

---

### ✅ Sprint E5 — Terminais Portuários & Mega-Obras Regionais (`Endgame Logístico`) — *Concluído*
*Destinos de altíssima escala para escoar a produção de frotas modernizadas e comboios pesados.*
- [x] **1. Porto Marítimo de Contêineres & Graneleiro (`Seaport Terminal`):**
  - Terminal costeiro com guindastes *Portainer* 3D animados e navio cargueiro voxel periódico (`SeaportTerminal`, `ScheduleStopKind.Seaport`, `ProceduralModels3D.CreateSeaportNode`), oferecendo **bônus de exportação (+45% por tonelada)** para grandes carregamentos de `Grain`, `Steel`, `Lumber`, `Goods` e `Timber` antes de zarpar (ciclo `120h` atracado / `60h` em alto-mar).
- [x] **2. Mega-Obras Regionais (`Regional Landmarks`):**
  - Projetos em 3 etapas no mapa (*Usina Hidrelétrica*, *Estádio Municipal*, *Parque Eólico* via `RegionalLandmark`, `ScheduleStopKind.RegionalLandmark`, `ProceduralModels3D.CreateLandmarkNode`) que consomem grandes volumes de `Steel`, `Lumber` e `Goods` e concedem bônus permanentes globais (`+15%` frete / `-10%` manutenção na Hidrelétrica, `+25%` XP no Estádio, `-15%` diesel / `+10%` produção industrial no Parque Eólico).

---

### ✅ Sprint E6 — Inteligência Operacional & Conquistas (`Gestão & Metagame`) — *Concluído*
*Com todos os sistemas de economia, tráfego e obras ativos, entrega diagnóstico cirúrgico e progressão de longo prazo.*
- [x] **1. Painel de Diagnóstico de Gargalos (`Bottleneck Analyzer`):**
  - Assistente em tempo real (`OperationalDiagnostics.Analyze` + `SimulationEngine.DiagnoseBottlenecks` + aba **Operações → Diagnóstico**) que detecta vias bloqueadas, asfalto deteriorado, corredores congestionados (frota + NPCs), indústrias paradas por falta de insumo, filas/desgaste crítico de caminhões e cidades desabastecidas com botão **`🔍 Ir`** para focar a câmera no local.
- [x] **2. Sistema de Conquistas & Marcos Históricos (`Milestones`):**
  - Catálogo de 6 troféus permanentes (`MilestoneTracker` + persistência em `SaveGameSerializer` + aba **Operações → Conquistas**) incluindo *Rei da Serra* (10 túneis, `+12%` serra), *Frota Centenária* (100.000 km, `-8%` combustível), *Barão do Asfalto* (ligações urbanas pavimentadas, `+10%` fretes/pedágios), *Magnata dos Mares* (`+10%` portos), *Arquiteto Regional* (`+15%` contratos) e *Império Metropolitano* (`+10%` XP global).

---

### ✅ Sprint E7 — Áudio Espacial 3D & Modo Cinematográfico (`Imersão Final`) — *Concluído*
*Polimento sensorial para coroar o mundo vivo completo.*
- [x] **1. Áudio Espacial 3D Dinâmico por Zoom (`AudioSynth` Evoluído):**
  - Síntese procedural em tempo real (`ProceduralAudioSynth.UpdateSpatialTruckAudio`, `PlayAirHorn`) acionada por proximidade de zoom e *Chase Cam* (`V`), gerando ronco diesel variável por velocidade/marcha com pan estéreo 3D, cadência de freio-motor nas serras (`Hills`/`Mountain`/`AlpineBiome`) e buzina a ar automática ao cruzar áreas urbanas (ou manual com `H`).
- [x] **2. Modo Foto & Tour Cinematográfico Automático (`Photo & Drone Mode`):**
  - Atalho `F12` e botão **`📸 Foto`** ocultando todo o HUD regular (`_hudRegularLayer`), com barra de estúdio ao vivo para travar hora do dia, alternar clima (`Limpo / Chuva / Tempestade / Nevasca`), aplicar filtros (`Retrô Anos 90 / Maquete Forte / Pôr do Sol Quente`), capturar `PNG` e ativar a **Câmera Drone Automática** passeando pela frota, porto marítimo, mega-obras e cidades.

---

## 🗓️ Resumo do Cronograma Reorganizado

| Sprint | Foco Principal | Por que nesta ordem? | Status |
| :--- | :--- | :--- | :--- |
| **Sprint E1** | **Desempenho & Escalabilidade** (`Chunked Road Mesh`, `Route Cache`, `LOD`) | Garante FPS e cache A\* rápido antes de multiplicar nós, caminhões e NPCs | ✅ Concluído |
| **Sprint E2** | **Ferramentas & Linhas** (`Shared Routes`, `🚜 Bulldozer`, `Truck Picking`, `Fleet Modernizer`) | Consolida linhas e remoção de vias integrando direto com os chunks e cache da E1 | ✅ Concluído |
| **Sprint E3** | **Identidade, Showroom & Veterania** (`Livery`, `Showroom 3D`, `Truck Rank`) | Cria o sistema unificado de pintura 3D que será herdado pelos caminhões rivais | ✅ Concluído |
| **Sprint E4** | **Ecossistema Rodoviário Vivo** (`Rivais + Pedágio`, `Tráfego Civil`, `Balança & Comboios`) | Popula as estradas aproveitando cache/LOD (E1) e pintura 3D (E3) | ✅ Concluído |
| **Sprint E5** | **Endgame Logístico** (`Porto Marítimo`, `Mega-Obras Regionais`) | Cria sumidouros gigantes para frotas pesadas e comboios da E2–E4 | ✅ Concluído |
| **Sprint E6** | **Telemetria & Metagame** (`Bottleneck Analyzer`, `Milestones`) | Audita gargalos de toda a economia completa e premia marcos globais | ✅ Concluído |
| **Sprint E7** | **Imersão Audiovisual** (`Áudio Motor 3D`, `Modo Foto & Drone`) | Polimento final de som espacial e showcase cinematográfico do mundo completo | ✅ Concluído |

---

## 🌟 Fase 2 — Backlog de Futuras Melhorias (Sprints F1 a F5)

*Ordem técnica recomendada: consolidar persistência, instancing em larga escala e mapas maiores antes de multiplicar cadeias produtivas, frotas automatizadas, guerra econômica com rivais e malha ferroviária intermodal.*

### ✅ Sprint F1 — Escalabilidade de Mapa, Preferências & Onboarding (`Engine & UX`) — *Concluído*
*Prepara o cliente 3D e o gerador de mundo para mapas extensos, configurações persistentes e entrada guiada para novos jogadores.*
- [x] **1. Persistência de Preferências Gráficas & Áudio (`user://settings.json`):**
  - Salvar e restaurar entre sessões o preset gráfico (`Performance` / `Balanced` / `DioramaUltra`), estados individuais (SSAO, Glow, SSR, Tilt-Shift, VSync, escala de resolução/fonte do HUD) e volumes do `ProceduralAudioSynth`.
- [x] **2. Instancing em `MultiMeshInstance3D` de Árvores & Rochas por Chunk:**
  - Agrupar elementos estáticos de vegetação/terreno por setor em `MultiMesh`, reduzindo ainda mais *draw calls* e tempo de inicialização em mapas densos.
- [x] **3. Tamanhos de Mapa Configuráveis (`50x50` / `80x80` / `100x100`) & Terraplenagem:**
  - Permitir escolher o tamanho do mapa ao iniciar nova partida e adicionar ferramenta simples de nivelamento/corte de terreno antes da pavimentação.
- [x] **4. Tutorial Interativo Guiado & Cenários Temáticos:**
  - Roteiro inicial em 5 passos (*conectar fazenda → comprar caminhão → criar linha → pavimentar → fechar mês no lucro*) e modos pré-definidos (*Arquipélago Costeiro*, *Cordilheira Alpina*).

---

### ✅ Sprint F2 — Automação Avançada de Frota & Agenda Inteligente (`Core Operações`) — *Concluído*
*Dá ao jogador ferramentas de escala para gerir dezenas de caminhões sem microgerenciamento repetitivo.*
- [x] **1. Clonagem Rápida na Garagem (`Clone Truck`):**
  - Copiar em 1 clique modelo, pintura (`Livery`) e `LogisticsLine` de um veículo existente diretamente para um novo caminhão.
- [x] **2. Regras Condicionais por Parada na Agenda:**
  - Permitir configurar por parada: *Aguardar Carga Completa (`Wait for Full Load`)*, *Tempo Máximo de Espera* e filtro explícito de recursos a carregar/descarregar.
- [x] **3. Controle de Espaçamento de Comboio (`Headway Control`):**
  - Espaçamento automático na parada inicial para evitar que vários caminhões da mesma `LogisticsLine` viajem enfileirados deixando terminais ociosos.
- [x] **4. Substituição Automática por Desgaste/Idade (`Auto-Replace`):**
  - Regra configurável por Garagem ou `LogisticsLine` para modernizar/substituir automaticamente caminhões que atingirem limite de desgaste ou quilometragem.

---

### ✅ Sprint F3 — Cadeia Produtiva Tier 3, Combustível Real & Passageiros (`Economia`) — *Concluído*
*Aprofunda o papel estratégico das refinarias, indústrias pesadas e mobilidade urbana entre cidades.*
- [x] **1. Transporte de Passageiros Intermunicipal (`Intercity Bus Lines`):**
  - Terminais rodoviários urbanos e linhas de ônibus próprias entre cidades, gerando receita previsível por passagem e acelerando o crescimento populacional das cidades atendidas.
- [x] **2. Cadeia Industrial Tier 3 (`Carvão & Materiais de Construção / Maquinário`):**
  - Introduzir etapas avançadas combinando `IronOre` + `Coal` para `Steel` e `Steel` + `Lumber` + `Fuel` para suprir demandas de cidades grandes e Mega-Obras.
- [x] **3. Logística Real de Combustível (`Fuel Supply`):**
  - Abastecer Garagens e `LogisticsHub` com `Fuel` reduz o custo operacional por km na região e evita sobretaxa de diesel escasso nas viagens longas.

---

### ⏳ Sprint F4 — Cidades Vivas & Concorrência Ativa com Rivais (`Mundo & Estratégia`) — *Planejado*
*Transforma o crescimento urbano e as empresas rivais em pressões reais de planejamento rodoviário.*
- [ ] **1. Expansão Física por Bairros & Anéis Viários:**
  - Cidades prósperas expandem quarteirões residenciais/comerciais adjacentes e aplicam penalidade de trânsito pesado no centro, incentivando anéis viários periféricos e contornos expressos.
- [ ] **2. Guerra Econômica com `RivalCompany`:**
  - Rivais disputam contratos exclusivos em leilão de tarifa/prazo e constroem trechos próprios com pedágio ativo, cabendo ao jogador pagar a tarifa rival ou projetar um desvio competitivo.

---

### ⏳ Sprint F5 — Expansão Multimodal: Ferrovias & Pátios Intermodais (`Endgame Ferroviário`) — *Planejado*
*Coroa o jogo com corredores férreos de altíssima tonelagem integrados à malha rodoviária e aos portos.*
- [ ] **1. Malha Férrea & Locomotivas de Carga:**
  - Construção de trilhos, viadutos e túneis ferroviários com composições de vagões especializadas para minério, grãos, madeira e contêineres.
- [ ] **2. Sinalização Ferroviária por Blocos (`Block Signals`):**
  - Sinais direcionais e desvios de cruzamento para operar múltiplos trens com segurança no mesmo corredor.
- [ ] **3. Pátios Rodoferroviários (`Intermodal Hub`):**
  - Baldeação automática *Caminhão ↔ Trem ↔ Porto Marítimo (`SeaportTerminal`)* conectando a capilaridade rodoviária das cidades/fazendas às grandes artérias férreas.

---

## 🗓️ Resumo do Cronograma — Fase 2

| Sprint | Foco Principal | Por que nesta ordem? | Status |
| :--- | :--- | :--- | :--- |
| **Sprint F1** | **Escalabilidade, Settings & Tutorial** (`MultiMesh`, `80x80/100x100`, `settings.json`) | Base técnica e performance para suportar mapas maiores e retenção inicial | ✅ Concluído |
| **Sprint F2** | **Automação de Frota** (`Clone`, `Wait Full Load`, `Headway`, `Auto-Replace`) | Elimina microgestão antes de ampliar cadeias de carga e número de linhas | ✅ Concluído |
| **Sprint F3** | **Economia & Passageiros** (`Ônibus`, `Tier 3`, `Combustível em Hubs`) | Enriquece fluxos econômicos aproveitando a automação da F2 | ✅ Concluído |
| **Sprint F4** | **Cidades Vivas & Guerra Rival** (`Anéis Viários`, `Leilões & Pedágio Rival`) | Cria desafios dinâmicos de malha e competição sobre a economia completa | ⏳ Planejado |
| **Sprint F5** | **Ferrovias & Intermodal** (`Trilhos`, `Block Signals`, `Hub Rodoferroviário`) | Expansão *endgame* conectando coleta rodoviária, trens pesados e portos | ⏳ Planejado |
