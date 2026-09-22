using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private string _graphicsQuality = "High";
    public string GraphicsQuality => _graphicsQuality;

    public void SetGraphicsQuality(string value)
    {
        _graphicsQuality = GraphicsProfile.Normalize(value);
        _settingsGraphicsQuality?.Select(_graphicsQuality == "Performance" ? 1 : 0);
        ApplyGraphicsQuality();
    }

    private void ApplyGraphicsQuality()
    {
        if (_worldEnvironment is null || _sun is null) return;
        GraphicsProfile.Apply(GetViewport(), _worldEnvironment.Environment, _sun, _graphicsQuality, _reduceEffects);
        _openingLighting?.Animate(0, _clock.Paused, _reduceEffects, _graphicsQuality);
        _verdantAtmosphere?.Animate(0, _clock.Paused, _reduceEffects, _graphicsQuality);
        _cinderAtmosphere?.Animate(0, _clock.Paused, _reduceEffects, _graphicsQuality);
        bool high = _graphicsQuality == "High";
        RenderingServer.DirectionalShadowAtlasSetSize(high ? 4096 : 2048, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(high
            ? RenderingServer.ShadowQuality.SoftHigh : RenderingServer.ShadowQuality.SoftLow);
    }
}
