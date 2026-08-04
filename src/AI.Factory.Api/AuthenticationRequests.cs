namespace AI.Factory.Api;

public sealed record LoginRequest(string Username, string Password, string? ReturnUrl);
public sealed record LogoutRequest(string? ReturnUrl);
public sealed record SetActivationRequest(bool IsActive);
