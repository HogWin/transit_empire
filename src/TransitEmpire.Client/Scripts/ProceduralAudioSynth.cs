using Godot;
using System;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Client.Scripts;

/// <summary>
/// Sintetizador de áudio 100% procedural baseado em AudioStreamGenerator (sem arquivos .wav externos).
/// Gera cliques de obra, caixa registradora, notificações, feedback de UI e paisagem sonora ambiente.
/// </summary>
public partial class ProceduralAudioSynth : Node
{
    private enum Waveform
    {
        Sine,
        Triangle,
        Square,
        Noise,
        FilteredNoise
    }

    private struct SynthVoice
    {
        public bool Active;
        public Waveform Wave;
        public float Phase;
        public float FreqStart;
        public float FreqEnd;
        public float Age;
        public float Delay;
        public float Duration;
        public float Attack;
        public float Gain;
        public float Pan;
        public float LowPassState;
        public float FilterCutoff;
    }

    private const float SfxMixRate = 44100f;
    private const float AmbientMixRate = 22050f;
    private const int MaxVoices = 24;

    private AudioStreamPlayer _sfxPlayer = null!;
    private AudioStreamGeneratorPlayback? _sfxPlayback;

    private AudioStreamPlayer _ambientPlayer = null!;
    private AudioStreamGeneratorPlayback? _ambientPlayback;

    private readonly SynthVoice[] _voices = new SynthVoice[MaxVoices];
    private const int AudioBatchSize = 256;
    private readonly Vector2[] _sfxBatch = new Vector2[AudioBatchSize];
    private readonly Vector2[] _ambientBatch = new Vector2[AudioBatchSize];
    private readonly Vector2[] _silentBatch = new Vector2[AudioBatchSize];

    private uint _rngState = 0x5A17C93Du;
    private double _lastCashTimeSec = -10.0;
    private double _lastClickTimeSec = -10.0;

    // Estado da paisagem sonora ambiente
    private WeatherCondition _weather = WeatherCondition.Clear;
    private float _simTimeScale = 0f;
    private float _rainIntensity = 0f;
    private float _trafficIntensity = 0f;
    private float _windPhase = 0f;
    private float _humPhase1 = 0f;
    private float _humPhase2 = 0f;
    private float _brownNoise = 0f;
    private float _rainFilter = 0f;
    private float _thunderEnv = 0f;
    private float _thunderPhase = 0f;
    private float _thunderTimer = 9f;
    private float _birdEnv = 0f;
    private float _birdPhase = 0f;
    private float _birdBaseFreq = 2200f;
    private float _birdTimer = 5f;

    // Sprint E7 — Áudio Espacial 3D Dinâmico por Zoom (Diesel, Freio-Motor e Estéreo)
    private float _spatialZoom01 = 0f;
    private float _spatialSpeed01 = 0f;
    private bool _spatialEngineBrake = false;
    private float _spatialStereoPan = 0f;
    private float _enginePhase = 0f;
    private float _engineHarmonicPhase = 0f;
    private float _jakePulsePhase = 0f;
    private float _engineLowPass = 0f;
    private double _lastAirHornTimeSec = -10.0;

    public bool IsMuted { get; private set; } = false;
    public float MasterVolume { get; set; } = 0.85f;
    public float AmbientVolume { get; set; } = 0.28f;

    public override void _Ready()
    {
        var sfxGen = new AudioStreamGenerator
        {
            MixRate = SfxMixRate,
            BufferLength = 0.20f
        };
        _sfxPlayer = new AudioStreamPlayer
        {
            Name = "ProceduralSfxPlayer",
            Stream = sfxGen,
            VolumeDb = -2.0f
        };
        AddChild(_sfxPlayer);
        _sfxPlayer.Play();
        _sfxPlayback = _sfxPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;

        var ambGen = new AudioStreamGenerator
        {
            MixRate = AmbientMixRate,
            BufferLength = 0.30f
        };
        _ambientPlayer = new AudioStreamPlayer
        {
            Name = "ProceduralAmbientPlayer",
            Stream = ambGen,
            VolumeDb = -6.0f
        };
        AddChild(_ambientPlayer);
        _ambientPlayer.Play();
        _ambientPlayback = _ambientPlayer.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        if (_sfxPlayer != null) _sfxPlayer.StreamPaused = muted;
        if (_ambientPlayer != null) _ambientPlayer.StreamPaused = muted;
    }

