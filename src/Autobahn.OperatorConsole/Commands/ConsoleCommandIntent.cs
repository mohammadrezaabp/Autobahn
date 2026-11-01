namespace Autobahn.OperatorConsole.Commands;
public sealed record ConsoleCommandIntent(ConsoleCommandKind Kind, int Limit = 50, string? ModelId = null);
