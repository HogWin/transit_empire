namespace TransitEmpire.Core.Domain;

/// <summary>
/// Tipos de cargas e recursos transportados no jogo.
/// </summary>
public enum ResourceType : byte
{
    None = 0,
    
    // Matérias-Primas Básicas
    Grain = 1,          // Produzido por Fazendas
    Timber = 2,         // Produzido por Florestas de Corte
    IronOre = 3,        // Produzido por Minas de Ferro
    CrudeOil = 4,       // Produzido por Poços de Petróleo
    Coal = 5,           // Produzido por Minas de Carvão (coque siderúrgico)
    
    // Produtos Intermediários e Finais
    Food = 10,          // Produzido em Moinhos/Padarias (a partir de Grain)
    Lumber = 11,        // Produzido em Serrarias (a partir de Timber)
    Steel = 12,         // Produzido em Siderúrgicas (IronOre + Coal)
    Fuel = 13,          // Produzido em Refinarias (a partir de CrudeOil)
    Goods = 14,         // Bens de consumo produzidos em Fábricas
    Machinery = 15      // Materiais de Construção & Maquinário Tier 3 (Steel + Lumber + Fuel)
}

