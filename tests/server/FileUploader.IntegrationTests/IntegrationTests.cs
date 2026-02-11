using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;

namespace FileUploader.IntegrationTests
{
    public class IntegrationTests : IAsyncLifetime
    {
        private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(60);
        
        private DistributedApplication? _app;

        [Fact]
        public async Task TestHealthy()
        {
            var ct = TestContext.Current.CancellationToken;

            ArgumentNullException.ThrowIfNull(_app);
            
            await _app.StartAsync(ct)
                .WaitAsync(s_defaultTimeout, ct);

            var httpClient = _app.CreateHttpClient("api");

            await _app.ResourceNotifications
                .WaitForResourceAsync("api", cancellationToken: ct)
                .WaitAsync(s_defaultTimeout, ct);

            using var response = await httpClient.GetAsync("/health", ct);

            Assert.Equal(
                expected: HttpStatusCode.OK,
                actual: response.StatusCode);
        }

        public async ValueTask DisposeAsync()
        {
            if ( _app != null)
            {
                await _app.DisposeAsync();
            }
        }

        public async ValueTask InitializeAsync()
        {
            var ct = TestContext.Current.CancellationToken;

            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.FileUploader_AppHost>(ct);

            appHost.Services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFilter("Aspire", LogLevel.Warning);
                logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
            });

            appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
            {
                clientBuilder.AddStandardResilienceHandler();
            });

            _app = await appHost.BuildAsync(ct)
                .WaitAsync(s_defaultTimeout, ct);
        }

    }
}
