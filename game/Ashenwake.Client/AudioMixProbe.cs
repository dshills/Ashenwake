using System.Buffers.Binary;
using Godot;

namespace Ashenwake.Client;

internal sealed record AudioMixMeasurement(string Fixture, bool QuietMode, int Frames, double InputPeak,
    double OutputPeak, double SteadyRms);

/// <summary>Native diagnostic of the shipping Master DSP chain. Captures precede the
/// Master fader, which is muted throughout so stress signals never reach the speakers.</summary>
internal static class AudioMixProbe
{
    internal static async Task<IReadOnlyList<AudioMixMeasurement>> Run(Node owner, Action<string, bool> check)
    {
        string[] buses = [ClientAudio.MasterBus, ClientAudio.MusicBus, ClientAudio.EffectsBus, ClientAudio.InterfaceBus];
        var levels = buses.Select(name => (Index: AudioServer.GetBusIndex(name),
            Db: AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex(name)), Muted: AudioServer.IsBusMute(AudioServer.GetBusIndex(name)))).ToArray();
        bool originalQuiet = ClientAudio.QuietModeEnabled;
        int master = levels[0].Index;
        var input = new AudioEffectCapture { BufferLength = .5f, ResourceName = "AudioMixProbeInput" };
        var output = new AudioEffectCapture { BufferLength = .5f, ResourceName = "AudioMixProbeOutput" };
        var players = new List<AudioStreamPlayer>();
        var results = new List<AudioMixMeasurement>();
        using var loud = Tone(.8);
        using var soft = Tone(.012);
        using var legacyAttack = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Data = CombatAudio.CreateSamples("blade") };
        try
        {
            ClientAudio.ApplyVolumes(1, 1, 1, 1);
            AudioServer.SetBusMute(master, true);
            AudioServer.AddBusEffect(master, input, 0);
            AudioServer.AddBusEffect(master, output);
            foreach (bool quiet in new[] { false, true })
            {
                ClientAudio.ApplyQuietMode(quiet);
                Add(loud, ClientAudio.MusicBus, 0); Add(loud, ClientAudio.EffectsBus, 0); Add(loud, ClientAudio.InterfaceBus, 0);
                results.Add(await Measure("summed_category_overload", quiet));
                ClearPlayers();
                Add(soft, ClientAudio.EffectsBus, 0);
                results.Add(await Measure("quiet_detail", quiet));
                ClearPlayers();
                // Conservative busy encounter: all three music stems at full layer gain,
                // ambience and overlapping warnings/impacts, before any music ducking.
                foreach (string stem in OpeningScore.StemNames) Add(OpeningScore.GetStream("hollow_breach", stem), ClientAudio.MusicBus, OpeningAudio.MusicBaseDb);
                Add(HollowAmbience.GetStream("hollow_breach"), ClientAudio.MusicBus, -29);
                Add(legacyAttack, ClientAudio.EffectsBus, -12);
                var reward = RewardAudio.Metadata("secret_treasure");
                Add(RewardAudio.GetStream(reward.Id), reward.Bus, reward.GainDb);
                foreach (string cue in new[] { "breach_phase3", "breach_sweep", "seal_broken", "impact_spell", "impact_weapon", "step_stone_1" })
                {
                    var metadata = OpeningFoley.Describe(cue);
                    Add(OpeningFoley.GetStream(cue), ClientAudio.EffectsBus, metadata.SuggestedGainDb);
                }
                results.Add(await Measure("busy_hollow_encounter", quiet));
                ClearPlayers();
            }
            foreach (var result in results)
            {
                string label = result.Fixture + (result.QuietMode ? "_quiet" : "_normal");
                check("native_mix_" + label + "_has_real_audio", result.Frames > 1000 && result.InputPeak > .001 && result.SteadyRms > .0001);
                check("native_mix_" + label + "_stays_under_minus_one_db", double.IsFinite(result.OutputPeak) && result.OutputPeak <= Mathf.DbToLinear(-1) + .002);
            }
            var overload = results.Where(r => r.Fixture == "summed_category_overload").ToArray();
            check("native_mix_overload_really_exceeds_digital_full_scale", overload.All(r => r.InputPeak > 1.2));
            check("native_mix_quiet_mode_reduces_sustained_loud_overlap", overload[1].SteadyRms < overload[0].SteadyRms * .65);
            var detail = results.Where(r => r.Fixture == "quiet_detail").ToArray();
            check("native_mix_quiet_mode_retains_low_level_details", detail[1].SteadyRms >= detail[0].SteadyRms * .9 && detail[1].SteadyRms <= detail[0].SteadyRms * 1.1);
            return results;
        }
        finally
        {
            ClearPlayers();
            // Keep the output muted while the limiter's look-ahead and the audio
            // driver's pending block drain, including when an assertion failed.
            ulong drainUntil = Time.GetTicksMsec() + 150;
            while (Time.GetTicksMsec() < drainUntil) await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
            for (int index = AudioServer.GetBusEffectCount(master) - 1; index >= 0; index--)
            {
                var effect = AudioServer.GetBusEffect(master, index);
                if (effect == input || effect == output) AudioServer.RemoveBusEffect(master, index);
            }
            input.Dispose(); output.Dispose();
            ClientAudio.ApplyQuietMode(originalQuiet);
            foreach (var level in levels)
            {
                AudioServer.SetBusVolumeDb(level.Index, level.Db);
                AudioServer.SetBusMute(level.Index, level.Muted);
            }
        }

        void Add(AudioStream stream, string bus, float db)
        {
            var player = new AudioStreamPlayer { Stream = stream, Bus = bus, VolumeDb = db, ProcessMode = Node.ProcessModeEnum.Always };
            owner.AddChild(player); players.Add(player);
        }

        void ClearPlayers()
        {
            foreach (var player in players) { player.Stop(); player.Stream = null; player.QueueFree(); }
            players.Clear();
        }

        async Task<AudioMixMeasurement> Measure(string fixture, bool quiet)
        {
            // Let the previous envelope release before beginning a separate fixture.
            ulong waitUntil = Time.GetTicksMsec() + 700;
            while (Time.GetTicksMsec() < waitUntil) await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
            input.ClearBuffer(); output.ClearBuffer();
            foreach (var player in players) player.Play();
            ulong started = Time.GetTicksMsec();
            double inputPeak = 0, outputPeak = 0, sumSquares = 0;
            int frames = 0, steadyFrames = 0;
            while (Time.GetTicksMsec() - started < 1100)
            {
                await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
                foreach (var sample in input.GetBuffer(input.GetFramesAvailable()))
                    inputPeak = Math.Max(inputPeak, Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y)));
                var samples = output.GetBuffer(output.GetFramesAvailable());
                frames += samples.Length;
                foreach (var sample in samples)
                {
                    outputPeak = Math.Max(outputPeak, Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y)));
                    if (Time.GetTicksMsec() - started < 400) continue;
                    sumSquares += (double)sample.X * sample.X + (double)sample.Y * sample.Y;
                    steadyFrames++;
                }
            }
            return new(fixture, quiet, frames, inputPeak, outputPeak, steadyFrames == 0 ? 0 : Math.Sqrt(sumSquares / (steadyFrames * 2)));
        }
    }

    private static AudioStreamWav Tone(double amplitude)
    {
        const int rate = 22050;
        byte[] data = new byte[rate * 2];
        for (int frame = 0; frame < rate; frame++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(frame * 2, 2), (short)Math.Round(short.MaxValue * amplitude * Math.Sin(Math.Tau * 440 * frame / rate)));
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Data = data,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = rate
        };
    }
}
