using UnityEngine;

namespace YxpDefeatFinishers
{
    // The generator admits exact PCM16 mono22050 WAVs with44-byte headers.
    // Convert only those known sample bytes into AudioClip's required float[].
    // The caller owns and destroys the returned Unity object with its movie.
    internal static class FinisherMediaAudio
    {
        internal static AudioClip TuRagingDemon()
        {
            return Create(FinisherMedia.TuRagingDemon(), 149940, "Tu Raging Demon");
        }

        internal static AudioClip LinRoadroller()
        {
            return Create(FinisherMedia.LinRoadroller(), 244755, "Lin Roadroller");
        }

        private static AudioClip Create(byte[] wav, int sampleCount, string name)
        {
            float[] samples = new float[sampleCount];
            for (int sample = 0, offset = 44; sample < sampleCount; sample++, offset += 2)
            {
                int value = wav[offset] | (wav[offset + 1] << 8);
                if (value >= 32768) value -= 65536;
                samples[sample] = value * (1f / 32768f);
            }
            AudioClip clip = AudioClip.Create(name, sampleCount, 1, 22050, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
