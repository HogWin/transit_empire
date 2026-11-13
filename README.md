# 🚚 Transit Empire

Um jogo de simulação e gerenciamento de transporte rodoviário em grade (*grid-based*), focado em alta performance, geração procedural de malha viária e crescimento urbano orgânico.

---

## 🏛️ Arquitetura do Projeto

O projeto é estruturado em duas camadas estritas:

```
transit_empire/
├── docs/                               # Especificações técnicas e modelos de domínio
│   ├── arquitetura_de_software.md
│   ├── modelos_de_dominio_e_algoritmos.md
│   └── catalogo_frota_e_modelagem_3d.md
├── src/
│   ├── TransitEmpire.Core/             # Núcleo de simulação (Headless, sem dependência de UI)
│   │   ├── Domain/                     # Entidades puras (City, Truck, Industry, Garage, LogisticsHub, LogisticsContract, WeatherCondition)
│   │   ├── Generation/                 # Algoritmos de geração procedural (World, Roads, Towns)
│   │   ├── Simulation/                 # Ticks, economia dinâmica, contratos, clima e expansão urbana
│   │   └── Pathfinding/                # Navegação A* de frotas com pesos climáticos e de tráfego
│   └── TransitEmpire.Client/           # Camada de apresentação gráfica e interface (Godot 4)
├── web_preview/                        # Edição 3D interativa completa (Three.js + WebGL Nativo)
│   ├── index.html                      # Simulador 3D completo do Império Logístico + Showroom in-game
│   ├── showroom.html                   # Showroom 3D dedicado da frota em tela cheia + Inspetor de pneus R22.5
│   └── trucks_showcase.html            # Showcase 3D WebGL autocontido (réplica 1:1 dos modelos do jogo)
└── tests/
    └── TransitEmpire.Core.Tests/       # Testes automatizados de simulação e desempenho
```

---

## ⚙️ Regras Centrais de Design & Funcionalidades

1. **Malha Viária Procedural:** O jogador **não constrói estradas do zero**. Apenas o jogo gera estradas inicialmente conectando as cidades e indústrias.
2. **Upgrades Viários (Individual e Corredor em Lote):** O papel do jogador nas estradas é melhorá-las estrategicamente (Terra Batida $\rightarrow$ Cascalho $\rightarrow$ Asfalto $\rightarrow$ Rodovia $\rightarrow$ Pista Dupla), inclusive usando **`Shift + Clique`** para pavimentar todo um ramal conectado com **10% de desconto de empreiteira**.
3. **Urbanismo Estilo *Transport Fever* & Expansão Urbana Orgânica:** As cidades possuem **Avenidas Urbanas Asfaltadas em Cruz** (com calçadas contínuas, faixas de pedestres, iluminação pública e arborização) que passam **entre** os quarteirões edificados. Os 6 arquétipos de edifícios 3D (Prefeitura Neoclássica com Torre do Relógio, Galeria Comercial com Toldos Listrados, Vila de Casas e Sobrados com Garagem Integrada, Torre Corporativa Skyscraper com Heliponto, Condomínio de Apartamentos com Sacadas de Vidro e Townhouses Geminadas) são orientados de frente para a avenida e crescem ao longo da malha viária conforme a cidade evolui.
4. **Complexos Industriais 3D & Integração Direta Rodovia-Fábrica:** Todas as 5 indústrias (Fazenda Agroindustrial com 3 Silos e Trator, Moinho Cerealista com Pás Treliçadas, Serraria com Pórtico Rolante, Fábrica Siderúrgica com Telhado Shed e 3 Docas, e Refinaria Petroquímica com Ilha de Carregamento) possuem **Portaria com Guarita, Cancela Automática, Balança Rodoviária Metálica e Rampa Asfaltada** integrada diretamente ao ramal de acesso da estrada adjacente.
5. **Construções Logísticas do Jogador:** O jogador constrói **Garagens** (compra e despacho de frota) e **Hubs Logísticos de Transbordo** (*Cross-Docking*, que bonificam fretes regionais em +20% e realizam manutenção preventiva gratuita nos caminhões em trânsito), ambos com rampas conectadas automaticamente à estrada.
6. **Economia Dinâmica, Contratos Expressos & Banco Central:**
   * Tarifas de frete dinâmicas baseadas na distância da rota, escassez na cidade destino e presença de Hubs Logísticos.
   * **Contratos Expressos Licitatórios** com metas de entrega, cronômetro e bônus generosos em R$ e XP.
   * **Sistema Bancário & Fechamento DRE Mensal** com tomada e amortização de empréstimos e apuração de juros.
7. **Clima Dinâmico & Ciclo Dia/Noite:** Eventos de chuva reduzem a aderência e velocidade em estradas de terra batida (-40%), incentivando a pavimentação asfáltica, e acionam automaticamente os faróis volumétricos 3D da frota.

