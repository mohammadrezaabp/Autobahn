using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IAdminSummaryReader
{
    AdminSummarySnapshot GetSnapshot();
}
