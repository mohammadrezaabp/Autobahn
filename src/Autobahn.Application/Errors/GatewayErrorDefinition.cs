namespace Autobahn.Application.Errors;
public sealed record GatewayErrorDefinition(int HttpStatusCode, string Type, string DefaultMessage, int Phase, string? Param = null);
