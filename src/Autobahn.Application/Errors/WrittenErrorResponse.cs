namespace Autobahn.Application.Errors;

public sealed record WrittenErrorResponse(
    int HttpStatusCode,
    ErrorResult Body,
    string Json);
