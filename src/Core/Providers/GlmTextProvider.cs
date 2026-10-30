namespace Cothon.Core.Providers;

/// <summary>
/// Z.ai GLM over their OpenAI-compatible Chat Completions endpoint:
/// `https://api.z.ai/api/paas/v4/chat/completions`, `Authorization: Bearer`,
/// model `glm-5.3-flash`. Same wire dialect as OpenAI, priced an order of
/// magnitude lower; screens ride along since Flash is multimodal.
/// </summary>
public sealed class GlmTextProvider(string apiKey, HttpMessageHandler? transport = null)
    : OpenAiTextProvider(apiKey, "glm-5.3-flash", transport, "https://api.z.ai/api/paas/v4", "GLM");
