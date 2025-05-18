namespace BondRun.Services.Token;

public interface IUserIdentity
{
    Guid GetIdByCookie(HttpRequest request);
}