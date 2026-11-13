# 🚛 Transit Empire — Catálogo da Frota 3D, Ícones das Estradas e Engenharia de Pneus

Este documento especifica a modelagem procedural 3D dos **5 caminhões exclusivos** do *Transit Empire* (inspirados em modelos lendários das rodovias), a arquitetura de **carrocerias e cabines 100% sólidas (sem frestas vazadas)** e a geometria em torno (`THREE.LatheGeometry`) dos **Pneus Radiais 295/80 R22.5** com **Rodas de Aço/Alcoa 10 Furos e Cubo Redutor Planetário**.

---

## 1. Os 6 Ícones da Frota 3D (`CreateTruckMeshNode` & `buildUnique3DTruckMesh`)

| ID | Modelo no Jogo | Inspiração Real | Configuração | Capacidade | Vel. Máx | Características Visuais 3D e Solidez |
|:--:|:---|:---|:---:|:---:|:---:|:---|
| **1** | **Utilitário F-4000 Carga Seca** | **Ford F-4000 Série F** | `4x2` (Rodado Duplo) | 12 un. | 65 km/h | Capô Série F inclinado com *Power Dome* central, emblema oval azul na grade cromada dupla, piscas âmbar, estribos laterais cromados, giroflex âmbar no teto e carroceria de madeira maciça fechada. |
| **2** | **Scania 113H TopLine 360** | **Scania 113H 360 Série 3** | `4x2` (6 Pneus R22.5) | 28 un. | 75 km/h | Pintura clássica Azul Celeste (ou Branco TopLine) com as **3 faixas tricolor anos 90 (Rosa, Roxo e Azul Escuro)**, grade frontal Série 3 com 5 frisos, filtro de ar lateral, caixa de estribo fechada, chaminé cromada e graneleiro Guerra/Randon sólido. |
| **3** | **Mercedes-Benz L-1620 Bicudo** | **MB L-1620 ("Rei da Estrada")** | `4x2` (6 Pneus R22.5) | 45 un. | 80 km/h | Capô arredondado e inclinado típico do 1620, grade preta com barra horizontal cromada e estrela central, faixa lateral branca, 2 cornetas de ar no teto e carroceria fechada. |
| **4** | **Volvo FH 540 Globetrotter 6x2** | **Volvo FH 540 Globetrotter** | `6x2` (10 Pneus R22.5) | 80 un. | 85 km/h | Cabine avançada cara-chata de teto alto com **testeira iluminada `GLOBETROTTER`**, barra de 4 faróis de milha LED, grade frontal Volvo com barra diagonal cromada (*Iron Mark*) e **saias aerodinâmicas integrais (*Full Side-Skirts*)**. |
| **5** | **Peterbilt 389 Bi-Trem King** | **Peterbilt 389 / Kenworth W900** | `5 Eixos` (18 Pneus R22.5) | 150 un. | 95 km/h | Clássico norte-americano **Preto Ônix & Faixa Dourada** com capô longo estreito, grade vertical cromada, *bullbar* pesado, *Sleeper Cab* gigante, chaminés e tanques duplos de inox, *dolly* e **2 semirreboques graneleiros fechados**. |
| **6** | **Scania R730 V8 Rodotrem 9 Eixos** | **Scania R730 V8 Topline Streamline** | `9 Eixos` (34 Pneus R22.5) | 220 un. | 90 km/h | Cabine *Topline V8* Azul Imperial com frisos Vermelho Griffin & Ouro V8, grade profunda com emblema `V8 730`, barra de 6 LEDs no teto e composição Rodotrem completa (cavalo `6x4` + 1º Semirreboque + *Dolly* articulado + 2º Semirreboque). |

---

## 2. Arquitetura 100% Sólida (Zero Transparência / Sem Frestas Vazadas)

1. **Carrocerias Graneleiras / Carga Seca Fechadas (`addStylizedOpenCargoBed`):**
   * Substituímos as antigas grades vazadas por **painéis laterais maciços de madeira naval e aço**, com sub-chassi inferior fechando o vão sobre as longarinas, frisos metálicos horizontais, fueiros verticais externos em relevo, protetor lateral inferior (*side underrun*) e para-choque traseiro homologado.