    public override void _ExitTree()
    {
        _sfxPlayback = null;
        _ambientPlayback = null;
        if (IsInstanceValid(_sfxPlayer))
        {
            _sfxPlayer.Stop();
            _sfxPlayer.Stream = null;
        }
        if (IsInstanceValid(_ambientPlayer))
        {
            _ambientPlayer.Stop();
            _ambientPlayer.Stream = null;
        }
    }

    public void UpdateEnvironmentState(WeatherCondition weather, float timeScale)
    {
        _weather = weather;
        _simTimeScale = timeScale;
    }

    public void UpdateSpatialTruckAudio(
        float zoomProximity01,
        float normalizedSpeed01,
        bool isEngineBrakingOnHill,
        float stereoPan)
    {
        _spatialZoom01 = Math.Clamp(zoomProximity01, 0f, 1f);
        _spatialSpeed01 = Math.Clamp(normalizedSpeed01, 0f, 1f);
        _spatialEngineBrake = isEngineBrakingOnHill;
        _spatialStereoPan = Math.Clamp(stereoPan, -1f, 1f);
    }

    public void PlayAirHorn(float stereoPan = 0f, float distanceGain = 1.0f)
    {
        if (IsMuted) return;
        double now = Time.GetTicksMsec() * 0.001;
        if (now - _lastAirHornTimeSec < 0.85) return;
        _lastAirHornTimeSec = now;

        float gain = Math.Clamp(distanceGain, 0.15f, 1.0f);
        // Buzina a ar bitonal de caminhão pesado (F4 + Ab4 + leve queda de pressão no fim)
        SpawnVoice(Waveform.Triangle, 349.23f, 342f, 0.34f, 0.015f, 0.20f * gain, 0f, stereoPan, cutoff: 0.35f);
        SpawnVoice(Waveform.Triangle, 415.30f, 408f, 0.34f, 0.015f, 0.18f * gain, 0f, stereoPan, cutoff: 0.35f);
        SpawnVoice(Waveform.Square, 349.23f, 342f, 0.28f, 0.02f, 0.06f * gain, 0.01f, stereoPan, cutoff: 0.20f);
    }

    public override void _Process(double delta)
    {
        if (IsMuted) return;

        float dt = (float)delta;
        float targetRain = _weather switch
        {
            WeatherCondition.Rain => 0.45f,
            WeatherCondition.HeavyStorm => 0.95f,
            _ => 0.0f
        };
        _rainIntensity = Mathf.Lerp(_rainIntensity, targetRain, Mathf.Clamp(dt * 1.6f, 0f, 1f));

        float targetTraffic = _simTimeScale > 0.01f ? Mathf.Clamp(0.35f + _simTimeScale * 0.1f, 0.2f, 0.85f) : 0.05f;
        _trafficIntensity = Mathf.Lerp(_trafficIntensity, targetTraffic, Mathf.Clamp(dt * 2.5f, 0f, 1f));

        PumpSfxFrames();
        PumpAmbientFrames();
    }

    // =========================================================================
    // Gatilhos de Efeitos Sonoros (SFX)
    // =========================================================================

    public void PlayUiClick()
    {
        double now = Time.GetTicksMsec() * 0.001;
        if (now - _lastClickTimeSec < 0.03) return;
        _lastClickTimeSec = now;

        SpawnVoice(Waveform.Triangle, 920f, 520f, 0.032f, 0.002f, 0.20f, 0f, 0f);
    }

    public void PlayError()
    {
        SpawnVoice(Waveform.Triangle, 175f, 135f, 0.08f, 0.004f, 0.24f, 0f, 0f);
        SpawnVoice(Waveform.Triangle, 145f, 105f, 0.11f, 0.004f, 0.26f, 0.075f, 0f);
    }

    public void PlaySpeedChange(float scale)
    {
        if (scale <= 0.01f)
        {
            SpawnVoice(Waveform.Sine, 460f, 260f, 0.07f, 0.003f, 0.22f, 0f, 0f);
            return;
        }

        float freq = scale switch
        {
            < 1.5f => 580f,
            < 3.5f => 740f,
            _ => 940f
        };
        SpawnVoice(Waveform.Triangle, freq, freq * 1.15f, 0.055f, 0.002f, 0.20f, 0f, 0f);
    }

