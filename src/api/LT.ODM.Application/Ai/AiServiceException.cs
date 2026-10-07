namespace LT.ODM.Application.Ai;

/// <summary>The AI service failed or answered with something unusable. The message is safe to show.</summary>
public sealed class AiServiceException(string message, Exception? inner = null) : Exception(message, inner);
