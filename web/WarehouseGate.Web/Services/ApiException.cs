namespace WarehouseGate.Web.Services;

public class ApiException : Exception
{
    public ApiException(string message) : base(message)
    {
    }
}
