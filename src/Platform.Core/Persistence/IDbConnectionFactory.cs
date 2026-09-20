using System.Data.Common;

namespace Platform.Core.Persistence;

public interface IDbConnectionFactory
{
    DbConnection Open();
}
