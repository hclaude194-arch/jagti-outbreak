using UnityEditor;
using UnityEngine;

namespace Jagti.EditorTools
{
    /// <summary>
    /// Import settings for the sound library (Assets/Resources/Audio, made by tools/audio/build_sounds.py):
    /// music streams from disk; ambience beds and other long loops stay compressed in memory; short
    /// effects are decompressed on load so a burst of gunfire costs no decoding. Clips load in the
    /// background so a scene doesn't wait on them.
    /// </summary>
    public class AudioImportSettings : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith("Assets/Resources/Audio/")) return;
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            if (path.Contains("/music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.quality = 0.75f;
            }
            else if (path.Contains("/ambience/") || path.Contains("_loop") || path.Contains("/vehicles/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.7f;
            }
            else if (path.Contains("/voice/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.quality = 0.8f;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.quality = 0.8f;
            }
            importer.defaultSampleSettings = s;
            importer.loadInBackground = true;
        }
    }
}