    public void PlayRoadBuild(RoadStructureType structure)
    {
        switch (structure)
        {
            case RoadStructureType.Bridge:
                // Pancada metálica/madeira dupla da ponte
                SpawnVoice(Waveform.Sine, 210f, 90f, 0.14f, 0.003f, 0.36f, 0f, -0.05f);
                SpawnVoice(Waveform.Triangle, 440f, 320f, 0.10f, 0.002f, 0.24f, 0.01f, 0.05f);
                SpawnVoice(Waveform.FilteredNoise, 1200f, 500f, 0.11f, 0.002f, 0.22f, 0f, 0f, cutoff: 0.22f);
                break;

            case RoadStructureType.Tunnel:
                // Detonação abafada + eco em rocha para túnel
                SpawnVoice(Waveform.Sine, 115f, 38f, 0.26f, 0.006f, 0.45f, 0f, 0f);
                SpawnVoice(Waveform.FilteredNoise, 800f, 180f, 0.28f, 0.008f, 0.34f, 0f, 0f, cutoff: 0.09f);
                SpawnVoice(Waveform.Sine, 185f, 110f, 0.14f, 0.01f, 0.16f, 0.09f, 0.12f);
                break;

            default:
                // Assentamento de terra / pedra da estrada
                SpawnVoice(Waveform.Sine, 155f, 58f, 0.11f, 0.002f, 0.35f, 0f, 0f);
                SpawnVoice(Waveform.FilteredNoise, 1400f, 600f, 0.085f, 0.002f, 0.24f, 0f, 0f, cutoff: 0.20f);
                break;
        }
    }

    public void PlayPaveUpgrade(RoadTier tier)
    {
        float pitchMult = tier switch
        {
            RoadTier.Gravel => 1.0f,
            RoadTier.Asphalt => 1.22f,
            RoadTier.Highway => 1.45f,
            RoadTier.DualCarriage => 1.70f,
            _ => 1.0f
        };

        SpawnVoice(Waveform.FilteredNoise, 1800f, 800f, 0.07f, 0.002f, 0.18f, 0f, 0f, cutoff: 0.28f);
        SpawnVoice(Waveform.Triangle, 330f * pitchMult, 440f * pitchMult, 0.09f, 0.003f, 0.22f, 0.02f, 0f);
    }

    public void PlayStructureBuilt()
    {
        // Martelo duplo + acorde positivo de inauguração
        SpawnVoice(Waveform.Sine, 190f, 75f, 0.10f, 0.002f, 0.32f, 0f, -0.1f);
        SpawnVoice(Waveform.Sine, 230f, 95f, 0.10f, 0.002f, 0.28f, 0.09f, 0.1f);
        SpawnVoice(Waveform.Triangle, 523.25f, 523.25f, 0.18f, 0.005f, 0.16f, 0.14f, -0.05f);
        SpawnVoice(Waveform.Triangle, 659.25f, 659.25f, 0.22f, 0.005f, 0.18f, 0.20f, 0.05f);
    }

    public void PlayCashRegister(long amount)
    {
        double now = Time.GetTicksMsec() * 0.001;
        if (now - _lastCashTimeSec < 0.14) return;
        _lastCashTimeSec = now;

        float boost = amount >= 1200 ? 1.12f : 1.0f;

        // "Ka-" (estalido mecânico da gaveta)
        SpawnVoice(Waveform.FilteredNoise, 2400f, 1600f, 0.045f, 0.001f, 0.16f, 0f, -0.08f, cutoff: 0.45f);
        // "-Ching!" (campainha dupla B5 -> E6 + harmônico brilhante)
        SpawnVoice(Waveform.Sine, 987.77f * boost, 987.77f * boost, 0.12f, 0.002f, 0.20f, 0.02f, -0.04f);
        SpawnVoice(Waveform.Sine, 1318.51f * boost, 1318.51f * boost, 0.30f, 0.002f, 0.26f, 0.065f, 0.06f);
        SpawnVoice(Waveform.Sine, 2637.02f * boost, 2637.02f * boost, 0.22f, 0.002f, 0.10f, 0.065f, 0.06f);
    }

