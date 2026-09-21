using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VolunteerCoordinator.Application;
using VolunteerCoordinator.Application.Notifications;
using VolunteerCoordinator.Application.Ports;
using VolunteerCoordinator.Infrastructure.Notifications;
using VolunteerCoordinator.Infrastructure.Persistence;
using VolunteerCoordinator.Infrastructure.Security;
using VolunteerCoordinator.Infrastructure.Time;

namespace VolunteerCoordinator.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVolunteerCoordinatorInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<VolunteerCoordinatorDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(VolunteerCoordinatorDbContext).Assembly.FullName)));
        services.AddScoped<IWorkflowStore, EfWorkflowStore>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenService, SecureTokenService>();
        services.AddScoped<INotificationService, OutboxNotificationService>();
        services.AddScoped<ITransactionalEmailProvider, UnavailableTransactionalEmailProvider>();
        services.AddSingleton<IEmailTemplateRenderer, SafeEmailTemplateRenderer>();
        services.AddSingleton<ITransientLinkMaterialStore, TransientLinkMaterialStore>();
        services.AddOptions<NotificationDeliveryOptions>()
            .Validate(static options => options.IsValid(), "Notification delivery settings are invalid.")
            .ValidateOnStart();
        services.AddHostedService<NotificationDeliveryHostedService>();
        services.AddScoped<VolunteerCoordinatorService>();
        services.AddScoped<ResendWebhookProcessor>();
        return services;
    }
}
