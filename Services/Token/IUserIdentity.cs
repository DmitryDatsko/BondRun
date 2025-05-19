namespace BondRun.Services.Token;

public interface IUserIdentity
{
    Guid GetIdByCookie(HttpRequest request);
    string GetAddressByCookie(HttpRequest request);
}