    public void PlayNotification(bool celebratory = false)
    {
        if (celebratory)
        {
            // Tríade ascendente estilo pesquisa concluída / contrato / região
            SpawnVoice(Waveform.Sine, 523.25f, 523.25f, 0.16f, 0.004f, 0.20f, 0.00f, -0.10f);
            SpawnVoice(Waveform.Sine, 659.25f, 659.25f, 0.18f, 0.004f, 0.20f, 0.07f, 0.00f);
            SpawnVoice(Waveform.Sine, 783.99f, 783.99f, 0.26f, 0.004f, 0.24f, 0.14f, 0.10f);
            SpawnVoice(Waveform.Triangle, 1046.50f, 1046.50f, 0.30f, 0.006f, 0.16f, 0.21f, 0.00f);
        }
        else
        {
            // Notificação suave de dois tons (demanda, DRE, expansão de cidade)
            SpawnVoice(Waveform.Sine, 587.33f, 587.33f, 0.14f, 0.004f, 0.18f, 0.00f, -0.06f);
            SpawnVoice(Waveform.Sine, 880.00f, 880.00f, 0.22f, 0.004f, 0.20f, 0.08f, 0.06f);
        }
    }

    public void PlaySaveLoad(bool isLoad)
    {
        if (isLoad)
        {
            SpawnVoice(Waveform.Triangle, 440f, 440f, 0.10f, 0.003f, 0.18f, 0f, 0f);
            SpawnVoice(Waveform.Triangle, 554.37f, 554.37f, 0.12f, 0.003f, 0.18f, 0.06f, 0f);
            SpawnVoice(Waveform.Triangle, 659.25f, 659.25f, 0.18f, 0.003f, 0.20f, 0.12f, 0f);
        }
        else
        {
            SpawnVoice(Waveform.Triangle, 659.25f, 659.25f, 0.10f, 0.003f, 0.18f, 0f, 0f);
            SpawnVoice(Waveform.Triangle, 880f, 880f, 0.16f, 0.003f, 0.20f, 0.06f, 0f);
        }
    }

    // =========================================================================
    // Síntese de Vozes SFX (Zero-Allocation)
    // =========================================================================

    private void SpawnVoice(
        Waveform wave,
        float freqStart,
        float freqEnd,
        float duration,
        float attack,
        float gain,
        float delay = 0f,
        float pan = 0f,
        float cutoff = 0.25f)
    {
        if (IsMuted) return;

        int slot = -1;
        float oldestAge = -1f;
        for (int i = 0; i < MaxVoices; i++)
        {
            if (!_voices[i].Active)
            {
                slot = i;
                break;
            }
            if (_voices[i].Age > oldestAge)
            {
                oldestAge = _voices[i].Age;
                slot = i;
            }
        }

        _voices[slot] = new SynthVoice
        {
            Active = true,
            Wave = wave,
            Phase = 0f,
            FreqStart = freqStart,
            FreqEnd = freqEnd,
            Age = 0f,
            Delay = delay,
            Duration = MathF.Max(0.01f, duration),
            Attack = MathF.Max(0.001f, attack),
            Gain = gain,
            Pan = Math.Clamp(pan, -1f, 1f),
            LowPassState = 0f,
            FilterCutoff = Math.Clamp(cutoff, 0.02f, 0.95f)
        };
    }

