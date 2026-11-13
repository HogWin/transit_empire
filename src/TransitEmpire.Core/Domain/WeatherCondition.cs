namespace TransitEmpire.Core.Domain;

/// <summary>
/// Condições climáticas dinâmicas que afetam a aderência e velocidade máxima nas estradas,
/// incentivando o jogador a pavimentar rotas críticas de terra batida.
/// </summary>
public enum WeatherCondition : byte
{
    Clear = 0,       // Tempo limpo: 100% da velocidade em todas as vias
    Rain = 1,        // Chuva moderada: Terra batida perde 40% de velocidade, Cascalho perde 15%
    HeavyStorm = 2   // Tempestade severa: Terra batida perde 60%, Cascalho 30%, Asfalto 10%
}

public static class WeatherConditionExtensions
{
    /// <summary>
    /// Retorna o multiplicador de velocidade (0.0 a 1.0) de acordo com o clima e o piso da via.
    /// </summary>
    public static float GetWeatherSpeedMultiplier(this RoadTier tier, WeatherCondition weather)
    {
        if (weather == WeatherCondition.Clear)
            return 1.0f;

        if (weather == WeatherCondition.Rain)
        {
            return tier switch
            {
                RoadTier.Dirt => 0.60f,        // Lamaçal em estrada de terra
                RoadTier.Gravel => 0.85f,      // Cascalho drena parcialmente
                RoadTier.Asphalt => 0.98f,     // Asfalto mantém ótima aderência
                RoadTier.Highway => 1.0f,
                RoadTier.DualCarriage => 1.0f,
                _ => 1.0f
            };
        }

        // HeavyStorm
        return tier switch
        {
            RoadTier.Dirt => 0.40f,
            RoadTier.Gravel => 0.70f,
            RoadTier.Asphalt => 0.90f,
            RoadTier.Highway => 0.95f,
            RoadTier.DualCarriage => 0.98f,
            _ => 1.0f
        };
    }
}

