namespace OriathHub.Plugins.SampleHelloWorld
{
    using System.Numerics;

    /// <summary>
    ///     Persisted settings for the <see cref="SampleHelloWorldPlugin"/>.
    ///     Plain public fields serialized via Newtonsoft.Json — the same convention the host uses.
    /// </summary>
    public sealed class SampleHelloWorldSettings
    {
        /// <summary>Show the info window while in-game.</summary>
        public bool Show = true;

        /// <summary>Text colour of the info window (RGBA, 0–1).</summary>
        public Vector4 TextColor = new(0.4f, 1f, 0.4f, 1f);
    }
}