    private void PumpSfxFrames()
    {
        if (_sfxPlayback == null) return;
        int available = _sfxPlayback.GetFramesAvailable();
        if (available <= 0) return;

        const float dtSample = 1f / SfxMixRate;

        bool anyVoice = false;
        for (int v = 0; v < MaxVoices; v++)
        {
            if (_voices[v].Active)
            {
                anyVoice = true;
                break;
            }
        }

        while (available > 0)
        {
            if (!anyVoice)
            {
                if (available >= AudioBatchSize)
                {
                    _sfxPlayback.PushBuffer(_silentBatch);
                    available -= AudioBatchSize;
                    continue;
                }

                for (int i = 0; i < available; i++)
                    _sfxPlayback.PushFrame(Vector2.Zero);
                break;
            }

            int chunk = Math.Min(available, AudioBatchSize);
            for (int i = 0; i < chunk; i++)
            {
                float mixL = 0f;
                float mixR = 0f;

                for (int v = 0; v < MaxVoices; v++)
                {
                    if (!_voices[v].Active) continue;

                    ref SynthVoice voice = ref _voices[v];
                    if (voice.Delay > 0f)
                    {
                        voice.Delay -= dtSample;
                        continue;
                    }

                    float progress = voice.Age / voice.Duration;
                    if (progress >= 1f)
                    {
                        voice.Active = false;
                        continue;
                    }

                    float freq = voice.FreqStart + (voice.FreqEnd - voice.FreqStart) * progress;
                    voice.Phase += freq * dtSample;
                    if (voice.Phase >= 1f) voice.Phase -= MathF.Floor(voice.Phase);

                    float raw = voice.Wave switch
                    {
                        Waveform.Sine => MathF.Sin(voice.Phase * MathF.Tau),
                        Waveform.Triangle => 1f - 4f * MathF.Abs(voice.Phase - 0.5f),
                        Waveform.Square => voice.Phase < 0.5f ? 0.65f : -0.65f,
                        Waveform.Noise => NextWhiteNoise(),
                        Waveform.FilteredNoise => UpdateLowPass(ref voice.LowPassState, NextWhiteNoise(), voice.FilterCutoff),
                        _ => 0f
                    };

                    float env = voice.Age < voice.Attack
                        ? voice.Age / voice.Attack
                        : MathF.Pow(1f - progress, 1.85f);

                    float amp = raw * env * voice.Gain * MasterVolume;
                    float leftGain = 0.707f * (1f - voice.Pan * 0.5f);
                    float rightGain = 0.707f * (1f + voice.Pan * 0.5f);
                    mixL += amp * leftGain;
                    mixR += amp * rightGain;

                    voice.Age += dtSample;
                }

                // Limiter suave para impedir clipping com múltiplos eventos simultâneos
                _sfxBatch[i] = new Vector2(MathF.Tanh(mixL), MathF.Tanh(mixR));
            }

            if (chunk == AudioBatchSize)
            {
                _sfxPlayback.PushBuffer(_sfxBatch);
            }
            else
            {
                for (int i = 0; i < chunk; i++)
                    _sfxPlayback.PushFrame(_sfxBatch[i]);
            }

            available -= chunk;
        }
    }

    // =========================================================================
    // Síntese de Paisagem Sonora Ambiente (Brisa, Tráfego, Chuva, Trovão)
    // =========================================================================

