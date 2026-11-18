using Trag.Domain.Updates;

namespace Trag.Application.Ports.Updates;

// Asks Finity Software's update service about this version of Trag; null when Trag could not learn the answer.
public interface IUpdateChecker
{
    Task<UpdateOffer?> CheckAsync(string version);
}
