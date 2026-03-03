
namespace MicroHttp.Registration
{
    public interface IRegistrationService
    {
        Task RegisterUserAsync(UserRegistrationInfo registrationInfo);
    }
}