    private void PumpAmbientFrames()
    {
        if (_ambientPlayback == null) return;
        int available = _ambientPlayback.GetFramesAvailable();
        if (available <= 0) return;

        const float dtSample = 1f / AmbientMixRate;

        while (available > 0)
        {
            int chunk = Math.Min(available, AudioBatchSize);
            for (int i = 0; i < chunk; i++)
            {
                float white = NextWhiteNoise();

                // Brisa suave (ruído marrom modulado por LFO lento)
                _windPhase += 0.14f * dtSample;
                if (_windPhase >= 1f) _windPhase -= 1f;
                float windLfo = 0.55f + 0.45f * MathF.Sin(_windPhase * MathF.Tau);
                _brownNoise = Math.Clamp(_brownNoise * 0.992f + white * 0.045f, -0.5f, 0.5f);
                float breeze = _brownNoise * (0.28f + 0.30f * windLfo);

                // Zumbido sutil de rodovia/motor à distância quando a simulação está rodando
                _humPhase1 += 54f * dtSample;
                if (_humPhase1 >= 1f) _humPhase1 -= 1f;
                _humPhase2 += 81f * dtSample;
                if (_humPhase2 >= 1f) _humPhase2 -= 1f;
                float trafficHum = (MathF.Sin(_humPhase1 * MathF.Tau) * 0.6f + MathF.Sin(_humPhase2 * MathF.Tau) * 0.4f)
                    * 0.045f * _trafficIntensity;

                // Chuva procedural (ruído filtrado em faixa média-alta)
                _rainFilter = _rainFilter * 0.72f + white * 0.28f;
                float rainSample = _rainFilter * 0.26f * _rainIntensity;

                // Trovão distante ocasional em tempestades
                float thunderSample = 0f;
                if (_weather == WeatherCondition.HeavyStorm)
                {
                    _thunderTimer -= dtSample;
                    if (_thunderTimer <= 0f)
                    {
                        _thunderEnv = 1.0f;
                        _thunderTimer = 8f + (NextUnitRandom() * 11f);
                    }
                }
                if (_thunderEnv > 0.002f)
                {
                    _thunderPhase += 38f * dtSample;
                    if (_thunderPhase >= 1f) _thunderPhase -= 1f;
                    float rumble = MathF.Sin(_thunderPhase * MathF.Tau) * 0.65f + _brownNoise * 0.9f;
                    thunderSample = rumble * _thunderEnv * 0.34f;
                    _thunderEnv *= 0.99975f;
                }

                // Pássaro distante sutil quando o tempo está limpo
                float birdSample = 0f;
                if (_weather == WeatherCondition.Clear)
                {
                    _birdTimer -= dtSample;
                    if (_birdTimer <= 0f)
                    {
                        _birdEnv = 1.0f;
                        _birdBaseFreq = 2100f + NextUnitRandom() * 900f;
                        _birdTimer = 6f + NextUnitRandom() * 9f;
                    }
                }
                if (_birdEnv > 0.005f)
                {
                    float chirpFreq = _birdBaseFreq + MathF.Sin(_birdEnv * 28f) * 320f;
                    _birdPhase += chirpFreq * dtSample;
                    if (_birdPhase >= 1f) _birdPhase -= 1f;
                    birdSample = MathF.Sin(_birdPhase * MathF.Tau) * _birdEnv * (1f - _birdEnv) * 0.08f;
                    _birdEnv -= dtSample * 3.8f;
                    if (_birdEnv < 0f) _birdEnv = 0f;
                }

                // Sprint E7 — Motor Diesel Espacial + Freio-Motor em Serra com Pan Estéreo
                float engineSample = 0f;
                if (_spatialZoom01 > 0.02f && _simTimeScale > 0.01f)
                {
                    // Simula troca de marchas progressiva (4 faixas de rotação) conforme a velocidade
                    float gearBand = (_spatialSpeed01 * 3.6f) % 1.0f;
                    float rpmNorm = _spatialSpeed01 <= 0.02f ? 0.1f : Math.Clamp(0.28f + gearBand * 0.55f + _spatialSpeed01 * 0.22f, 0.15f, 1.0f);
                    float dieselHz = 28f + rpmNorm * 46f;

                    _enginePhase += dieselHz * dtSample;
                    if (_enginePhase >= 1f) _enginePhase -= 1f;
                    _engineHarmonicPhase += dieselHz * 2.0f * dtSample;
                    if (_engineHarmonicPhase >= 1f) _engineHarmonicPhase -= 1f;

                    float rawDiesel = MathF.Sin(_enginePhase * MathF.Tau) * 0.65f
                        + MathF.Sin(_engineHarmonicPhase * MathF.Tau) * 0.35f
                        + MathF.Tanh(MathF.Sin(_enginePhase * MathF.Tau) * 2.4f) * 0.30f;

                    float jakeBrake = 0f;
                    if (_spatialEngineBrake && _spatialSpeed01 > 0.12f)
                    {
                        _jakePulsePhase += 18.5f * dtSample;
                        if (_jakePulsePhase >= 1f) _jakePulsePhase -= 1f;
                        float gate = _jakePulsePhase < 0.48f ? 1.0f : 0.15f;
                        jakeBrake = (rawDiesel * 0.7f + white * 0.28f) * gate * 0.55f;
                    }

                    _engineLowPass = _engineLowPass * 0.82f + (rawDiesel + jakeBrake) * 0.18f;
                    engineSample = _engineLowPass * 0.52f * _spatialZoom01;
                }

                float mono = (breeze + trafficHum + rainSample + thunderSample + birdSample) * AmbientVolume * MasterVolume;
                // Leve estéreo espacial entre canais L/R + pan do motor diesel por posição na tela
                float stereoOffset = (_rainFilter - _brownNoise) * 0.03f * AmbientVolume;
                float engLeft = engineSample * MasterVolume * (1f - _spatialStereoPan * 0.65f);
                float engRight = engineSample * MasterVolume * (1f + _spatialStereoPan * 0.65f);
                _ambientBatch[i] = new Vector2(
                    MathF.Tanh(mono - stereoOffset + engLeft),
                    MathF.Tanh(mono + stereoOffset + engRight));
            }

            if (chunk == AudioBatchSize)
            {
                _ambientPlayback.PushBuffer(_ambientBatch);
            }
            else
            {
                for (int i = 0; i < chunk; i++)
                    _ambientPlayback.PushFrame(_ambientBatch[i]);
            }

            available -= chunk;
        }
    }

    private float NextWhiteNoise()
    {
        return NextUnitRandom() * 2f - 1f;
    }

    private float NextUnitRandom()
    {
        uint x = _rngState;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _rngState = x;
        return (x & 0x00FFFFFFu) / 16777215f;
    }

    private static float UpdateLowPass(ref float state, float input, float cutoff)
    {
        state += cutoff * (input - state);
        return state;
    }
}
