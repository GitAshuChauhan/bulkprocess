using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Configuration
{
    public class ValidateOptionsHostedService : IHostedService
    {
        private readonly IServiceProvider _sp;
        public ValidateOptionsHostedService(IServiceProvider sp) => _sp = sp;
        public Task StartAsync(CancellationToken ct)
        {
            _ = _sp.GetRequiredService<IOptions<Worker.Configuration.ServiceBusOptions>>().Value;
            _ = _sp.GetRequiredService<IOptions<Worker.Configuration.ProcessingOptions>>().Value;
            _ = _sp.GetRequiredService<IOptions<Worker.Configuration.StorageOptions>>().Value;
            _ = _sp.GetRequiredService<IOptions<Worker.Configuration.DatabaseOptions>>().Value;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