2. **Cabines Emolduradas com Colunas A/B e Vidro Azul-Ardósia (`addFramedCabinWindows`):**
   * Todas as cabines possuem núcleo interno opaco, **colunas A na cor da carroceria**, maçanetas cromadas e vidros embutidos em tom **Azul-Ardósia Metálico (`#1e3a5f` / `#254670`)** — nunca se confundindo com o céu noturno nem parecendo um recorte vazado.
3. **Eixos Rígidos, Diferenciais e Caixas de Roda Internas:**
   * Cada par de rodas conta com barra de eixo rígido transversal, bojo central do diferencial (*pumpkin*) e bloco interno de caixa de roda/para-barro, bloqueando totalmente a visão através da parte inferior do caminhão.

---

## 3. Anatomia do Pneu Radial `295/80 R22.5` e Roda 10 Furos

1. **Carcaça Esculpida em Torno (`THREE.LatheGeometry` de 44–48 Segmentos):**
   * Perfil transversal 2D com talão no aro, **bochecha lateral abaulada**, **ombros arredondados** e **3 sulcos circunferenciais longitudinais de drenagem**.
2. **Micro-Entalhes Transversais nos Ombros (`24 Sipes`):**
   * 24 entalhes escuros nivelados ao raio do ombro (`0.991 * rTire`), mantendo o pneu 100% circular enquanto destacam o giro real ($\omega = v / R$).
3. **Roda de Aço/Alcoa 22.5" (`10 Furos + 10 Prisioneiros + Cubo Redutor Planetário`):**
   * Prato dianteiro convexo com esterçamento ativo e prato traseiro côncavo profundo para rodado duplo, com **10 furos de ventilação**, **10 porcas cromadas**, válvula dourada e **cubo redutor planetário** com 3 nervuras metálicas.

---

## 4. Sistema de Iluminação Noturna & Sinalização Traseira Homologada

1. **Lanternas Traseiras Triplas Embutidas (`addStylizedOpenCargoBed` / `addSolidCargoBedWebGL`):**
   * Fixadas na viga traseira inferior da carroceria (abaixo do assoalho naval, sem qualquer sobreposição geométrica), compostas por moldura de borracha/aço preta (`#0d0f12`) e **3 câmaras luminosas independentes**:
     * **1ª Câmara (Externa):** Freio e Lanterna de Posição Vermelha (`#ef4444`).
     * **2ª Câmara (Central):** Seta Direcional Âmbar (`#f59e0b`).
     * **3ª Câmara (Interna):** Luz de Ré Branca Cristal (`#f8fafc`).
   * Inclui luzes delimitadoras superiores nos cantos da tampa traseira e **para-choque traseiro homologado com faixas refletivas CONTRAN** (vermelho e branco), além de reflexo avermelhado projetado no asfalto à noite.
2. **Faróis Dianteiros Volumétricos Duplos & Animação Noturna (`headlightBeams`):**
   * **Cones Frustum 3D Duplos:** Cone principal halógeno/LED (`rTop = 0.055` na lente abrindo para `rBot = 0.42` à frente, inclinado `-0.095 rad` para o asfalto) + núcleo central de facho alto (*High-Beam Core* branco intenso) + faróis auxiliares (neblinas âmbar no Scania 113H e barra de 4 milhas LED no teto do Volvo FH Globetrotter).
   * **Iluminação no Asfalto e Animação:** Projetam poças elípticas de luz e *SpotLights* reais sobre a pista e faixas rodoviárias à frente do caminhão, com **pulsação sutil de alternador** e **varredura horizontal sincronizada com o esterçamento das rodas dianteiras nas curvas**.

---

## 5. Arquivos Sincronizados no Projeto para Exportação ao GitHub

* [`web_preview/index.html`](../web_preview/index.html): Jogo principal completo + Modal `🎨 Showroom 3D` integrado.
* [`web_preview/showroom.html`](../web_preview/showroom.html): **Showroom 3D Dedicado em Tela Cheia** (Three.js) com câmeras de *Macro Zoom* na roda dianteira, no rodado duplo traseiro e na cabine.
* [`web_preview/trucks_showcase.html`](../web_preview/trucks_showcase.html): **Showcase 3D WebGL Nativo Autocontido** (Zero dependências externas, com Z-Buffer por hardware e *SpotLights* no fragment shader), réplica 1:1 idêntica aos modelos do jogo.
