using System.Data.Common;

namespace ForgeDeck.Core.Persistence;

public interface IDbConnectionFactory
{
    DbConnection Open();
}
